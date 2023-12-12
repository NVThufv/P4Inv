# P4Inv

Despite that we use Z3 4.12.2 instead of z3 4.8.4, we follow the original imlementation of [sorcar](https://github.com/horn-ice/sorcar). Sorcar, together with boogie, form the invariant generator component of P4Inv.

Note: Before building, please modify the `Z3_INCLUDE` and `Z3_BINARY` variables in src/Makefile with the corresponding loactions in your system.

After installing the prerequisites, move to the `src` directory, run

```
  make build
```
to build sorcar.

## Sorcar
=====
Sorcar is a property-driven learning algorithm for conjunctive invariants. The code in this repository is designed to be used in conjunction with the [Horn-ICE Verification Framework](https://github.com/horn-ice/hice-dt).

You can try Sorcar online at <https://horn-ice.mpi-sws.org>.

### Requirements
Please make sure that your system satisfies the following requirements:

  * Sorcar is a plug-in learning algorithm for the [Horn-ICE Verification Framework](https://github.com/horn-ice/hice-dt). Download the framework and follow the instructions to set it up.
  * Sorcar uses [Microsoft's Z3 SMT solver](https://github.com/Z3Prover/z3). The sources of Z3 4.8.4 are included in this repository, but you need to make sure that your system satisfies their requirements. More information can be found on the Z3 website on [GitHub](https://github.com/Z3Prover/z3/tree/z3-4.8.4).
  * To build Sorcar, you need the GNU build utilities and GCC version 6.0+ installed.

We have tested Sorcar on Linux (Ubuntu 19.04). It is likely to also compile and run on Microsoft Windows (e.g., via [MinGW](http://www.mingw.org/) and [MSYS](http://www.mingw.org/wiki/MSYS)), but we cannot offer assistance for this setup.

### Building Sorcar
To compile Sorcar, simply execute the following command in the folder `./src` of the repository:

```bash
make
```

#### Building Sorcar With a Different Version of Z3
Sorcar might be compiled and linked against different versions of Z3, which you might have installed on your system (note, however, that we did not test this). To facilitate this, the build script of Sorcar uses two environment variables:

  * `Z3_INCLUDE`, which needs to point to the location of the Z3 header files (specifically to `z3.h` and `z3++.h` as well as all other referenced headers).
  * `Z3_BINARY`, which needs to point to the location of the Z3 library files (the shared library `libz3.so` and/or the static library `libz3.a`).

Both environment variables are already set properly for the sources of Z3 located in this repository.

The standard behavior of the build script is to build both the statically and the dynamically linked version of Sorcar. However, the build script also allows building only one version (as Z3 is usually only shipped with the shared library `libz3.so`):

  * To build `sorcar`, run the following command in the the base folder `./`:
    ```bash
    Z3_INCLUDE=... Z3_BINARY=... make -C src sorcar
    ```
    In this case, `Z3_BINARY` needs to point to a location containing the file `libz3.so`.
  * To build `sorcar-static`, run the following command in the the base folder `./`:
    ```bash
    Z3_INCLUDE=... Z3_BINARY=... make -C src sorcar-static
    ```
    In this case, `Z3_BINARY` needs to point to a location containing the file `libz3.a`.
  
Note that all paths have the be absolute or relative to the `./src` folder (not the base folder). If you have Z3 installed on your system, you may leave the environment variables undefined.

### Running Sorcar
After a successful compilation, the binaries of Sorcar are located in the `./src` folder.

`sorcar-static` can immediately be executed as it is statically linked against Z3.

To run `sorcar`, the shared library `libz3.so` must either be installed on your system, or the environment variable `LD_LIBRARY_PATH` needs to point to its location. A typical execution would look as follows:
```bash
LD_LIBRARY_PATH=$LD_LIBRARY_PATH:... ./src/sorcar
```
Run the Sorcar binaries with the `-h` option (or without arguments) to display a list of available options.

#### Running Sorcar in Conjunction With The Horn-ICE Verification Framework
To run Sorcar in conjunction with the Horn-ICE Verification Framework, follow the framework's instructions as described in the section ["Running a Single Benchmark"](https://github.com/horn-ice/hice-dt#running-a-single-benchmark)/[The Boogie-based Verifier](https://github.com/horn-ice/hice-dt#the-boogie-based-verifier). If necessary, set the the environment variable `LD_LIBRARY_PATH` appropriately.
