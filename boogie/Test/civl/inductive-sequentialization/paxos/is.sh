#!/bin/bash

# RUN: %parallel-boogie Paxos.bpl PaxosActions.bpl PaxosImpl.bpl PaxosSeq.bpl > "%t"
# RUN: %diff "%s.expect" "%t"

boogie $@ Paxos.bpl PaxosActions.bpl PaxosImpl.bpl PaxosSeq.bpl
