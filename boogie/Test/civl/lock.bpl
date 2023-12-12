// RUN: %parallel-boogie "%s" > "%t"
// RUN: %diff "%s.expect" "%t"
var {:layer 0,2} b: bool;

procedure {:yields} {:layer 2} main()
{
    while (*)
    {
        async call Customer();
    }
}

procedure {:yields} {:layer 2} Customer()
{
    while (*)
    invariant {:yields} true;
    {
        call Enter();
        call Yield();
        call Leave();
    }
}

procedure {:atomic} {:layer 2} AtomicEnter()
modifies b;
{ assume !b; b := true; }

procedure {:yields} {:layer 1} {:refines "AtomicEnter"} Enter()
{
    var status: bool;

    while (true)
    invariant {:yields} true;
    {
        call status := CAS(false, true);
        if (status) {
            return;
        }
    }
}

procedure {:atomic} {:layer 1,2} AtomicCAS(prev: bool, next: bool) returns (status: bool)
modifies b;
{
  if (b == prev) {
    b := next;
    status := true;
  } else {
    status := false;
  }
}

procedure {:yields} {:layer 0} {:refines "AtomicCAS"} CAS(prev: bool, next: bool) returns (status: bool);

procedure {:atomic} {:layer 1,2} AtomicLeave()
modifies b;
{ b := false; }

procedure {:yields} {:layer 0} {:refines "AtomicLeave"} Leave();

procedure {:yields} {:layer 2} Yield();
