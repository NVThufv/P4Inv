#define ETHERTYPE_IPV4                      16w0x800
#define IP_PROTOCOLS_TCP                    8w6

parser ParserImpl(packet_in pkt_in, out headers hdr,
    inout metadata meta,
    inout standard_metadata_t standard_metadata) {

    state start {
        pkt_in.extract(hdr.ethernet);
        transition select(hdr.ethernet.etherType) {
            ETHERTYPE_IPV4: parse_ipv4;
            // no default rule: all other packets rejected
        }
    }

    state parse_ipv4 {
        pkt_in.extract(hdr.ipv4);
        transition select(hdr.ipv4.protocol) {
            IP_PROTOCOLS_TCP: parse_tcp;
            default: accept;
        }
    }

    state parse_tcp {
        pkt_in.extract(hdr.tcp);

        transition accept;
    }
}
