#include <core.p4>
#define V1MODEL_VERSION 20200408
#include <v1model.p4>

#define INSTANCE_COUNT  65536

struct intrinsic_metadata_t {
    bit<48> ingress_global_timestamp;
    bit<16> mcast_grp;
    bit<16> egress_rid;
}

struct ingress_metadata_t {
    bit<1>  pkt_start;
    bit<8>  pkt_curr;
    bit<8>  pkt_par;
    bit<8>  out_port;
    bit<8>  if_out_failed;
    bit<1>  first_visit;
    bit<1>  failure_visit;
    bit<1>  is_completed;
    bit<32> starting_port;
    bit<32> all_ports;
    bit<32> out_port_xor;
}

header header_bfs_t {
    bit<32> inst;
    bit<8> pkt_v1_curr;
    bit<8> pkt_v1_par;
    bit<8> pkt_v2_curr;
    bit<8> pkt_v2_par;
    bit<8> pkt_v3_curr;
    bit<8> pkt_v3_par;
    bit<8> pkt_v4_curr;
    bit<8> pkt_v4_par;
}

header header_ff_tags_t {
    bit<64> preamble;
    bit<8>  shortest_path;
    bit<8>  path_length;
    bit<1>  is_edge;
    bit<1>  bfs_start;
}

struct metadata {
    @name(".local_metadata") 
    ingress_metadata_t local_metadata;
}

struct headers {
    @name(".bfsTag") 
    header_bfs_t     bfsTag;
    @name(".ff_tags") 
    header_ff_tags_t ff_tags;
}

register<bit<8>, bit<32>>(INSTANCE_COUNT) pkt_curr;
register<bit<8>, bit<32>>(INSTANCE_COUNT) pkt_par;

parser ParserImpl(packet_in packet, out headers hdr, inout metadata meta, inout standard_metadata_t standard_metadata) {
    @name(".start") state start {
        packet.extract(hdr.ff_tags);
        packet.extract(hdr.bfsTag);
        pkt_curr.read(meta.local_metadata.pkt_curr, hdr.bfsTag.inst);
        pkt_par.read(meta.local_metadata.pkt_par, hdr.bfsTag.inst);
        meta.local_metadata.pkt_start = hdr.ff_tags.bfs_start;
        transition accept;
    }
}

@name(".all_ports_status") register<bit<32>, bit<32>>(32w1) all_ports_status;

