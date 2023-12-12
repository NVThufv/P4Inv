header_type header_ff_tags_t {
   fields {
        preamble: 64;
	shortest_path: 8;
	path_length: 8;
        is_edge: 1;
        bfs_start: 1;
   }
}

header header_ff_tags_t ff_tags;

header_type header_bfs_t {
   fields {
        pkt_v1_curr: 8;
        pkt_v1_par: 8;
        pkt_v2_curr: 8;
        pkt_v2_par: 8;
        pkt_v3_curr: 8;
        pkt_v3_par: 8;
        pkt_v4_curr: 8;
        pkt_v4_par: 8;
   }
}
