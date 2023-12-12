#ifndef P4C_BF4_H
#define P4C_BF4_H

#include "ir/ir.h"
#include "backends/bmv2/common/controlFlowGraph.h"
#include "frontends/p4/methodInstance.h"

using namespace P4;

/* helper */ 
IR::BlockStatement *call_bug();
inline const P4::MethodInstance *
is_bim(const IR::MethodCallExpression *methodCallExpression,
       P4::ReferenceMap *refMap, P4::TypeMap *typeMap, cstring name) {
  auto mi = P4::MethodInstance::resolve(methodCallExpression, refMap, typeMap);
  if (auto ef = mi->to<P4::BuiltInMethod>()) {
    if (ef->name.name == name) {
      return mi;
    }
  }
  return nullptr;
}
inline const P4::MethodInstance *
is_set_valid(const IR::MethodCallExpression *methodCallExpression,
             P4::ReferenceMap *refMap, P4::TypeMap *typeMap) {
  return is_bim(methodCallExpression, refMap, typeMap, "setValid");
}
inline const P4::MethodInstance *
is_set_invalid(const IR::MethodCallExpression *methodCallExpression,
               P4::ReferenceMap *refMap, P4::TypeMap *typeMap) {
  return is_bim(methodCallExpression, refMap, typeMap, "setInvalid");
}
inline const P4::MethodInstance *
is_is_valid(const IR::MethodCallExpression *methodCallExpression,
            P4::ReferenceMap *refMap, P4::TypeMap *typeMap) {
  return is_bim(methodCallExpression, refMap, typeMap, "isValid");
}
inline const P4::MethodInstance *
is_set_valid(const IR::MethodCallStatement *mcs, P4::ReferenceMap *refMap,
             P4::TypeMap *typeMap) {
  return is_set_valid(mcs->methodCall, refMap, typeMap);
}
inline const P4::MethodInstance *
is_set_invalid(const IR::MethodCallStatement *mcs, P4::ReferenceMap *refMap,
               P4::TypeMap *typeMap) {
  return is_set_invalid(mcs->methodCall, refMap, typeMap);
}
inline const P4::MethodInstance *
is_bug(const IR::MethodCallExpression *methodCallExpression,
       P4::ReferenceMap *refMap, P4::TypeMap *typeMap);

const P4::MethodInstance *
is_bug(const IR::MethodCallExpression *methodCallExpression,
       P4::ReferenceMap *refMap, P4::TypeMap *typeMap) {
  auto mi = P4::MethodInstance::resolve(methodCallExpression, refMap, typeMap);
  if (auto ef = mi->to<P4::ExternFunction>()) {
    if (ef->method->name == "bug") {
      return mi;
    }
  }
  return nullptr;
}
inline const P4::MethodInstance *is_bug(const IR::MethodCallStatement *mcs,
                                        P4::ReferenceMap *refMap,
                                        P4::TypeMap *typeMap) {
  return is_bug(mcs->methodCall, refMap, typeMap);
}


/* solver */
class ValidityCheck : public Inspector {
  P4::ReferenceMap *refMap;
  P4::TypeMap *typeMap;
  std::map<const IR::Expression *, const IR::Expression *> valids;
  const IR::Expression *get(const IR::Expression *e);
  void set(const IR::Expression *e, const IR::Expression *v);
  template <typename Transformer>
  const IR::Expression *combine(const IR::Expression *lv,
                                const IR::Expression *rv,
                                Transformer transformer) {
    auto l = get(lv);
    auto r = get(rv);
    if (auto bl = l->to<IR::BoolLiteral>()) {
      if (bl->value) {
        if (auto br = r->to<IR::BoolLiteral>()) {
          if (br->value)
            return new IR::BoolLiteral(true);
          else
            return transformer(lv);
        }
        return new IR::LOr(r, transformer(lv));
      } else {
        if (auto br = r->to<IR::BoolLiteral>()) {
          if (!br->value)
            return new IR::BoolLiteral(false);
          else
            return transformer(rv);
        }
        return new IR::LAnd(r, transformer(rv));
      }
    } else if (auto br = r->to<IR::BoolLiteral>()) {
      if (br->value) {
        return new IR::LOr(l, transformer(rv));
      } else {
        return new IR::LAnd(l, transformer(lv));
      }
    } else {
      return new IR::LOr(
          new IR::LOr(new IR::LAnd(l, r), new IR::LAnd(l, transformer(lv))),
          new IR::LAnd(r, transformer(rv)));
    }
  }

