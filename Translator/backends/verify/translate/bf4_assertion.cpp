#include "bf4_assertion.h"
#include "frontends/common/constantFolding.h"
#include "frontends/p4/evaluator/evaluator.h"

GenerateAssertions::GenerateAssertions(ReferenceMap *refMap, TypeMap *typeMap,
                                       bool instrument_ifs)
    : refMap(refMap), typeMap(typeMap) {
  setName("MidEnd_genassert");
  bool isv1 = false;
  refMap->setIsV1(isv1);
  auto eval = new EvaluatorPass(refMap, typeMap);
//   passes.push_back(new CreateAnalysisHelpers);
  passes.push_back(eval);
  if (instrument_ifs) {
    passes.push_back(new DoInstrumentIfStatements(refMap, typeMap));
    passes.push_back(eval);
  }
  passes.push_back(eval);
  passes.push_back(new ConvertHeaderAssignments(refMap, typeMap));
  passes.push_back(eval);
  passes.push_back(new DoGenerateAssertions(refMap, typeMap, &stats));
  passes.push_back(eval);
}

IR::BlockStatement *call_bug() {
  auto falseExpr = new IR::BoolLiteral(new IR::Type_Boolean(), false);
  auto assert = new IR::Annotations();
  assert->addAnnotation("assert", falseExpr);
  auto bug = new IR::BlockStatement(assert);
  return bug;
  // return new IR::MethodCallStatement(
  //     new IR::MethodCallExpression(new IR::PathExpression("bug")));
}

const IR::Node *ConvertHeaderAssignments::preorder(IR::AssignmentStatement *asg) {
  // // std::cout << "Before Ha, arg: " + asg->left->toString() + " = " + asg->right->toString()  <<  "\n";
  auto tl = typeMap->getType(asg->left);
  // // std::cout << "After tl: " + (tl ? tl->toString() : "nullptr") +  "\n";
  if (tl && tl->is<IR::Type_Header>()) {
    if (asg->right->is<IR::MethodCallExpression>()) {
      return asg;
    }
    // if (asg->right->is<IR::StructInitializerExpression>()) {
    //   auto rsi = asg->right->to<IR::StructInitializerExpression>();
    //   auto bs = new IR::BlockStatement();
    //   for (const auto &ne : rsi->components) {
    //     auto a = new IR::AssignmentStatement(
    //       new IR::Member(asg->left, ne->name), ne->expression);
    //     bs->components.push_back(a);
    //   }
    //   return bs;
    // }
    auto bs = new IR::BlockStatement();
    auto tl_ = tl->to<IR::Type_Header>();

    bs->components.push_back(
      new IR::MethodCallStatement(new IR::MethodCallExpression(
        new IR::Member(asg->left, IR::ID(IR::Type_Header::setValid)))));
    for (auto fld : tl_->fields) {
      auto meml = new IR::Member(asg->left, fld->name);
      auto memr = new IR::Member(asg->right, fld->name);
      bs->components.push_back(new IR::AssignmentStatement(meml, memr));
    }
    auto iftargetInvalid = new IR::IfStatement(
      new IR::MethodCallExpression(
        new IR::Member(asg->left, IR::ID(IR::Type_Header::isValid))),
      call_bug(), new IR::BlockStatement());
      // new IR::MethodCallStatement(
      //   new IR::MethodCallExpression(new IR::PathExpression(
      //     "dontcare"))));
    return new IR::IfStatement(
      new IR::MethodCallExpression(
        new IR::Member(asg->right, IR::ID(IR::Type_Header::isValid))),
      bs, iftargetInvalid);
  }
  return asg;
}

const IR::Node *DoGenerateAssertions::postorder(IR::AssignmentStatement *asg) {
  // std::cout << "befor asg: " + asg->left->toString() + " = " + asg->right->toString() + "\n";
  WhatCanGoWrong whatCanGoWrong(refMap, typeMap);
  asg->right->apply(whatCanGoWrong);
  // std::cout << "After whatcam go wromh right\n";
  auto cd = whatCanGoWrong.get(asg->right);
  asg->left->apply(whatCanGoWrong);
  // std::cout << "After whatcam go wromh left\n";
  auto cdl = whatCanGoWrong.get(asg->left);
  // std::cout << "After whatcam go wromh cdl\n";
  auto fullCondition = new IR::LAnd(asg->getSourceInfo(), cd, cdl);
  auto ctProp = DoConstantFolding(refMap, typeMap);
  auto expr = fullCondition->apply(ctProp);
  // std::cout << "After DoConstantFolding\n";
  auto mcs = call_bug();
  if (auto bl = expr->to<IR::BoolLiteral>()) {
    if (!bl->value) {
      return mcs;
    } else {
      return asg;
    }
  }
  auto bs = new IR::BlockStatement(
      {new IR::IfStatement(asg->getSourceInfo(), expr, asg, mcs)});
  stats->push_back(bs);
  return bs;
}

