// RUN: %parallel-boogie -infer:j -instrumentInfer:e -printInstrumented -noVerify "%s" > "%t"
// RUN: %diff "%s.expect" "%t"
// Simple test file for checking the inference of linear constraints.

var x: int;
var y: int;

procedure p()
{
  start:
    assume x*x == y;  // not a linear condition
    return;
}