  void postorder(const IR::LAnd *a) override;
  void postorder(const IR::LOr *a) override {
    set(a,
        combine(a->left, a->right, [](const IR::Expression *e) { return e; }));
  }
  void postorder(const IR::PathExpression *e) {
    set(e, new IR::BoolLiteral(true));
  }
  void postorder(const IR::Operation_Unary *u) override;

public:
  const IR::Expression *smart_and(const IR::Expression *e0,
                                  const IR::Expression *e1) {
    if (auto b0 = e0->to<IR::BoolLiteral>()) {
      if (b0->value) {
        return e1;
      } else {
        return new IR::BoolLiteral(false);
      }
    } else if (auto b1 = e1->to<IR::BoolLiteral>()) {
      if (b1->value) {
        return e0;
      } else {
        return new IR::BoolLiteral(false);
      }
    } else {
      return new IR::LAnd(e0, e1);
    }
  }

private:
  //  void postorder(const IR::StructInitializerExpression *init) override {
  //    init->
  //  }

  void postorder(const IR::Operation_Binary *t) override {
    // std::cout << "in vc postorder Operation_Binary\n";
    set(t, smart_and(get(t->left), get(t->right)));
    // std::cout << "after vc postorder Operation_Binary\n";
  }
  void postorder(const IR::Operation_Ternary *t) override {
    auto e0 = get(t->e0);
    auto e1 = get(t->e1);
    auto e2 = get(t->e2);
    // TODO: sema rules for ternaries
    set(t, smart_and(smart_and(e0, e1), e2));
  }
  void postorder(const IR::Literal *l) { set(l, new IR::BoolLiteral(true)); }
  void postorder(const IR::Member *m) {
    // std::cout << "in vc Member, arg m: " + m->toString() + "\n";
    auto mt = typeMap->getType(m);
    // std::cout << "after getType1\n";
    // std::cout << "result of mt:" + mt->toString() + "\n";
    if (mt->is<IR::Type_Method>()) {
      return;
    }
    // std::cout << "after if mt\n";
    auto e = m->expr;
    // std::cout << "after m->expr\n";
    auto et = typeMap->getType(e);
    // std::cout << "after getType2\n";
    if (et->is<IR::Type_Header>()) {
      set(m, new IR::MethodCallExpression(
                 new IR::Member(e, IR::Type_Header::isValid)));
    } else {
      set(m, new IR::BoolLiteral(true));
    }
    // std::cout << "after vc Member\n";
  }
  void postorder(const IR::MethodCallExpression *mce) {
    // std::cout << "in vc postorder mce\n";
    if (is_is_valid(mce, refMap, typeMap)) {
      set(mce, new IR::BoolLiteral(true));
    } else {
      const IR::Expression *crt = nullptr;
      if (auto m = mce->method->to<IR::Member>()) {
        crt = get(m);
      }
      if (mce->arguments) {
        for (auto arg : *mce->arguments) {
          if (!crt)
            crt = get(arg->expression);
          else
            crt = new IR::LAnd(crt, get(arg->expression));
        }
      }
      if (!crt)
        crt = new IR::BoolLiteral(true);
      set(mce, crt);
    }
    // std::cout << "after vc postorder mce\n";
  }
  void postorder(const IR::BOr *a) override {
    // std::cout << "in vc postorder BOr\n";
    auto tb = typeMap->getType(a->left)->to<IR::Type_Bits>();
    auto ctzero = new IR::Cmpl(new IR::Constant(tb, 0));
    set(a, combine(a->left, a->right, [ctzero](const IR::Expression *e) {
          return new IR::Equ(e, ctzero);
        }));
    // std::cout << "after vc postorder BOr\n";
  }
  void postorder(const IR::BAnd *a) override {
    // std::cout << "in vc postorder BAnd\n";
    auto tb = typeMap->getType(a->left)->to<IR::Type_Bits>();
    auto ctzero = new IR::Constant(tb, 0);
    set(a, combine(a->left, a->right, [ctzero](const IR::Expression *e) {
          return new IR::Equ(e, ctzero);
        }));
    // std::cout << "after vc postorder BAnd\n";
  }
  void postorder(const IR::ListExpression *lexp) override {
    const IR::Expression *v = nullptr;
    for (auto e : lexp->components) {
      auto crt = get(e);
      if (!v)
        v = crt;
      else
        v = smart_and(crt, v);
    }
    set(lexp, v);
  }

public:
  ValidityCheck(P4::ReferenceMap *refMap, P4::TypeMap *typeMap)
      : refMap(refMap), typeMap(typeMap) {}
  const IR::Expression *is_valid(const IR::Expression *e) {
    // std::cout << "in vc is_valid\n";
    e->apply(*this);
    // std::cout << "After apply\n";
    return get(e);
  }
};