control ctrl_set_bfs_metadata(inout headers hdr, inout metadata meta, inout standard_metadata_t standard_metadata) {
    @name(".skip_failures") action skip_failures(bit<8> _working) {
        @assume[_working < 8w5] {}
        meta.local_metadata.out_port = _working;
    }
    @name(".xor_outport") action xor_outport(bit<32> _all_ports) {
        all_ports_status.read(meta.local_metadata.all_ports, (bit<32>)32w0);
        meta.local_metadata.out_port_xor = meta.local_metadata.all_ports ^ _all_ports;
    }
    @name(".set_next_port") action set_next_port() {
        meta.local_metadata.pkt_curr = meta.local_metadata.pkt_curr + 8w1;
        meta.local_metadata.out_port = meta.local_metadata.pkt_curr;
    }
    @name(".set_par_to_ingress") action set_par_to_ingress() {
        meta.local_metadata.pkt_par = (bit<8>)standard_metadata.ingress_port;
        pkt_par.write(hdr.bfsTag.inst, meta.local_metadata.pkt_par);
        meta.local_metadata.out_port = (bit<8>)standard_metadata.ingress_port;
        meta.local_metadata.first_visit = 1w1;
    }
    @name(".set_out_to_ingress") action set_out_to_ingress() {
        meta.local_metadata.out_port = meta.local_metadata.out_port;
        meta.local_metadata.failure_visit = 1w1;
    }
    @name(".go_to_next") action go_to_next() {
        meta.local_metadata.out_port = meta.local_metadata.pkt_par;
        meta.local_metadata.pkt_curr = 8w0;
        meta.local_metadata.is_completed = 1w1;
    }
    @name(".set_if_status") action set_if_status(bit<8> _value) {
        meta.local_metadata.if_out_failed = _value;
    }
    @name(".skip_parent") action skip_parent() {
        meta.local_metadata.out_port = meta.local_metadata.out_port + 8w1;
    }
    @name(".start_from_one") action start_from_one() {
        meta.local_metadata.out_port = 8w1;
        meta.local_metadata.is_completed = 1w0;
    }
    @name(".starting_port_meta") action starting_port_meta(bit<32> port_start) {
        meta.local_metadata.starting_port = port_start;
        all_ports_status.read(meta.local_metadata.all_ports, (bit<32>)32w0);
    }
    @name(".send_to_parent") action send_to_parent() {
        meta.local_metadata.out_port = meta.local_metadata.pkt_par;
        meta.local_metadata.pkt_curr = 8w0;
        meta.local_metadata.is_completed = 1w1;
    }
    @name(".outport_to_parent") action outport_to_parent() {
        meta.local_metadata.out_port = meta.local_metadata.pkt_par;
        meta.local_metadata.pkt_curr = 8w0;
        meta.local_metadata.is_completed = 1w1;
    }
    @name(".next_outport") action next_outport() {
        meta.local_metadata.out_port = meta.local_metadata.out_port + 8w1;
    }
    @name(".check_out_failed") table check_out_failed {
        actions = {
            skip_failures;
        }
        key = {
            meta.local_metadata.starting_port: ternary;
            meta.local_metadata.all_ports    : ternary;
        }
    }
    @name(".check_outport_status") table check_outport_status {
        actions = {
            xor_outport;
        }
    }
    @name(".curr_par_eq_neq_ingress") table curr_par_eq_neq_ingress {
        actions = {
            set_next_port;
        }
    }
    @name(".curr_par_eq_zero") table curr_par_eq_zero {
        actions = {
            set_par_to_ingress;
        }
    }
    @name(".curr_par_neq_in") table curr_par_neq_in {
        actions = {
            set_out_to_ingress;
        }
    }
    @name(".hit_depth") table hit_depth {
        actions = {
            go_to_next;
        }
        default_action = go_to_next();
    }
    @name(".if_status") table if_status {
        actions = {
            set_if_status;
        }
        key = {
            meta.local_metadata.starting_port: ternary;
            meta.local_metadata.out_port_xor : ternary;
        }
    }
    @name(".jump_to_next") table jump_to_next {
        actions = {
            skip_parent;
        }
    }
    @name(".out_eq_zero") table out_eq_zero {
        actions = {
            start_from_one;
        }
    }
    @name(".set_egress_port") table set_egress_port {
        actions = {
            starting_port_meta;
        }
        key = {
            meta.local_metadata.out_port: exact;
        }
    }
    @name(".set_parent_out") table set_parent_out {
        actions = {
            send_to_parent;
        }
        default_action = send_to_parent();
    }
    @name(".to_parent") table to_parent {
        actions = {
            outport_to_parent;
        }
        default_action = outport_to_parent();
    }
    @name(".try_next") table try_next {
        actions = {
            next_outport;
        }
    }
    apply {
        if (meta.local_metadata.pkt_curr == 8w0 && meta.local_metadata.pkt_par == 8w0) {
            curr_par_eq_zero.apply();
        } else {
            if ((bit<9>)meta.local_metadata.pkt_curr != standard_metadata.ingress_port && (bit<9>)meta.local_metadata.pkt_par != standard_metadata.ingress_port) {
                curr_par_neq_in.apply();
            } else {
                curr_par_eq_neq_ingress.apply();
                if (meta.local_metadata.out_port == 8w5) {
                    hit_depth.apply();
                }
                if (meta.local_metadata.is_completed == 1w0) {
                    set_egress_port.apply();
                    if (meta.local_metadata.out_port != 8w4) {
                        check_out_failed.apply();
                    } else {
                        check_outport_status.apply();
                        if_status.apply();
                        if (meta.local_metadata.if_out_failed == meta.local_metadata.out_port) {
                            try_next.apply();
                            if (meta.local_metadata.out_port == 8w5) {
                                to_parent.apply();
                            }
                        }
                    }
                    if (meta.local_metadata.is_completed == 1w0 && meta.local_metadata.out_port == meta.local_metadata.pkt_par) {
                        jump_to_next.apply();
                        if (meta.local_metadata.out_port == 8w5) {
                            set_parent_out.apply();
                        }
                    }
                }
                if (meta.local_metadata.is_completed == 1w1) {
                    if (meta.local_metadata.out_port == 8w0) {
                        out_eq_zero.apply();
                    }
                }
                @assert[meta.local_metadata.out_port < 8w5 || meta.local_metadata.out_port == meta.local_metadata.pkt_par] {}
            }
        }
    }
}

control egress(inout headers hdr, inout metadata meta, inout standard_metadata_t standard_metadata) {
    apply {
    }
}

