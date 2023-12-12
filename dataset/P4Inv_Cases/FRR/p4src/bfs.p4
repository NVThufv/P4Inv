#include "includes/headers.p4"

header_type ingress_metadata_t {
   fields {
	pkt_start: 1;
        pkt_curr: 8;
        pkt_par: 8;
        out_port: 8;
	if_out_failed: 8;
	first_visit: 1;
	failure_visit: 1;
	is_completed: 1;
	starting_port: 32;
	all_ports: 32;
	out_port_xor: 32;
  }
}

header  header_bfs_t bfsTag;
metadata ingress_metadata_t local_metadata;

#define PORTS 4

parser start {
  extract(ff_tags);
  extract(bfsTag);
  set_metadata(local_metadata.pkt_curr, bfsTag.pkt_v1_curr);
  set_metadata(local_metadata.pkt_par, bfsTag.pkt_v1_par);
  set_metadata(local_metadata.pkt_start, ff_tags.bfs_start);
  return ingress;
}

header_type intrinsic_metadata_t {
    fields {
        ingress_global_timestamp : 48;
        lf_field_list : 8;
        mcast_grp : 16;
        egress_rid : 16;
        resubmit_flag : 8;
        recirculate_flag : 8;
    }
}
metadata intrinsic_metadata_t intrinsic_metadata;

register all_ports_status {
  width: 32;
  instance_count: 1;
}

action set_par_to_ingress() {
   modify_field(local_metadata.pkt_par, standard_metadata.ingress_port); // par <- ingress_port
   modify_field(bfsTag.pkt_v1_par, local_metadata.pkt_par);
   modify_field(local_metadata.out_port, standard_metadata.ingress_port);
   modify_field(local_metadata.first_visit, 1);
}

table curr_par_eq_zero {
  actions{set_par_to_ingress;}
}

action send_on_parent() {
  modify_field(standard_metadata.egress_spec, local_metadata.out_port);
  add_to_field(ff_tags.path_length, 1);
}

table send_parent {
  actions {send_on_parent;}
}


action set_out_to_ingress() {
  modify_field(local_metadata.out_port, local_metadata.out_port);
  modify_field(local_metadata.failure_visit, 1);
}

table curr_par_neq_in {
  actions {set_out_to_ingress;}
}

action send_in() {
  modify_field(standard_metadata.egress_spec, standard_metadata.ingress_port);
  add_to_field(ff_tags.path_length, 1);
}

table send_in_ingress {
  actions {send_in;}
}

action set_next_port() {
  add_to_field(local_metadata.pkt_curr, 1); // curr <-- curr + 1
  modify_field(local_metadata.out_port, local_metadata.pkt_curr);
}

table curr_par_eq_neq_ingress {
  actions {set_next_port;}
}

action go_to_next() {
  modify_field(local_metadata.out_port, local_metadata.pkt_par);
  modify_field(local_metadata.pkt_curr, 0);
  modify_field(local_metadata.is_completed, 1);
}

table hit_depth {
  actions {go_to_next;}
}

action start_from_one() {
  modify_field(local_metadata.out_port, 1);
  modify_field(local_metadata.is_completed, 0);
}

table out_eq_zero {
  actions {start_from_one;}
}

action skip_parent() {
  add_to_field(local_metadata.out_port, 1);
}

table jump_to_next {
  actions {skip_parent;}
}

action outport_to_parent() {
  modify_field(local_metadata.out_port, local_metadata.pkt_par);
  modify_field(local_metadata.pkt_curr, 0);
  modify_field(local_metadata.is_completed, 1);
}

table to_parent {
  actions {outport_to_parent;}
}

action next_outport() {
  add_to_field(local_metadata.out_port, 1);
}

table try_next {
  actions {next_outport;}
}

action send_to_parent() {
  modify_field(local_metadata.out_port, local_metadata.pkt_par);
  modify_field(local_metadata.pkt_curr, 0);
  modify_field(local_metadata.is_completed, 1);
}

table set_parent_out {
  actions {send_to_parent;}
}


action towards_parent() {
  modify_field(bfsTag.pkt_v1_curr, local_metadata.pkt_curr);
  modify_field(standard_metadata.egress_spec, local_metadata.out_port);
  add_to_field(ff_tags.path_length, 1);
}

table fwd_parent {
  actions {towards_parent;}
}


action set_out_meta(meta_state) {
  modify_field(local_metadata.starting_port, meta_state);
  register_read(local_metadata.all_ports, all_ports_status, 0);
}

table set_out_port {
  reads {
        local_metadata.out_port: exact;
  }
  actions {
        set_out_meta;
  }
}

