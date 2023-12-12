import os
import subprocess
import time
import argparse

timeoutSecs = 86400

logDir = "./log"

translatorBinPath = "Translator/build/p4c-translator"
sorcarBinPath = "/home/p4ltl/Desktop/p4inv/sorcar/src/sorcar"
boogieBinPath = "boogie/Source/BoogieDriver/bin/Debug/net6.0/BoogieDriver"
z3Path = "/home/p4ltl/Desktop/z3/build/z3"

def makeLogDir(logDir):
    if not os.path.isdir(logDir):
        os.makedirs(logDir)
    print(f"---- Logs directory at {os.path.abspath(logDir)}. ----")

# SpecFile: regWrite + CPI
def testP4Inv(p4File, specFile, translateOutputPath=".", clean=True):
    if not os.path.isfile(p4File):
        print(f"Error: File {p4File} does not exist.")
        return

    if not os.path.isfile(specFile):
        print(f"Error: File {specFile} does not exist.")
        return

    # get the basename of the data directory
    dataDirName = os.path.basename(os.path.dirname(p4File))
    testLogDir = os.path.join(logDir, dataDirName)
    makeLogDir(testLogDir)
    
    p4FileName, _ = os.path.splitext(os.path.basename(p4File))
    
    def genTranslateOutput(file_name, prefix="boogie", simpleConjecture=False):
        outputPath = os.path.join(translateOutputPath, file_name)
        if prefix == "boogie":
            translateCmd = f"./{translatorBinPath} {p4File} --boogie --p4invSpec {specFile} -o {outputPath}"
        else:
            translateCmd = f"./{translatorBinPath} {p4File} --p4Inv --p4invSpec {specFile} -o {outputPath} {'--simpleConjecture' if simpleConjecture else ''}"
        print(f"{translateCmd}")
        outProcess = subprocess.run(translateCmd, timeout=timeoutSecs, shell=True, stdout=subprocess.PIPE, stderr=subprocess.STDOUT)
        strout = outProcess.stdout.decode()
        translateLogPath = os.path.join(testLogDir, f"log_translate_{prefix}_{p4FileName}.txt")
        with open(translateLogPath, "w") as f:
            f.write(strout)
            print(f"==== Translate log at {os.path.abspath(testLogDir)}. ====")
        outProcess.check_returncode()   # if return code non-zero, abort

    def runTool(cmd, prefix="boogie"):
        print(f"{cmd}")
        try:
            outProcess = subprocess.run(cmd, timeout=timeoutSecs, shell=True, stdout=subprocess.PIPE, stderr=subprocess.STDOUT)
            strout = outProcess.stdout.decode()
        except subprocess.TimeoutExpired as e:
            # print(f"Cannot open subprocess: {e}")
            strout = f"==== TIMEOUT for {timeoutSecs} seconds ====\n" +  e.stdout.decode()
            print("Timeout.")
        # endtry
        checkLogPath = os.path.join(testLogDir, f"log_{prefix}_check_{p4FileName}.txt")
        with open(checkLogPath, "w") as f:
            f.write(strout)
        print(f"==== {prefix} check log at {os.path.abspath(checkLogPath)}. ====")

    # Solely use boogie can be think as stateless verification
    # Boogie test file
    # boogieOutPath = "p4Inv_boogie.bpl"
    # boogieOutPath = os.path.join(testLogDir, boogieOutPath)
    # genTranslateOutput(boogieOutPath, prefix="boogie")

    # # run Boogie   
    # # boogieCheckCmd = f"time mono Boogie/Binaries/Boogie.exe -trace -nologo -noinfer -contractInfer {boogieOutPath}"
    # boogieCheckCmd = f"time {boogieBinPath} -trace -proverOpt:PROVER_PATH={z3Path} -contractInfer {boogieOutPath}"
    # runTool(boogieCheckCmd)

    # Sorcar test file -- stateful verification
    sorcarOutPath = f"p4Inv_{p4FileName}.bpl"
    sorcarOutPath = os.path.join(testLogDir, sorcarOutPath)
    genTranslateOutput(sorcarOutPath, prefix="p4Inv", simpleConjecture=False)

    sorcarCheckCmd = f"time {boogieBinPath} -trace -contractInfer -useProverEvaluate -mv:z3model -proverOpt:PROVER_PATH={z3Path} -mlHoudini:{sorcarBinPath} -learnerOptions:\"-a sorcar\" {sorcarOutPath}"

    runTool(sorcarCheckCmd, "p4Inv")

    # clean mid files
    cleanCmd = f"rm -f log.txt; cd {testLogDir}; rm -rf *.attributes *.data *.horn *.intervals *.status *.json *.R *.statistics *.bound log.txt"
    if clean:
        print(cleanCmd)
        outProcess = subprocess.run(cleanCmd, timeout=timeoutSecs, shell=True, stdout=subprocess.PIPE, stderr=subprocess.STDOUT)
        print("Clean Done.")

if __name__ == "__main__":
    parser = argparse.ArgumentParser(description="P4Inv")
    parser.add_argument("--p4", type=str, help="The p4 file to be checked.", required=True)
    parser.add_argument("--init", type=str, help="The register initialization file. Deafult is dummy.p4inv (no initialization).",
    default="dummy.p4inv")
    args = parser.parse_args()
    testP4Inv(args.p4, args.init)

    # Or, u can run p4inv locally
    # testP4Inv("dataset/P4Inv_Cases/P4NIS/P4NIS.p4", "dataset/P4Inv_Cases/P4NIS/init.p4inv")
    # testP4Inv("dataset/P4Inv_Cases/P4xos/acceptor.p4", "dataset/P4Inv_Cases/P4xos/acceptor.p4inv")
    # testP4Inv("dataset/P4Inv_Cases/P4xos/learner.p4", "dataset/P4Inv_Cases/P4xos/learner.p4inv")
    # testP4Inv("dataset/P4Inv_Cases/FRR/main.p4", "dataset/P4Inv_Cases/FRR/init.p4inv")
    # testP4Inv("dataset/P4Inv_Cases/Blink/main.p4", "dataset/P4Inv_Cases/Blink/init.p4inv")