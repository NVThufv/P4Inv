// RUN: %parallel-boogie /monomorphize "%s" > "%t"
// RUN: %diff "%s.expect" "%t"

datatype Pair { Pair(a: int, b: int) }

procedure P0(p: Pair) returns (q: Pair)
  requires p->a == 0;
  ensures  q->a == 1;
{
  q := p;
  q->a := 1;
  assert q->b == p->b;
  q->b := 1;
  assert q == Pair(1, 1);
}

procedure P1(p: [int]Pair, x: int) returns (q: [int]Pair)
  requires p[x]->a == 0;
  ensures  q[x]->a == 1;
{
  q[x] := p[x];
  q[x]->a := 1;
  assert q[x]->b == p[x]->b;
  q[x]->b := 1;
  assert q[x] == Pair(1, 1);
}

datatype PairOfMaps { PairOfMaps(amap: [int]Pair, bmap: [int]Pair) }

procedure P2(p: PairOfMaps, x: int) returns (q: PairOfMaps)
  requires p->amap[x]->a == 0;
  ensures  q->bmap[x]->a == 1;
{
  var t: [int]Pair;
  q := p;
  call t := P1(p->amap, x);
  q->bmap := t;
}

datatype GenericPair<U> { GenericPair(a: U, b: U) }

procedure P3<T>(p: GenericPair T) returns (q: GenericPair T)
  requires p->a == p->b;
  ensures  q->a == q->b;
{
  assert p is GenericPair;
  q->a := p->b;
  q->b := p->a;
  assert q is GenericPair;
}

datatype Split<T> { Left(i: T), Right(i: T) }

procedure P4(a: int, b: int)
{
  var left, right: Split int;
  left->i := a;
  right->i := b;
  assert left->i == a;
  assert left is Left ==> left == Left(a);
  assert right->i == b;
  assert right is Right ==> right == Right(b);
}

procedure P5(p: Pair) returns (a: int, b: int)
ensures a == p->a && b == p->b;
{
  var p': Pair;
  Pair(a, b) := p;
  p' := Pair(a, b);
  assert p == p';
}

procedure P6(s: Split int) returns (i: int)
requires s is Left;
ensures i == s->i;
{
  var s': Split int;
  Left(i) := s;
  s' := Left(i);
  assert s == s';
}

procedure P7(p: Pair) returns (q: Pair)
  requires p->a == 0;
  ensures  q->a == 1;
{
  q := p->(a := 1);
  assert q->b == p->b;
  q := q->(b := 1);
  assert q == Pair(1, 1);
}

procedure P8<T>(p: GenericPair T) returns (q: GenericPair T)
  requires p->a == p->b;
  ensures  q->a == q->b;
{
  assert p is GenericPair;
  q := q->(a := p->b);
  q := q->(b := p->a);
  assert q is GenericPair;
}
