#include "../includes/macros.p4"

control flowselector(inout headers hdr,
    inout metadata meta,
    inout standard_metadata_t standard_metadata,
    in register<bit<32>> flowselector_key, // Could be just 16 or something bits
    in register<bit<32>> flowselector_nep,
    in register<bit<9>> flowselector_ts,
    in register<bit<19>> flowselector_last_ret,
    in register<bit<4>> flowselector_last_ret_bin,
    in register<bit<1>> flowselector_correctness,
    in register<bit<2>> flowselector_fwloops,
    in register<bit<6>> sw,
    in register<bit<19>> sw_time,
    in register<bit<4>> sw_index,
    in register<bit<6>> sw_sum,
    in register<bit<6>> nbflows_progressing_2,
    in register<bit<6>> nbflows_progressing_3,
    in register<bit<19>> rerouting_ts)
{
    bit<32> newflow_key;
    bit<32> cell_id;

    bit<32> curflow_key;
    bit<9> curflow_ts;
    bit<32> curflow_nep;
    bit<19> ts_tmp;

    bit<4> index_tmp;
    bit<6> bin_value_tmp;
    bit<6> sum_tmp;
    bit<19> time_tmp;

    bit<32> flowselector_index;
    bit<19> last_ret_ts;
    bit<4> index_prev;

    bit<19> rerouting_ts_tmp;
    bit<1> flowselector_correctness_tmp;
    bit<6> correctness_tmp;

    bit<6> sw0;
    bit<6> sw1;
    bit<6> sw2;
    bit<6> sw3;
    bit<6> sw_sum_tmp;

    apply {
        // fix for meta.id not casue index overflow
        if(meta.id < MAX_NB_PREFIXES)
        {
            #include "sliding_window.p4"
            
            // Compute the hash for the flow key
            hash(newflow_key, HashAlgorithm.crc32, (bit<16>)0,
                {hdr.ipv4.srcAddr, hdr.ipv4.dstAddr, hdr.tcp.srcPort, hdr.tcp.dstPort, \
                HASH1_OFFSET}, (bit<32>)(TWO_POWER_32-1));
            newflow_key = newflow_key + 1;

            // Compute the hash for the cell id
            hash(cell_id, HashAlgorithm.crc32, (bit<16>)0,
                {hdr.ipv4.srcAddr, hdr.ipv4.dstAddr, hdr.tcp.srcPort, hdr.tcp.dstPort, \
                    HASH2_OFFSET}, (bit<32>)FLOWSELECTOR_NBFLOWS);

            meta.flowselector_cellid = cell_id;
            flowselector_index = (meta.id * FLOWSELECTOR_NBFLOWS) + cell_id;
            flowselector_key.read(curflow_key, flowselector_index);
            flowselector_ts.read(curflow_ts, flowselector_index);
            flowselector_nep.read(curflow_nep, flowselector_index);

            rerouting_ts.read(rerouting_ts_tmp, meta.id);

            if(flowselector_index < MAX_NB_PREFIXES*FLOWSELECTOR_NBFLOWS) {
                if (curflow_key == newflow_key && meta.ingress_timestamp_second >= curflow_ts)
                {
                    meta.selected = 1w1;

                    if (hdr.tcp.fin == 1w1)
                    {
                        // Retrieve the timestamp of the last retransmission
                        flowselector_last_ret.read(last_ret_ts, flowselector_index);

                        // Retrieve the timestamp of the current bin
                        sw_time.read(time_tmp, meta.id);

                        // If there was a retransmission during the last time window:
                        // remove it from the sliding window
                        if (((bit<48>)(meta.ingress_timestamp_millisecond - last_ret_ts)) <
                            (bit<48>)((bit<19>)(SW_NB_BINS-1)*(SW_BINS_DURATION)
                            + (meta.ingress_timestamp_millisecond - time_tmp))
                            && last_ret_ts > 0)
                        {
                            // Read the value of the previous index used for the previous retransmission
                            flowselector_last_ret_bin.read(index_prev, flowselector_index);

                            // Decrement the value in the previous bin in the sliding window,
                            // as well as the total sum
                            sw.read(bin_value_tmp, (meta.id*(bit<32>)SW_NB_BINS)+(bit<32>)index_prev);
                            sw_sum.read(sum_tmp, meta.id);

                            sw.write((meta.id*(bit<32>)SW_NB_BINS)+(bit<32>)index_prev, bin_value_tmp-1);
                            sw_sum.write(meta.id, sum_tmp-1);
                        }

                        flowselector_key.write(flowselector_index, 32w0);
                        flowselector_nep.write(flowselector_index, 32w0);
                        flowselector_ts.write(flowselector_index, 9w0);
                        flowselector_last_ret.write(flowselector_index, 19w0);
                        flowselector_correctness.write(flowselector_index, 1w0);
                        flowselector_fwloops.write(flowselector_index, 2w0);
                    }
                    else
                    {
                        // If it is a RETRANSMISSION
                        if (curflow_nep == hdr.tcp.seqNo + (bit<32>)meta.tcp_payload_len)
                        {
                            // Indicate that this packet is a retransmssion
                            meta.is_retransmission = 1;

                            // Retrieve the timestamp of the last retransmission
                            flowselector_last_ret.read(last_ret_ts, flowselector_index);

                            // Retrieve the timestamp of the current bin
                            sw_time.read(time_tmp, meta.id);

                            if (((bit<48>)(meta.ingress_timestamp_millisecond - last_ret_ts)) <
                                (bit<48>)((bit<19>)(SW_NB_BINS-1)*(SW_BINS_DURATION)
                                + (meta.ingress_timestamp_millisecond - time_tmp))
                                && last_ret_ts > 0)
                            {
                                // Read the value of the previous index used for the previous retransmission
                                flowselector_last_ret_bin.read(index_prev, flowselector_index);

                                // First, decrement the value in the previous bin in the sliding window
                                sw.read(bin_value_tmp, (meta.id*(bit<32>)SW_NB_BINS)+(bit<32>)index_prev);
                                sw_sum.read(sum_tmp, meta.id);

                                sw.write((meta.id*(bit<32>)SW_NB_BINS)+(bit<32>)index_prev, bin_value_tmp-1);
                                sw_sum.write(meta.id, sum_tmp-1);
                            }

                            // Then, increment the value in the current bin of the sliding window
                            sw_index.read(index_tmp, meta.id);
                            sw.read(bin_value_tmp, (meta.id*(bit<32>)SW_NB_BINS)+(bit<32>)index_tmp);
                            sw_sum.read(sum_tmp, meta.id);

                            sw.write((meta.id*(bit<32>)SW_NB_BINS)+(bit<32>)index_tmp, bin_value_tmp+1);
                            sw_sum.write(meta.id, sum_tmp+1);

                            // Update the timestamp of the last retransmission in the flowselector
                            sw_time.read(time_tmp, meta.id);
                            flowselector_last_ret.write(flowselector_index, meta.ingress_timestamp_millisecond);

                            // Read the value of the previous index used for the previous retransmission
                            flowselector_last_ret_bin.write(flowselector_index, index_tmp);
                        }
                        // If it is not a retransmission: Update the correctness register (if blink has rerouted)
                        else if (rerouting_ts_tmp > 19w0 && meta.ingress_timestamp_millisecond
                            - rerouting_ts_tmp < (bit<19>)TIMEOUT_PROGRESSION)
                        {
                            flowselector_correctness.read(flowselector_correctness_tmp,
                                (meta.id * FLOWSELECTOR_NBFLOWS) + meta.flowselector_cellid);

                            if (flowselector_correctness_tmp == 1w0)
                            {
                                if (meta.flowselector_cellid < 32)
                                {
                                    nbflows_progressing_2.read(correctness_tmp, meta.id);
                                    nbflows_progressing_2.write(meta.id, correctness_tmp+1);
                                }
                                else
                                {
                                    nbflows_progressing_3.read(correctness_tmp, meta.id);
                                    nbflows_progressing_3.write(meta.id, correctness_tmp+1);
                                }
                            }

                            flowselector_correctness.write(
                                (meta.id * FLOWSELECTOR_NBFLOWS) + meta.flowselector_cellid, 1w1);
                        }

                        flowselector_ts.write(flowselector_index, meta.ingress_timestamp_second);
                        flowselector_nep.write(flowselector_index, hdr.tcp.seqNo + (bit<32>)meta.tcp_payload_len);
                    }
                }
                else
                {
                    if (((curflow_key == 0) || (meta.ingress_timestamp_second
                        - curflow_ts) > FLOWSELECTOR_TIMEOUT || meta.ingress_timestamp_second
                        < curflow_ts) && hdr.tcp.fin == 1w0)
                    {
                        meta.selected = 1w1;

                        if (curflow_key > 0)
                        {
                            // Retrieve the timestamp of the last retransmission
                            flowselector_last_ret.read(last_ret_ts, flowselector_index);

                            // Retrieve the timestamp of the current bin
                            sw_time.read(time_tmp, meta.id);

                            // If there was a retransmission during the last time window:
                            // remove it from the sliding window
                            if (((bit<48>)(meta.ingress_timestamp_millisecond - last_ret_ts)) <
                                (bit<48>)((bit<19>)(SW_NB_BINS-1)*(SW_BINS_DURATION)
                                + (meta.ingress_timestamp_millisecond - time_tmp))
                                && last_ret_ts > 0)
                            {
                                // Read the value of the previous index used for the previous retransmission
                                flowselector_last_ret_bin.read(index_prev, flowselector_index);

                                // Decrement the value in the previous bin in the sliding window,
                                // as well as the total sum
                                sw.read(bin_value_tmp, (meta.id*(bit<32>)SW_NB_BINS)+(bit<32>)index_prev);
                                sw_sum.read(sum_tmp, meta.id);

                                sw.write((meta.id*(bit<32>)SW_NB_BINS)+(bit<32>)index_prev, bin_value_tmp-1);
                                sw_sum.write(meta.id, sum_tmp-1);
                            }
                        }

                        flowselector_key.write(flowselector_index, newflow_key);
                        flowselector_nep.write(flowselector_index, hdr.tcp.seqNo + (bit<32>)meta.tcp_payload_len);
                        flowselector_ts.write(flowselector_index, meta.ingress_timestamp_second);
                        flowselector_last_ret.write(flowselector_index, 19w0);
                        flowselector_correctness.write(flowselector_index, 1w0);
                        flowselector_fwloops.write(flowselector_index, 2w0);
                    }
                }
            }
            
        }
        // assertion
        sw.read(sw0, 0); sw.read(sw1, 1);
        sw.read(sw2, 2); sw.read(sw3, 3);
        sw_sum.read(sw_sum_tmp, 0);
        @assert[sw0 + sw1 == sw_sum_tmp - (sw2 + sw3)] {}
    }
}