const IR::Node *DoGenerateAssertions::postorder(IR::MethodCallStatement *mcs) {
  // std::cout << "before mcs: " + mcs->toString() + "\n";
  auto orig = getOriginal<IR::MethodCallStatement>();
  ValidityCheck validityCheck(refMap, typeMap);
  // std::cout << "After validityCheck\n";
  const IR::Expression *valid = nullptr;
  if (auto mi = P4::MethodInstance::resolve(orig, refMap, typeMap)) {
    // std::cout << cstring("mi: ") + (mi ? "not null" : "null") + "\n";
    if (mi->is<P4::ExternFunction>() || mi->is<P4::ExternMethod>()) {
      for (auto arg : *mcs->methodCall->arguments) {
        // std::cout << "Arg: " + arg->toString() + "\n";
        auto parm = mi->substitution.findParameter(arg);
        // std::cout << cstring("parm: ") + (parm ? "not null" : "null") + "\n";
        if (parm->direction == IR::Direction::In ||
            parm->direction == IR::Direction::InOut) {
          // std::cout << "Before is_valid: arg->expr: " + arg->expression->toString() << "\n";
          auto v = validityCheck.is_valid(arg->expression);
          // std::cout << "After is_valid\n";
          if (!valid)
            valid = v;
          else if(v)
          {
            // std::cout << "Before smart_and: v: " + v->toString() + " valid: " + valid->toString()  + "\n";
            valid = validityCheck.smart_and(v, valid);
            // std::cout << "After smart_and\n";
          }
        }
      }
      // std::cout << "After arg\n";
      if (valid) {
        if (auto v = valid->to<IR::BoolLiteral>()) {
          if (!v->value)
            return call_bug();
          else
            return mcs;
        }
        return new IR::IfStatement(valid, mcs, call_bug());
      }
    }
  }
  return mcs;
}

const IR::Expression *ValidityCheck::get(const IR::Expression *e) {
  auto I = valids.find(e);
  if (I != valids.end())
    return I->second;
  return nullptr;
}

void ValidityCheck::set(const IR::Expression *e, const IR::Expression *v) {
  valids.emplace(e, v);
}

void ValidityCheck::postorder(const IR::LAnd *a) {
  set(a, combine(a->left, a->right,
                 [](const IR::Expression *e) { return new IR::LNot(e); }));
}

void ValidityCheck::postorder(const IR::Operation_Unary *u) {
  auto e = get(u->expr);
  set(u, e);
}

const IR::Expression *WhatCanGoWrong::orer(const IR::Expression *l,
                                           const IR::Expression *r) {
  auto lv = l;
  auto rv = r;
  if (lv->is<IR::BoolLiteral>()) {
    auto lval = lv->to<IR::BoolLiteral>();
    if (!lval->value) {
      return rv;
    } else {
      return new IR::BoolLiteral(true);
    }
  } else {
    if (rv->is<IR::BoolLiteral>()) {
      auto rval = rv->to<IR::BoolLiteral>();
      if (!rval->value) {
        return lv;
      } else {
        return new IR::BoolLiteral(true);
      }
    }
  }
  return new IR::LOr(l, r);
}

const IR::Expression *WhatCanGoWrong::neger(const IR::Expression *l) {
  if (l->is<IR::BoolLiteral>()) {
    return new IR::BoolLiteral(!l->to<IR::BoolLiteral>()->value);
  }
  return new IR::Neg(l);
}

const IR::Expression *WhatCanGoWrong::ander(const IR::Expression *l,
                                            const IR::Expression *r) {
  auto lv = l;
  auto rv = r;
  if (lv->is<IR::BoolLiteral>()) {
    auto lval = lv->to<IR::BoolLiteral>();
    if (lval->value) {
      return rv;
    } else {
      return new IR::BoolLiteral(false);
    }
  } else {
    if (rv->is<IR::BoolLiteral>()) {
      auto rval = rv->to<IR::BoolLiteral>();
      if (rval->value) {
        return lv;
      } else {
        return new IR::BoolLiteral(false);
      }
    }
  }
  return new IR::LAnd(l, r);
}

