#ifndef BACKENDS_VERIFY_TRANSLATE_OPTIONS_H_
#define BACKENDS_VERIFY_TRANSLATE_OPTIONS_H_

#include <getopt.h>
#include "frontends/common/options.h"

const std::vector<cstring> P4INV_KEYS = {"//#register_write"};

class P4VerifyOptions : public CompilerOptions {
 public:
    bool translateOnly = false;
    bool addValidityAssertion = false;
    bool addForwardingAssertion = false;
    bool addBoundAssertion = false;
    bool useHeaderRef = false;
    
    bool loadIRFromJson = false;
    bool whileLoop = false;
    bool havocStatefulElements = false;
    bool addInvariant = false;
    cstring outputBplFile = nullptr;

    bool p4invSpec = false;
    cstring p4invFile = nullptr;

    bool bmv2cmds = false;
    cstring cmdFile = nullptr;

    bool gotoOrIf = false; // 1:goto, 0:if

    bool bv2int = false;
    bool bitBlasting = false;

    bool CpiIfElse = false;
    bool bf4Assertion = false;
    bool simpleConjecture = false;
    cstring inlineFunction = "{:inline 1}";
    // bool statelessVerify = false;

    P4VerifyOptions() {
        registerOption("--simpleConjecture", nullptr,
                       [this](const char*) {
                           simpleConjecture = true;
                           return true; },
                       "Experimental: Only generate simple conjecture.");
        registerOption("--bf4Assert", nullptr,
                       [this](const char*) {
                           bf4Assertion = true;
                           addInvariant = true;
                        //    whileLoop = true;
                        //    inlineFunction = "";
                        //    statelessVerify = true;
                           return true; },
                       "Generate assertion in bf4 style.");
        registerOption("--translate-only", nullptr,
                       [this](const char*) {
                           translateOnly = true;
                           return true; },
                       "only translate the P4 input into Boogie program");
        registerOption("-o", "outfile",
                      [this](const char* arg) { outputBplFile = arg; return true; },
                      "Write translation result to outfile");
        registerOption("--fromJSON", "file",
                       [this](const char* arg) {
                           loadIRFromJson = true;
                           file = arg;
                           return true;
                       },
                       "read previously dumped json instead of P4 source code");
        registerOption("--bmv2cmds", "file",
                       [this](const char* arg) {
                           bmv2cmds = true;
                           cmdFile = arg;
                           return true;
                       },
                       "read static bmv2 CLI commands together with the P4 program");
        registerOption("--assert-validity", nullptr,
                       [this](const char*) {
                           addValidityAssertion = true;
                           return true; },
                       "add assertions for header accessing");
        registerOption("--assert-forward", nullptr,
                       [this](const char*) {
                           addForwardingAssertion = true;
                           return true; },
                       "add assertions for egress_spec assignment");
        registerOption("--assert-bound", nullptr,
                       [this](const char*) {
                           addBoundAssertion = true;
                           return true; },
                       "add assertions for header stack out-of-bounds");
        registerOption("--ref", nullptr,
                       [this](const char*) {
                           useHeaderRef = true;
                           return true; },
                       "use header reference to access header fields instead of fields variables");
        
        /*
          Some Boogie compiler/verifier does not support goto statements.
          If so, use if-else statements instead.
        */
        registerOption("--gotoOrIf", nullptr,
                       [this](const char*) {
                           gotoOrIf = true;
                           return true; },
                       "1:goto, 0:if (default:0)");

        /*
          Some verifier does not support bit-vector theory.
          Use integer instead of bitvector
        */
        registerOption("--bv2int", nullptr,
                       [this](const char*) {
                           bv2int = true;
                           return true; },
                       "use integer instead of bitvector");

        /*
          Using integer is time-costing.
          Use bit-blasting algorithm instead.
        */
        registerOption("--bitBlasting", nullptr,
                       [this](const char*) {
                           bitBlasting = true;
                           return true; },
                       "use bit-blasting instead of bitvector");

        /*
          Use inv generator as backend
        */
       registerOption("--p4Inv", nullptr,
                       [this](const char*) {
                           whileLoop = true;
                           CpiIfElse = true;
                           addInvariant = true;
                           return true; },
                       "use invariant generator as backend");
        
        /*
          Use Boogie as backend
        */
       registerOption("--boogie", nullptr,
                       [this](const char*) {
                           whileLoop = true;
                           CpiIfElse = true;
                           return true; },
                       "use invariant generator as backend");

        registerOption("--p4invSpec", "file",
                      [this](const char* arg) { 
                          p4invFile = arg;
                          p4invSpec = true;
                          return true; },
                      "P4INV specification for the P4 program");

        /*
          Options for invariant generation
        */
        registerOption("--whileloop", nullptr,
                       [this](const char*) {
                           whileLoop = true;
                           return true; },
                       "consider the P4 program as a while loop with condition TRUE");
        registerOption("--add-invariant", nullptr,
                       [this](const char*) {
                           addInvariant = true;
                           return true; },
                       "add invariant for the while loop");
        registerOption("--infer-inv", nullptr,
                       [this](const char*) {
                           whileLoop = true;
                           addInvariant = true;
                           return true; },
                       "add required components for infering invariants");
        
        /*
          Add havoc statements at the enter of the while loop
          Used to show the method of traditional tools
          The stateful elements have to be random (without invariants)
        */
        registerOption("--havoc-se", nullptr,
                       [this](const char*) {
                           havocStatefulElements = true;
                           return true; },
                       "havoc stateful elements (counter, register, meter)");
    }
};

using P4VerifyContext = P4CContextWithOptions<P4VerifyOptions>;

#endif