/*
Copyright 2013-present Barefoot Networks, Inc.

Licensed under the Apache License, Version 2.0 (the "License");
you may not use this file except in compliance with the License.
You may obtain a copy of the License at

    http://www.apache.org/licenses/LICENSE-2.0

Unless required by applicable law or agreed to in writing, software
distributed under the License is distributed on an "AS IS" BASIS,
WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
See the License for the specific language governing permissions and
limitations under the License.
*/

#include <stdio.h>
#include <string>
#include <iostream>
#include <fstream>
#include <sstream>

#include "ir/ir.h"
#include "ir/json_loader.h"
#include "lib/gc.h"
#include "lib/crash.h"
#include "lib/nullstream.h"
#include "backends/verify/translate/options.h"
#include "backends/verify/translate/version.h"
#include "backends/verify/translate/translate.h"
#include "backends/verify/translate/analyzer.h"
#include "backends/verify/translate/bmv2.h"
#include "backends/verify/translate/bf4_assertion.h"
#include "frontends/common/applyOptionsPragmas.h"
#include "frontends/common/parseInput.h"
#include "frontends/p4/frontend.h"
#include <time.h>

int main(int argc, char *const argv[]) {
    clock_t program_start = clock(), program_end;
    double program_cpu_time_used;
    setup_gc_logging();
    setup_signals();

    AutoCompileContext autoP4VerifyContext(new P4VerifyContext);
    auto& options = P4VerifyContext::get().options();
    options.langVersion = CompilerOptions::FrontendVersion::P4_16;
    options.compilerVersion = P4VERIFY_VERSION_STRING;

    if (options.process(argc, argv) != nullptr) {
            if (options.loadIRFromJson == false)
                    options.setInputFile();
    }
    if (::errorCount() > 0)
        return 1;
    const IR::P4Program *program = nullptr;
    auto hook = options.getDebugHook();
    if (options.loadIRFromJson == false) {
        program = P4::parseP4File(options);
        // std::cout << "parse time " << ((double) (clock() - program_start)) / CLOCKS_PER_SEC << " s\n";
        if (program == nullptr || ::errorCount() > 0)
            return 1;
        try {
            P4::P4COptionPragmaParser optionsPragmaParser;
            program->apply(P4::ApplyOptionsPragmas(optionsPragmaParser));

            P4::FrontEnd frontend;
            frontend.addDebugHook(hook);
            program = frontend.run(options, program);
        } catch (const std::exception &bug) {
            std::cerr << bug.what() << std::endl;
            return 1;
        }
        if (program == nullptr || ::errorCount() > 0)
            return 1;
    } else{
        std::ifstream json(options.file);
        if (json) {
            JSONLoader loader(json);
            const IR::Node* node = nullptr;
            loader >> node;
            if (!(program = node->to<IR::P4Program>()))
                error(ErrorType::ERR_INVALID, "%s is not a P4Program in json format", options.file);
        } else {
            error(ErrorType::ERR_IO, "Can't open %s", options.file); }
    }

    // cstring p4inv1 = "[](AP(drop))";
    // cstring p4inv2 = "Error!!!";
    // cstring p4inv3 = "[](AP(!drop) ==> AP((old(hdr.ipv4.ttl) != 0 && hdr.ipv4.ttl == old(hdr.ipv4.ttl) - 1) || (old(hdr.ipv4.ttl) == 0 && hdr.ipv4.ttl == 255)))";
    // std::vector<cstring> strings = {p4inv1, p4inv2, p4inv3};
    // // processing
    // for(int j = 0; j < strings.size(); ++j)
    // {
    //     std::cout << "\n======= Processing string " << j + 1 << "=======" << std::endl;
    //     // init scanner and parser
    //     std::stringstream inputStringStream;
    //     inputStringStream << strings[j];
    //     // std::istringstream inputStringStream(strings[j]);
    //     p4inv::Scanner scanner{ inputStringStream, std::cerr };
    //     p4inv::AstNode* root = nullptr;    // root is the parse result
    //     p4inv::p4invParser parser{ scanner, root};
    //     int result = parser.parse();
    //     if(result == 0 && root)
    //     {
    //         std::cout << "Gotcha! Here is the parse result: ";
    //         std::cout << root->toString() << std::endl;
    //         std::cout << "Now here's another example for getting all the atomic proposition: " << std::endl;
    //         // example of getting all the aps
    //         std::vector<p4inv::p4invAtomicProposition*> result = getAllAP(root);
    //         for(int i = 0; i < result.size(); ++i)
    //         {
    //             std::cout << "AP" << i + 1 << ": " + result[i]->toString() << std::endl;
    //         }
    //     }
    //     else
    //         std::cout << "Can't get root/Root is empty, maybe a parse error." << std::endl;
    // }


    clock_t backend_start, backend_end;
    double backend_cpu_time_used;
    backend_start = clock();

    std::ifstream bmv2cmds(options.cmdFile);
    BMV2CmdsAnalyzer* bMV2CmdsAnalyzer = nullptr;
    if(bmv2cmds){
        try{
            bMV2CmdsAnalyzer = new BMV2CmdsAnalyzer(&bmv2cmds);
        } catch (const char* msg) {
            std::cerr << msg << std::endl;
            return 1;
        }
    }
    
    // test bf4 instrument
    if(options.bf4Assertion) {
        P4::ReferenceMap refMap;
        P4::TypeMap typeMap;
        cstring testFileName = "instrumentP4.p4";
        auto assertionGenerator = new GenerateAssertions(
            &refMap, &typeMap
        );
        auto testP = program->apply(*assertionGenerator);
        std::cout << "Instrumenting bf4Assert...\n";
        std::ostream* testOut = openFile(testFileName, false);
        if (testOut != nullptr) {
            // (*testOut) << testP->toString();
            // testOut->flush();
            testP->dbprint(*testOut);
            std::cout << "Writing instrument result to " + testFileName + ".\n";
        }
        // std::cout << "Test done.\n";
        // return 0;
        program = testP;
    }
    
    std::ostream* out = openFile(options.outputBplFile, false);
    if (out != nullptr) {
        Translator translator(*out, options, bMV2CmdsAnalyzer);

        if(options.p4invSpec){
            std::ifstream fin(options.p4invFile);
            
            if(fin){
                std::string s;
                while(getline(fin, s)){
                    cstring key = nullptr;
                    // std::cout << s << std::endl;
                    for(int i = 0; i < P4INV_KEYS.size(); i++){
                        int idx = s.find(P4INV_KEYS[i]);
                        if(idx != -1){
                            s = s.substr(idx+P4INV_KEYS[i].size());
                            key = P4INV_KEYS[i];
                            break;
                        }
                    }
                    if(key == nullptr) continue;
                    //#reg_write
                    if(key == P4INV_KEYS[0]){
                        translator.addRegWrite(s);
                        continue;
                    }
                }
            }
        }

        translator.translate(program);
        if(options.addInvariant) {
            unsigned int traverseTimes = translator.expectedTraverseTimes;
            while(--traverseTimes)
                translator.translate(program);
        }
        translator.writeToFile();
        out->flush();
    }
    backend_end = clock();
    backend_cpu_time_used = ((double) (backend_end - backend_start)) / CLOCKS_PER_SEC;
    program_cpu_time_used = ((double) (backend_end - program_start)) / CLOCKS_PER_SEC;
    // std::cout << "backend done in " << DURATION(front)/1000.0 << " s\n";
    std::cout << "backend cpu time " << backend_cpu_time_used << " s\n";
    std::cout << "program cpu time " << program_cpu_time_used << " s\n";
    return 0;
}