void WhatCanGoWrong::postorder(const IR::LAnd *) {}

void WhatCanGoWrong::postorder(const IR::LOr *) {}

void WhatCanGoWrong::postorder(const IR::LNot *op) {
  condition[op] = get(op->expr);
}

void WhatCanGoWrong::postorder(const IR::MethodCallExpression *mce) {
  auto mi = MethodInstance::resolve(mce, refMap, typeMap);
  if (mi->is<BuiltInMethod>()) {
    auto bim = mi->to<BuiltInMethod>();
    auto applyKind = typeMap->getType(bim->appliedTo);
    if (applyKind->is<IR::Type_Header>()) {
      if (bim->name.name == IR::Type_Header::isValid) {
        condition[mce] = new IR::BoolLiteral(true);
      }
    }
  } else if (mi->is<ExternMethod>()) {
    auto em = mi->to<ExternMethod>();
    if (em->method->name.name == "lookahead") {
      condition[mce] = new IR::BoolLiteral(true);
    } else {
      condition[mce] = get(mce->method);
    }
  } else if (mi->is<ExternFunction>()) {
    condition[mce] = new IR::BoolLiteral(true);
  }
}

void WhatCanGoWrong::postorder(const IR::BAnd *op) {
  auto rtype = typeMap->getType(op->right);
  auto constant = new IR::Constant(rtype, 0);
  auto neg = orer(ander(get(op->right), new IR::Equ(op->right, constant)),
                  ander(get(op->left), new IR::Equ(op->left, constant)));
  condition[op] = orer(ander(get(op->left), get(op->right)), neg);
}

void WhatCanGoWrong::postorder(const IR::Member *mem) {
  auto applied_to = typeMap->getType(mem->expr);
  if (applied_to->is<IR::Type_Header>()) {
    if (mem->member.name != IR::Type_Header::isValid) {
      auto method = new IR::Member(mem->expr, IR::ID(IR::Type_Header::isValid));
      auto result =
          new IR::MethodCallExpression(IR::Type::Boolean::get(), method);
      condition[mem] = result;
      return;
    }
  }
}

void WhatCanGoWrong::postorder(const IR::Add *op) {
  condition[op] = ander(get(op->left), get(op->right));
}

void WhatCanGoWrong::postorder(const IR::Sub *op) {
  condition[op] = ander(get(op->left), get(op->right));
}

void WhatCanGoWrong::postorder(const IR::Equ *op) {
  condition[op] = ander(get(op->left), get(op->right));
}

void WhatCanGoWrong::postorder(const IR::Leq *op) {
  condition[op] = ander(get(op->left), get(op->right));
}

void WhatCanGoWrong::postorder(const IR::Geq *op) {
  condition[op] = ander(get(op->left), get(op->right));
}

void WhatCanGoWrong::postorder(const IR::Cmpl *op) {
  condition[op] = get(op->expr);
}

void WhatCanGoWrong::postorder(const IR::ArrayIndex *op) {
  condition[op] = ander(get(op->left), get(op->right));
}

void WhatCanGoWrong::postorder(const IR::BXor *op) {
  condition[op] = ander(get(op->left), get(op->right));
}

void WhatCanGoWrong::postorder(const IR::Slice *slice) {
  condition[slice] = get(slice->e0);
}

void WhatCanGoWrong::postorder(const IR::BOr *op) {
  condition[op] = ander(get(op->left), get(op->right));
}

void WhatCanGoWrong::postorder(const IR::Cast *cast) {
  condition[cast] = get(cast->expr);
}

void WhatCanGoWrong::postorder(const IR::Neq *op) {
  condition[op] = ander(get(op->left), get(op->right));
}

void WhatCanGoWrong::postorder(const IR::PathExpression *pathExpression) {
  condition[pathExpression] = new IR::BoolLiteral(true);
}

void WhatCanGoWrong::postorder(const IR::Shl *op) {
  condition[op] = ander(get(op->left), get(op->right));
}

const IR::Expression *WhatCanGoWrong::get(const IR::Expression *iex) {
  //            if (!condition.condition.count(iex))
  //                return nullptr;
  return condition[iex]->clone();
}