control ingress(inout headers hdr, inout metadata meta, inout standard_metadata_t standard_metadata) {
    @name(".set_default_route") action set_default_route(bit<8> send_to) {
        meta.local_metadata.out_port = send_to;
    }
    @name(".towards_parent") action towards_parent() {
        pkt_curr.write(hdr.bfsTag.inst, meta.local_metadata.pkt_curr);
        standard_metadata.egress_spec = (bit<9>)meta.local_metadata.out_port;
        hdr.ff_tags.path_length = hdr.ff_tags.path_length + 8w1;
    }
    @name(".fwd") action fwd(bit<9> _port) {
        @assume[(bit<8>)_port < 8w5] {}
        standard_metadata.egress_spec = _port;
        pkt_curr.write(hdr.bfsTag.inst, (bit<8>)_port);
        hdr.ff_tags.path_length = hdr.ff_tags.path_length + 8w1;
    }
    @name(".send_in") action send_in() {
        standard_metadata.egress_spec = standard_metadata.ingress_port;
        hdr.ff_tags.path_length = hdr.ff_tags.path_length + 8w1;
    }
    @name(".send_on_parent") action send_on_parent() {
        standard_metadata.egress_spec = (bit<9>)meta.local_metadata.out_port;
        hdr.ff_tags.path_length = hdr.ff_tags.path_length + 8w1;
    }
    @name(".set_out_meta") action set_out_meta(bit<32> meta_state) {
        meta.local_metadata.starting_port = meta_state;
        all_ports_status.read(meta.local_metadata.all_ports, (bit<32>)32w0);
    }
    @name(".set_bfs_tags") action set_bfs_tags() {
        meta.local_metadata.pkt_start = 1w1;
        hdr.ff_tags.bfs_start = meta.local_metadata.pkt_start;
        meta.local_metadata.pkt_par = 8w0;
        pkt_par.write(hdr.bfsTag.inst, meta.local_metadata.pkt_par);
        meta.local_metadata.out_port = 8w1;
    }
    @name(".default_route") table default_route {
        actions = {
            set_default_route;
        }
        key = {
            standard_metadata.ingress_port: exact;
        }
    }
    @name(".fwd_parent") table fwd_parent {
        actions = {
            towards_parent;
        }
    }
    @name(".fwd_pkt") table fwd_pkt {
        actions = {
            fwd;
        }
        key = {
            meta.local_metadata.starting_port: ternary;
            meta.local_metadata.all_ports    : ternary;
        }
    }
    @name(".send_in_ingress") table send_in_ingress {
        actions = {
            send_in;
        }
    }
    @name(".send_parent") table send_parent {
        actions = {
            send_on_parent;
        }
    }
    @name(".set_out_port") table set_out_port {
        actions = {
            set_out_meta;
        }
        key = {
            meta.local_metadata.out_port: exact;
        }
    }
    @name(".start_bfs") table start_bfs {
        actions = {
            set_bfs_tags;
        }
    }
    @name(".ctrl_set_bfs_metadata") ctrl_set_bfs_metadata() ctrl_set_bfs_metadata_0;
    apply {
        if (hdr.bfsTag.isValid()) {
            if (meta.local_metadata.pkt_start == 1w0) {
                default_route.apply();
                if (meta.local_metadata.out_port == 8w0) {
                    start_bfs.apply();
                }
            } else {
                ctrl_set_bfs_metadata_0.apply(hdr, meta, standard_metadata);
            }
            if (meta.local_metadata.first_visit == 1w1) {
                send_parent.apply();
            }
            if (meta.local_metadata.failure_visit == 1w1) {
                send_in_ingress.apply();
            }
            if (meta.local_metadata.first_visit == 1w0 && meta.local_metadata.failure_visit == 1w0) {
                if (meta.local_metadata.is_completed == 1w0) {
                    set_out_port.apply();
                    fwd_pkt.apply();
                } else {
                    fwd_parent.apply();
                }
            }
        }
    }
}

control DeparserImpl(packet_out packet, in headers hdr) {
    apply {
        packet.emit(hdr.ff_tags);
        packet.emit(hdr.bfsTag);
    }
}

control verifyChecksum(inout headers hdr, inout metadata meta) {
    apply {
    }
}

control computeChecksum(inout headers hdr, inout metadata meta) {
    apply {
    }
}

V1Switch(ParserImpl(), verifyChecksum(), ingress(), egress(), computeChecksum(), DeparserImpl()) main;

