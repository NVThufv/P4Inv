// RUN: %parallel-boogie "%s" > "%t"
// RUN: %diff "%s.expect" "%t"
// UNSUPPORTED: batch_mode

var {:layer 0,2} x:int;

procedure {:yields}{:layer 1}{:refines "INCR"} p ()
{
  call incr(); // Refined action INCR occurred
  call Yield();
  call incr(); // Error: State changed again
}

procedure {:yields}{:layer 1}{:refines "INCR"} q ()
{
  call decr(); // Error: State changed, but not according to INCR
  call Yield();
  call incr(); // Error: State changed again
}

procedure {:yields}{:layer 1}{:refines "INCR"} r ()
{
  call incr();
  call decr(); // SKIP
  call Yield();
  call incr(); // INCR
  call Yield();
  call incr();
  call incr();
  call decr();
  call decr(); // SKIP
}

procedure {:yields}{:layer 1}{:refines "INCR"} s ()
{
  // Error: Refined action INCR never occurs
}

procedure {:yields}{:layer 1}{:refines "INCR"} t ()
{
  call incr();
  call Yield();
  while (*)
  invariant {:yields} true;
  {
    call incr(); // Error: State change inside yielding loop
  }
}

procedure {:both} {:layer 1,2} INCR ()
modifies x;
{
  x := x + 1;
}

procedure {:both} {:layer 1,2} DECR ()
modifies x;
{
  x := x - 1;
}

procedure {:yields} {:layer 0} {:refines "INCR"} incr ();
procedure {:yields} {:layer 0} {:refines "DECR"} decr ();

yield invariant {:layer 1} Yield();
