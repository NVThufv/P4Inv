#include <core.p4>
#include <v1model.p4>

#include "includes/headers.p4"
#include "includes/metadata.p4"
#include "includes/parser.p4"
#include "includes/macros.p4"

#include "pipeline/flowselector.p4"


control ingress(inout headers hdr,
                inout metadata meta,
                inout standard_metadata_t standard_metadata) {

    /** Registers used by the Flow Selector **/
    register<bit<32>>(MAX_NB_PREFIXES*FLOWSELECTOR_NBFLOWS) flowselector_key;
    register<bit<32>>(MAX_NB_PREFIXES*FLOWSELECTOR_NBFLOWS) flowselector_nep;
    register<bit<9>>(MAX_NB_PREFIXES*FLOWSELECTOR_NBFLOWS) flowselector_ts;
    register<bit<19>>(MAX_NB_PREFIXES*FLOWSELECTOR_NBFLOWS) flowselector_last_ret;
    register<bit<4>>(MAX_NB_PREFIXES*FLOWSELECTOR_NBFLOWS) flowselector_last_ret_bin;
    register<bit<1>>(MAX_NB_PREFIXES*FLOWSELECTOR_NBFLOWS) flowselector_correctness;
    register<bit<2>>(MAX_NB_PREFIXES*FLOWSELECTOR_NBFLOWS) flowselector_fwloops;

    /** Registers used by the sliding window **/
    register<bit<6>>(MAX_NB_PREFIXES*(bit<32>)(SW_NB_BINS)) sw;
    register<bit<19>>(MAX_NB_PREFIXES) sw_time;
    register<bit<4>>(MAX_NB_PREFIXES) sw_index;
    register<bit<6>>(MAX_NB_PREFIXES) sw_sum;

    // Register to store the threshold for each prefix (by default all the prefixes
    // have the same threshold, so this could just be a macro)
    register<bit<6>>(MAX_NB_PREFIXES) threshold_registers;

    // List of next-hops for each prefix
    register<bit<32>>(MAX_NB_PREFIXES*3) next_hops_port;

    // Register used to indicate whether a next-hop is working or not.
    register<bit<1>>(MAX_NB_PREFIXES) nh_avaibility_1;
    register<bit<1>>(MAX_NB_PREFIXES) nh_avaibility_2;
    register<bit<1>>(MAX_NB_PREFIXES) nh_avaibility_3;

    // Register use to keep track for each flow, the number of flows that restart
    // after the rerouting. One per backup next-hop
    register<bit<6>>(MAX_NB_PREFIXES) nbflows_progressing_2;
    register<bit<6>>(MAX_NB_PREFIXES) nbflows_progressing_3;

    // Timestamp of the rerouting
    register<bit<19>>(MAX_NB_PREFIXES) rerouting_ts;

    // Every time period seconds (define by MAX_FLOWS_SELECTION_TIME), the
    // controller updates this register
    register<bit<48>>(32w1) timestamp_reference;

    // Switch IP used to reply to the traceroutes
    register<bit<32>>(32w1) switch_ip;

    bit<9> ts_second;

    bit<48> ts_tmp;
    bit<6> sum_tmp;
    bit<6> threshold_tmp;
    bit<6> correctness_tmp;
    bit<19> rerouting_ts_tmp;
    bit<2> flowselector_fwloops_tmp;
    bit<1> nh_avaibility_1_tmp;
    bit<1> nh_avaibility_2_tmp;
    bit<1> nh_avaibility_3_tmp;

    flowselector() fc;

    /**
    * Mark packet to drop
    */
    action _drop() {
        mark_to_drop(standard_metadata);
    }

    /**
    * Set the metadata used in the normal pipeline
    */
    action set_meta(bit<32> id, bit<1> use_blink, bit<32> default_nexthop_port) {
        meta.id = id;
        meta.use_blink = use_blink;
        meta.next_hop_port = default_nexthop_port;
    }

    table meta_fwtable {
        actions = {
            set_meta;
            _drop;
        }
        key = {
            hdr.ipv4.dstAddr: lpm;
            meta.bgp_ngh_type: exact;
        }
        size = 20000;
        default_action = _drop;
    }


    /**
    * Set the metadata about BGP (provider, peer or customer)
    */
    action set_bgp_tag(bit<2> neighbor_bgp_type) {
        meta.bgp_ngh_type = neighbor_bgp_type;
    }

    table bgp_tag {
        actions = {
            set_bgp_tag;
            NoAction;
        }
        key = {
            standard_metadata.ingress_port: exact;
            hdr.ethernet.srcAddr: exact;
        }
        size = 20000;
        default_action = NoAction; // By default bgp_ngh_type will be 0, meaning customer (used for the host)
    }

    /**
    * Set output port and destination MAC address based on port ID
    */
    action set_nh(bit<9> port, EthernetAddress smac, EthernetAddress dmac) {
        standard_metadata.egress_spec = port;
        hdr.ethernet.srcAddr = smac;
        hdr.ethernet.dstAddr = dmac;

        // Decrement the TTL by one
        hdr.ipv4.ttl = hdr.ipv4.ttl - 1;
    }

    table send {
        actions = {
            set_nh;
            _drop;
        }
        key = {
            meta.next_hop_port: exact;
        }
        size = 1024;
        default_action = _drop;
    }

    apply
    {
        timestamp_reference.read(ts_tmp, 32w0);

        // If the difference between the reference timestamp and the current
        // timestamp is above MAX_FLOWS_SELECTION_TIME, then reference timestamp
        // is updated
        if (standard_metadata.ingress_global_timestamp - ts_tmp > MAX_FLOWS_SELECTION_TIME)
        {
            timestamp_reference.write(32w0, standard_metadata.ingress_global_timestamp);
        }

        timestamp_reference.read(ts_tmp, 32w0);

        meta.ingress_timestamp_second =
            (bit<9>)((standard_metadata.ingress_global_timestamp - ts_tmp) >> 20);
        meta.ingress_timestamp_millisecond =
            (bit<19>)((standard_metadata.ingress_global_timestamp - ts_tmp) >> 10);

        bgp_tag.apply();
        meta_fwtable.apply();

        //Traceroute Logic (only for TCP probes)
        if (hdr.ipv4.isValid() && hdr.tcp.isValid() && hdr.ipv4.ttl == 1){

            // Set new headers valid
            hdr.ipv4_icmp.setValid();
            hdr.icmp.setValid();

            // Set egress port == ingress port
            standard_metadata.egress_spec = standard_metadata.ingress_port;

            //Ethernet: Swap map addresses
            bit<48> tmp_mac = hdr.ethernet.srcAddr;
            hdr.ethernet.srcAddr = hdr.ethernet.dstAddr;
            hdr.ethernet.dstAddr = tmp_mac;

            //Building new Ipv4 header for the ICMP packet
            //Copy original header (for simplicity)
            hdr.ipv4_icmp = hdr.ipv4;
            //Set destination address as traceroute originator
            hdr.ipv4_icmp.dstAddr = hdr.ipv4.srcAddr;
            //Set src IP to the IP assigned to the switch
            switch_ip.read(hdr.ipv4_icmp.srcAddr, 0);

            //Set protocol to ICMP
            hdr.ipv4_icmp.protocol = IP_ICMP_PROTO;
            //Set default TTL
            hdr.ipv4_icmp.ttl = 64;
            //And IP Length to 56 bytes (normal IP header + ICMP + 8 bytes of data)
            hdr.ipv4_icmp.totalLen= 56;

            //Create ICMP header with
            hdr.icmp.type = ICMP_TTL_EXPIRED;
            hdr.icmp.code = 0;

            //make sure all the packets are length 70.. so wireshark does not complain when tpc options,etc
            truncate((bit<32>)70);
        }
        else
        {
            // Get the threshold to use for fast rerouting (default is 32 flows)
            threshold_registers.read(threshold_tmp, meta.id);

            // If it is a TCP packet and destined to a destination that has Blink activated
            if (hdr.tcp.isValid() && meta.use_blink == 1w1)
            {
                // If it is a SYN packet, then we set the tcp_payload_len to 1
                // (even if the packet actually does not have any payload)
                if (hdr.tcp.syn == 1w1 || hdr.tcp.fin == 1w1)
                    meta.tcp_payload_len = 16w1;
                else
                    meta.tcp_payload_len = hdr.ipv4.totalLen - (bit<16>)(hdr.ipv4.ihl)*16w4 - (bit<16>)(hdr.tcp.dataOffset)*16w4; // ip_len - ip_hdr_len - tcp_hdr_len

                if (meta.tcp_payload_len > 0)
                {
                    fc.apply(hdr, meta, standard_metadata,
                        flowselector_key, flowselector_nep, flowselector_ts,
                        flowselector_last_ret, flowselector_last_ret_bin,
                        flowselector_correctness, flowselector_fwloops,
                        sw, sw_time, sw_index, sw_sum,
                        nbflows_progressing_2,
                        nbflows_progressing_3,
                        rerouting_ts);

                    sw_sum.read(sum_tmp, meta.id);
                    nh_avaibility_1.read(nh_avaibility_1_tmp, meta.id);

                    // Trigger the fast reroute if sum_tmp is greater than the
                    // threshold (i.e., default 31)
                    if (sum_tmp > threshold_tmp && nh_avaibility_1_tmp == 0)
                    {
                        // Write 1, to deactivate this next-hop
                        // and start using the backup ones
                        nh_avaibility_1.write(meta.id, 1);

                        // Initialize the registers used to check flow progression
                        nbflows_progressing_2.write(meta.id, 6w0);
                        nbflows_progressing_3.write(meta.id, 6w0);

                        // Storing the timestamp of the rerouting
                        rerouting_ts.write(meta.id, meta.ingress_timestamp_millisecond);
                    }
                }
            }

            if (meta.use_blink == 1w1)
            {
                nh_avaibility_1.read(nh_avaibility_1_tmp, meta.id);
                nh_avaibility_2.read(nh_avaibility_2_tmp, meta.id);
                nh_avaibility_3.read(nh_avaibility_3_tmp, meta.id);
                rerouting_ts.read(rerouting_ts_tmp, meta.id);

                // All the selected flows, within the first second after the rerouting.
                if (meta.selected == 1w1 && rerouting_ts_tmp > 0 &&
                    (meta.ingress_timestamp_millisecond -
                    rerouting_ts_tmp) < ((bit<19>)TIMEOUT_PROGRESSION))
                {
                    // Monitoring the first backup NH
                    if (meta.flowselector_cellid < (FLOWSELECTOR_NBFLOWS >> 1))
                    {
                        // If the backup next-hop is working so far
                        if (nh_avaibility_2_tmp == 1w0)
                        {
                            next_hops_port.read(meta.next_hop_port, (meta.id*3)+1);

                            if (meta.is_retransmission == 1w1) // If this is a retransmission
                            {
                                flowselector_fwloops.read(flowselector_fwloops_tmp,
                                    (FLOWSELECTOR_NBFLOWS * meta.id) + meta.flowselector_cellid);

                                // If a forwarding loop is detected for this flow
                                if (flowselector_fwloops_tmp == FWLOOPS_TRIGGER)
                                {
                                    // We switch to the third backup nexthop
                                    nh_avaibility_2.write(meta.id, 1);
                                    nh_avaibility_2_tmp = 1w1;
                                }
                                else
                                {
                                    flowselector_fwloops.write((FLOWSELECTOR_NBFLOWS * meta.id)
                                    + meta.flowselector_cellid, flowselector_fwloops_tmp + 1);
                                }
                            }
                        }
                        else
                        {
                            if (nh_avaibility_3_tmp == 1w0)
                            {
                                // Retrieve the port ID to use for that prefix
                                next_hops_port.read(meta.next_hop_port, (meta.id*3)+2);
                            }
                            else
                            {
                                next_hops_port.read(meta.next_hop_port, (meta.id*3)+0);
                            }
                        }

                    }
                    // Monitoring the second backup NH
                    else
                    {
                        // If the backup next-hop is working so far
                        if (nh_avaibility_3_tmp == 1w0)
                        {
                            next_hops_port.read(meta.next_hop_port, (meta.id*3)+2);

                            if (meta.is_retransmission == 1w1) // If this is a retransmission
                            {
                                flowselector_fwloops.read(flowselector_fwloops_tmp,
                                    (FLOWSELECTOR_NBFLOWS * meta.id) + meta.flowselector_cellid);

                                // If a forwarding loop is detected for this flow
                                if (flowselector_fwloops_tmp == FWLOOPS_TRIGGER)
                                {
                                    // We switch to the third backup nexthop
                                    nh_avaibility_3.write(meta.id, 1);
                                    nh_avaibility_3_tmp = 1w1;
                                }
                                else
                                {
                                    flowselector_fwloops.write((FLOWSELECTOR_NBFLOWS * meta.id)
                                    + meta.flowselector_cellid, flowselector_fwloops_tmp + 1);
                                }
                            }
                        }
                        else
                        {
                            if (nh_avaibility_2_tmp == 1w0)
                            {
                                // Retrieve the port ID to use for that prefix
                                next_hops_port.read(meta.next_hop_port, (meta.id*3)+1);
                            }
                            else
                            {
                                next_hops_port.read(meta.next_hop_port, (meta.id*3)+0);
                            }
                        }
                    }
                }
                // Else: All the flows of the prefixes monitored by Blink
                else
                {
                    if (nh_avaibility_1_tmp == 1w0)
                    {
                        // Retrieve the port ID to use for that prefix
                        next_hops_port.read(meta.next_hop_port, (meta.id*3)+0);
                    }
                    else if (nh_avaibility_2_tmp == 1w0)
                    {
                        next_hops_port.read(meta.next_hop_port, (meta.id*3)+1);
                    }
                    else if (nh_avaibility_3_tmp == 1w0)
                    {
                        next_hops_port.read(meta.next_hop_port, (meta.id*3)+2);
                    }
                    else
                    {
                        // If none of the backup next-hop is working, then we use primary next-hop
                        next_hops_port.read(meta.next_hop_port, (meta.id*3)+0);
                    }
                }

                // Check if after one second at least more than half of the flows have
                // restarted otherwise deactive the corresponding next-hop
                if (rerouting_ts_tmp > 0 && (meta.ingress_timestamp_millisecond -
                    rerouting_ts_tmp) > ((bit<19>)TIMEOUT_PROGRESSION))
                {
                    nbflows_progressing_2.read(correctness_tmp, meta.id);
                    if (correctness_tmp < MIN_NB_PROGRESSING_FLOWS && nh_avaibility_2_tmp == 0)
                    {
                        nh_avaibility_2.write(meta.id, 1);
                    }

                    nbflows_progressing_3.read(correctness_tmp, meta.id);
                    if (correctness_tmp < MIN_NB_PROGRESSING_FLOWS && nh_avaibility_3_tmp == 0)
                    {
                        nh_avaibility_3.write(meta.id, 1);
                    }
                }
            }

            send.apply();
        }
    }
}