action fwd(_port) {
  modify_field(standard_metadata.egress_spec, _port);
  modify_field(bfsTag.pkt_v1_curr, _port);
  add_to_field(ff_tags.path_length, 1);
}

table fwd_pkt {
  reads {
        local_metadata.starting_port: ternary;
        local_metadata.all_ports: ternary;
  }
  actions {
        fwd;
  }
}

action skip_failures(_working) {
  modify_field(local_metadata.out_port, _working);
  //modify_field(bfsTag.pkt_v1_curr, local_metadata.pkt_curr);
  //modify_field(standard_metadata.egress_spec, port_to_send);
  //add_to_field(ff_tags.path_length, 1);
}

table check_out_failed {
  reads {
        local_metadata.starting_port: ternary;
        local_metadata.all_ports: ternary;
  }
  actions {
        skip_failures;
  }
}

action starting_port_meta(port_start) {
  modify_field(local_metadata.starting_port, port_start);
  register_read(local_metadata.all_ports, all_ports_status, 0);
}


table set_egress_port {
  reads {
        local_metadata.out_port: exact;
  }
  actions {
        starting_port_meta;
  }

}

action set_bfs_tags() {
  modify_field(local_metadata.pkt_start, 1);
  modify_field(ff_tags.bfs_start, local_metadata.pkt_start);
  modify_field(local_metadata.pkt_par, 0);
  modify_field(bfsTag.pkt_v1_par, local_metadata.pkt_par);
  modify_field(local_metadata.out_port, 1);
}

table start_bfs {
  actions {set_bfs_tags;}
}


action set_default_route(send_to) {
  modify_field(local_metadata.out_port, send_to);
}

table default_route {
  reads {
        standard_metadata.ingress_port: exact;
  }
  actions {
        set_default_route;
  }
}

action xor_outport(_all_ports) {
  register_read(local_metadata.all_ports, all_ports_status, 0);
  bit_xor(local_metadata.out_port_xor, local_metadata.all_ports, _all_ports);
}

table check_outport_status {
  actions{xor_outport;}
}

action set_if_status(_value) {
  modify_field(local_metadata.if_out_failed, _value);
}

table if_status {
  reads {
	local_metadata.starting_port: ternary;
        local_metadata.out_port_xor: ternary;
  }
  actions {
        set_if_status;
  }
}

control ctrl_set_bfs_metadata {
        if(local_metadata.pkt_curr == 0 and local_metadata.pkt_par == 0) {//initial round of bfs
                apply(curr_par_eq_zero);
        } else if(local_metadata.pkt_curr != standard_metadata.ingress_port and local_metadata.pkt_par != standard_metadata.ingress_port) {	
		apply(curr_par_neq_in);
	} else {
		apply(curr_par_eq_neq_ingress);
        	if(local_metadata.out_port == PORTS+1){// reached depth
         		apply(hit_depth); 
        	}
		if(local_metadata.is_completed == 0) {
			apply(set_egress_port); //in case of 0 to n-1 ports failed and then n becomes parent
			if(local_metadata.out_port != PORTS) { //shouldn't be sending again on parent, instead starts from n+1
				apply(check_out_failed);
			} else {
				apply(check_outport_status);
                                apply(if_status);
				if(local_metadata.if_out_failed == local_metadata.out_port) {
                                        apply(try_next);
                                        if(local_metadata.out_port == PORTS+1) {
                                                apply(to_parent);
                                        }
                                }
			
			}
			if(local_metadata.is_completed == 0 and local_metadata.out_port == local_metadata.pkt_par) {
                        	apply(jump_to_next);
                        	if(local_metadata.out_port == PORTS+1) {
                                	apply(set_parent_out);
                        	}
                	}
		}
		if(local_metadata.is_completed == 1) { 
        		if(local_metadata.out_port == 0) {
              			apply(out_eq_zero); 
			}
        	}

	}
}	

control ingress {
  if(valid(bfsTag)) {
   	if(local_metadata.pkt_start == 0) {
                apply(default_route);
                if(local_metadata.out_port == 0) {//default port failed
                        apply(start_bfs);
                } 
	} else {
       		ctrl_set_bfs_metadata();
	}
	if(local_metadata.first_visit == 1) {
                apply(send_parent); // save ingress as parent and send on ingress
        } 
	if(local_metadata.failure_visit == 1) {
                apply(send_in_ingress); //do nothing and send back on ingress
        }
	if(local_metadata.first_visit == 0 and local_metadata.failure_visit == 0) {
		if(local_metadata.is_completed == 0) {
            		apply(set_out_port);//check egress port status
			apply(fwd_pkt);
		} else {
			apply(fwd_parent);
		}
	}	
   }
}

control egress {}