class ConvertHeaderAssignments : public Transform {
  ReferenceMap *refMap;
  TypeMap *typeMap;

public:
  ConvertHeaderAssignments(ReferenceMap *refMap, TypeMap *typeMap)
      : refMap(refMap), typeMap(typeMap) {}

  const IR::Node *preorder(IR::AssignmentStatement *asg) override;
};

class GenerateAssertions : public PassManager {
  ReferenceMap *refMap;
  TypeMap *typeMap;
  std::vector<const IR::BlockStatement *> stats;
public:
  explicit GenerateAssertions(ReferenceMap *refMap, TypeMap *typeMap,
                     bool instrument_ifs = true);
};

class DoInstrumentIfStatements : public Transform {
  ReferenceMap *refMap;
  TypeMap *typeMap;
  std::vector<const IR::BlockStatement *> *statements;

public:
  DoInstrumentIfStatements(
      ReferenceMap *refMap, TypeMap *typeMap,
      std::vector<const IR::BlockStatement *> *statements = nullptr)
      : refMap(refMap), typeMap(typeMap), statements(statements) {}

  const IR::Node *postorder(IR::IfStatement *ifs) override {
    // std::cout << "Before vc build\n";
    ValidityCheck validityCheck(refMap, typeMap);
    // std::cout << "After vc build\n";
    auto vcheck =
        validityCheck.is_valid(getOriginal<IR::IfStatement>()->condition);
    // std::cout << "After vcheck\n";
    if (auto bl = vcheck->to<IR::BoolLiteral>()) {
      if (bl->value) {
        return ifs;
      } else {
        return call_bug();
      }
    } else {
      ifs = new IR::IfStatement(vcheck, ifs, call_bug());
    }
    return ifs;
  }
};

class DoGenerateAssertions : public Transform {
  ReferenceMap *refMap;
  TypeMap *typeMap;
  std::vector<const IR::BlockStatement *> *stats;

public:
  DoGenerateAssertions(ReferenceMap *refMap, TypeMap *typeMap,
                       std::vector<const IR::BlockStatement *> *stats)
      : refMap(refMap), typeMap(typeMap), stats(stats) {}

  const IR::Node *postorder(IR::AssignmentStatement *asg) override;
  const IR::Node *postorder(IR::MethodCallStatement *mcs) override;
};

struct ConditionWrapper {
  std::map<const IR::Expression *, const IR::Expression *> condition;
  const IR::Expression *&operator[](const IR::Expression *what) {
    auto p = condition.emplace(what, new IR::BoolLiteral(true));
    return p.first->second;
  }
};

class WhatCanGoWrong : public Inspector {
  ReferenceMap *refMap;
  TypeMap *typeMap;
  ConditionWrapper condition;

public:
  const IR::Expression *get(const IR::Expression *iex);
  WhatCanGoWrong(ReferenceMap *refMap, TypeMap *typeMap)
      : refMap(refMap), typeMap(typeMap) {}

  const IR::Expression *orer(const IR::Expression *l, const IR::Expression *r);

  const IR::Expression *neger(const IR::Expression *l);
  const IR::Expression *ander(const IR::Expression *l, const IR::Expression *r);

  void postorder(const IR::LAnd *op) override;
  void postorder(const IR::LOr *op) override;
  void postorder(const IR::LNot *op) override;
  void postorder(const IR::MethodCallExpression *mce) override;
  void postorder(const IR::Add *op) override;
  void postorder(const IR::Sub *op) override;
  void postorder(const IR::Equ *op) override;
  void postorder(const IR::Leq *op) override;
  void postorder(const IR::Geq *op) override;
  void postorder(const IR::BAnd *op) override;
  void postorder(const IR::Cmpl *op) override;
  void postorder(const IR::ArrayIndex *op) override;
  void postorder(const IR::BXor *op) override;
  void postorder(const IR::Slice *slice) override;
  void postorder(const IR::Cast *cast) override;
  void postorder(const IR::BOr *op) override;
  void postorder(const IR::Neq *op) override;
  void postorder(const IR::PathExpression *pathExpression) override;
  void postorder(const IR::Shl *op) override;
  void postorder(const IR::Member *mem) override;
};

#endif