/* ------------------------------------------------------------------------- */
control egress(inout headers hdr,
               inout metadata meta,
               inout standard_metadata_t standard_metadata) {

   apply { }
}

/* ------------------------------------------------------------------------- */
control verifyChecksum(inout headers hdr, inout metadata meta) {
    apply {
    }
}

/* ------------------------------------------------------------------------- */
control computeChecksum(inout headers hdr, inout metadata meta) {
    apply {
    	update_checksum(
    	    hdr.ipv4.isValid(),
                { hdr.ipv4.version,
    	          hdr.ipv4.ihl,
                  hdr.ipv4.dscp,
                  hdr.ipv4.ecn,
                  hdr.ipv4.totalLen,
                  hdr.ipv4.identification,
                  hdr.ipv4.flags,
                  hdr.ipv4.fragOffset,
                  hdr.ipv4.ttl,
                  hdr.ipv4.protocol,
                  hdr.ipv4.srcAddr,
                  hdr.ipv4.dstAddr },
                  hdr.ipv4.hdrChecksum,
                  HashAlgorithm.csum16);

        update_checksum(
        hdr.ipv4_icmp.isValid(),
            { hdr.ipv4_icmp.version,
              hdr.ipv4_icmp.ihl,
              hdr.ipv4_icmp.dscp,
              hdr.ipv4_icmp.ecn,
              hdr.ipv4_icmp.totalLen,
              hdr.ipv4_icmp.identification,
              hdr.ipv4_icmp.flags,
              hdr.ipv4_icmp.fragOffset,
              hdr.ipv4_icmp.ttl,
              hdr.ipv4_icmp.protocol,
              hdr.ipv4_icmp.srcAddr,
              hdr.ipv4_icmp.dstAddr },
              hdr.ipv4_icmp.hdrChecksum,
              HashAlgorithm.csum16);

        update_checksum(
        hdr.icmp.isValid(),
            { hdr.icmp.type,
              hdr.icmp.code,
              hdr.icmp.unused,
              hdr.ipv4.version,
	          hdr.ipv4.ihl,
              hdr.ipv4.dscp,
              hdr.ipv4.ecn,
              hdr.ipv4.totalLen,
              hdr.ipv4.identification,
              hdr.ipv4.flags,
              hdr.ipv4.fragOffset,
              hdr.ipv4.ttl,
              hdr.ipv4.protocol,
              hdr.ipv4.hdrChecksum,
              hdr.ipv4.srcAddr,
              hdr.ipv4.dstAddr,
              hdr.tcp.srcPort,
              hdr.tcp.dstPort,
              hdr.tcp.seqNo
              },
              hdr.icmp.checksum,
              HashAlgorithm.csum16);
        }
}

/* ------------------------------------------------------------------------- */
control DeparserImpl(packet_out packet, in headers hdr) {
    apply {
        packet.emit(hdr.ethernet);
        packet.emit(hdr.ipv4_icmp);
        packet.emit(hdr.icmp);
        packet.emit(hdr.ipv4);
        packet.emit(hdr.tcp);
    }
}

V1Switch(ParserImpl(),
    verifyChecksum(),
    ingress(),
    egress(),
    computeChecksum(),
    DeparserImpl()) main;
