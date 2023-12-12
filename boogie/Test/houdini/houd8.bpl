// RUN: %parallel-boogie -contractInfer -printAssignment "%s" > "%t"
// RUN: %diff "%s.expect" "%t"

// Houdini is very interactive and doesn't work with batch mode
// UNSUPPORTED: batch_mode
const {:existential true} b1:bool;
const {:existential true} b2:bool;
const {:existential true} b3:bool;

var myVar: int;

procedure foo(i:int) 
modifies myVar;
ensures b1 ==> myVar>0;
ensures b2 ==> myVar==0;
ensures b3 ==> myVar<0;
{
  myVar:=5; 
}

// expected outcome: Correct
// expected assigment: b1->True,b2->False,b3->False













