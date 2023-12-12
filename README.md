# P4Inv

This is the implementation of our approach in "P4Inv: Inferring Packet Invariants for Verification of Stateful P4 Programs" INFOCOM 24.

## Prerequisite

[Z3 4.12.2](https://github.com/Z3Prover/z3/releases) and other prerequisites in REDEME files below.

## Building

We have tested P4Inv on Ubuntu 20.04 platform. Other platforms are untested; you can try to use them, but YMMV.

Before running p4inv, three components needs to be built:

1. [Boogie](boogie/README.md)
2. [Sorcar](sorcar/README.md)
3. [Translator](Translator/README.md)


## Verifying A P4 Program

Change the `sorcarBinPath` and `z3Path` to the absolute paths of sorcar and z3 executables in your system. E.g.

```
    sorcarBinPath = "/home/user/Desktop/p4inv/sorcar/src/sorcar"
    z3Path = "/home/user/Desktop/z3/build/z3"
```

and run 

```
    python P4Inv.py --p4 dataset/P4Inv_Cases/P4NIS/P4NIS.p4 --init dataset/P4Inv_Cases/P4NIS/init.p4inv
```

to check `P4NIS.p4` with the intialization in `init.p4inv`. For more help, run

```
    python P4Inv.py --help
```