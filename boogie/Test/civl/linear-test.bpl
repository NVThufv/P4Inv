// RUN: %parallel-boogie "%s" > "%t"
// RUN: %diff "%s.expect" "%t"

type {:linear "A"} A = int;

procedure {:yields}{:layer 1} foo({:linear "A"} x: int, {:linear "A"} y: int)
{
    assert {:layer 1} x != y;
}
