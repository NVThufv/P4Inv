using System;
using Microsoft.Boogie;
using Microsoft.Boogie.VCExprAST;
using System.Diagnostics.Contracts;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Microsoft.BaseTypes;

namespace VC
{
  public class VCContext
  {
    public VCGenOptions Options { get; }

    [ContractInvariantMethod]
    void ObjectInvariant()
    {
      Contract.Invariant(Ctxt != null);
    }

    [Rep] public readonly ControlFlowIdMap<Absy> absyIds;
    [Rep] public readonly ProverContext Ctxt;
    public readonly VCExpr ControlFlowVariableExpr;
    public int AssertionCount; // counts the number of assertions for which Wlp has been computed
    public bool isPositiveContext;

    public VCContext(VCGenOptions options, ControlFlowIdMap<Absy> absyIds, ProverContext ctxt, bool isPositiveContext = true)
    {
      Contract.Requires(ctxt != null);
      Options = options;
      this.absyIds = absyIds;
      this.Ctxt = ctxt;
      this.isPositiveContext = isPositiveContext;
    }

    public VCContext(VCGenOptions options, ControlFlowIdMap<Absy> absyIds, ProverContext ctxt, VCExpr controlFlowVariableExpr,
      bool isPositiveContext = true)
    {
      Contract.Requires(ctxt != null);
      Options = options;
      this.absyIds = absyIds;
      this.Ctxt = ctxt;
      this.ControlFlowVariableExpr = controlFlowVariableExpr;
      this.isPositiveContext = isPositiveContext;
    }
  }

  class FunctionCollector : StandardVisitor
  {
      public List<Tuple<Function, ExistsExpr>> functionsUsed;
      ExistsExpr existentialExpr;

      public FunctionCollector()
      {
          functionsUsed = new List<Tuple<Function, ExistsExpr>>();
          existentialExpr = null;
      }

      public static List<Tuple<Function, ExistsExpr>> Collect(Expr expr)
      {
          var fv = new FunctionCollector();
          fv.VisitExpr(expr);
          return fv.functionsUsed;
      }

      public override QuantifierExpr VisitQuantifierExpr(QuantifierExpr node)
      {
          var oldE = existentialExpr;

          if (node is ExistsExpr)
              existentialExpr = (node as ExistsExpr);

          node = base.VisitQuantifierExpr(node);

          existentialExpr = oldE;
          return node;
      }

      public override Expr VisitNAryExpr(NAryExpr node)
      {
          if (node.Fun is FunctionCall)
          {
              var collector = new VariableCollector();
              collector.Visit(node);
              if(existentialExpr != null)
              {
                var dummy = new HashSet<Variable>(existentialExpr.Dummies);
                dummy.IntersectWith(collector.usedVars);
                if (existentialExpr != null && dummy.Count > 0)
                    functionsUsed.Add(Tuple.Create((node.Fun as FunctionCall).Func, existentialExpr));
                else
                    functionsUsed.Add(Tuple.Create<Function, ExistsExpr>((node.Fun as FunctionCall).Func, null));
              }
              
          }

          return base.VisitNAryExpr(node);
      }
  }

  #region A class to compute wlp of a passive command program

  public class Wlp
  {
    public static VCExpr Block(Block b, VCExpr N, VCContext ctxt)
      //modifies ctxt.*;
    {
      Contract.Requires(b != null);
      Contract.Requires(N != null);
      Contract.Requires(ctxt != null);
      Contract.Ensures(Contract.Result<VCExpr>() != null);

      VCExpressionGenerator gen = ctxt.Ctxt.ExprGen;
      Contract.Assert(gen != null);

      VCExpr res = N;

      for (int i = b.Cmds.Count; --i >= 0;)
      {
        res = Cmd(b, cce.NonNull(b.Cmds[i]), res, ctxt);
      }

      ctxt.absyIds.GetId(b);

      try
      {
        cce.BeginExpose(ctxt);
        return res;
      }
      finally
      {
        cce.EndExpose();
      }
    }

    public static VCExpr P_Block(Block b, HashSet<string> constantsAssumed, VCExpr N, VCContext ctxt)
      //modifies ctxt.*;
    {
      Contract.Requires(b != null);
      Contract.Requires(N != null);
      Contract.Requires(ctxt != null);
      Contract.Ensures(Contract.Result<VCExpr>() != null);

      VCExpressionGenerator gen = ctxt.Ctxt.ExprGen;
      Contract.Assert(gen != null);

      VCExpr res = N;

      for (int i = b.Cmds.Count; --i >= 0;)
      {
        res = P_Cmd(b, constantsAssumed, cce.NonNull(b.Cmds[i]), res, ctxt);
      }

      ctxt.absyIds.GetId(b);

      try
      {
        cce.BeginExpose(ctxt);
        return res;
      }
      finally
      {
        cce.EndExpose();
      }
    }

    /// <summary>
    /// Computes the wlp for an assert or assume command "cmd".
    /// </summary>
    internal static VCExpr Cmd(Block b, Cmd cmd, VCExpr N, VCContext ctxt)
    {
      Contract.Requires(cmd != null);
      Contract.Requires(N != null);
      Contract.Requires(ctxt != null);
      Contract.Ensures(Contract.Result<VCExpr>() != null);

      VCExpressionGenerator gen = ctxt.Ctxt.ExprGen;
      Contract.Assert(gen != null);
      if (cmd is AssertCmd)
      {
        AssertCmd ac = (AssertCmd) cmd;

        var isFullyVerified = false;
        if (ac.VerifiedUnder != null)
        {
          var litExpr = ac.VerifiedUnder as LiteralExpr;
          isFullyVerified = litExpr != null && litExpr.IsTrue;
        }

        if (!isFullyVerified)
        {
          ctxt.Ctxt.BoogieExprTranslator.isPositiveContext = !ctxt.Ctxt.BoogieExprTranslator.isPositiveContext;
        }

        VCExpr C = ctxt.Ctxt.BoogieExprTranslator.Translate(ac.Expr);

        VCExpr VU = null;
        if (!isFullyVerified)
        {
          if (ac.VerifiedUnder != null)
          {
            VU = ctxt.Ctxt.BoogieExprTranslator.Translate(ac.VerifiedUnder);

            if (ctxt.Options.RunDiagnosticsOnTimeout)
            {
              ctxt.Ctxt.TimeoutDiagnosticIDToAssertion[ctxt.Ctxt.TimeoutDiagnosticsCount] =
                new Tuple<AssertCmd, TransferCmd>(ac, b.TransferCmd);
              VU = gen.Or(VU,
                gen.Function(VCExpressionGenerator.TimeoutDiagnosticsOp,
                  gen.Integer(BigNum.FromInt(ctxt.Ctxt.TimeoutDiagnosticsCount++))));
            }
          }
          else if (ctxt.Options.RunDiagnosticsOnTimeout)
          {
            ctxt.Ctxt.TimeoutDiagnosticIDToAssertion[ctxt.Ctxt.TimeoutDiagnosticsCount] =
              new Tuple<AssertCmd, TransferCmd>(ac, b.TransferCmd);
            VU = gen.Function(VCExpressionGenerator.TimeoutDiagnosticsOp,
              gen.Integer(BigNum.FromInt(ctxt.Ctxt.TimeoutDiagnosticsCount++)));
          }

          ctxt.Ctxt.BoogieExprTranslator.isPositiveContext = !ctxt.Ctxt.BoogieExprTranslator.isPositiveContext;
        }

        {
          var subsumption = Subsumption(ctxt.Options, ac);
          if (subsumption == CoreOptions.SubsumptionOption.Always
              || (subsumption == CoreOptions.SubsumptionOption.NotForQuantifiers && !(C is VCExprQuantifier)))
          {
            // Translate ac.Expr again so that we create separate VC expressions for the two different
            // occurrences of the translation of ac.Expr.  Pool-based quantifier instantiation assumes
            // that the only sharing in the verification condition is via explicit let bindings.
            N = gen.ImpliesSimp(ctxt.Ctxt.BoogieExprTranslator.Translate(ac.Expr), N, false);
          }

          if (isFullyVerified)
          {
            return N;
          }
          else if (VU != null)
          {
            C = gen.OrSimp(VU, C);
          }

          ctxt.absyIds.GetId(ac);

          ctxt.AssertionCount++;

          if (ctxt.ControlFlowVariableExpr == null)
          {
            Contract.Assert(ctxt.absyIds != null);
            return gen.AndSimp(C, N);
          }
          else
          {
            VCExpr controlFlowFunctionAppl = gen.ControlFlowFunctionApplication(ctxt.ControlFlowVariableExpr,
              gen.Integer(BigNum.FromInt(ctxt.absyIds.GetId(b))));
            Contract.Assert(controlFlowFunctionAppl != null);
            VCExpr assertFailure = gen.Eq(controlFlowFunctionAppl, gen.Integer(BigNum.FromInt(-ctxt.absyIds.GetId(ac))));
            return gen.AndSimp(gen.Implies(assertFailure, C), N);
          }
        }
      }
      else if (cmd is AssumeCmd)
      {
        AssumeCmd ac = (AssumeCmd) cmd;

        if (ctxt.Options.StratifiedInlining > 0)
        {
          // Label the assume if it is a procedure call
          NAryExpr naryExpr = ac.Expr as NAryExpr;
          if (naryExpr != null)
          {
            if (naryExpr.Fun is FunctionCall)
            {
              ctxt.absyIds.GetId(ac);
              return MaybeWrapWithOptimization(ctxt, gen, ac.Attributes,
                gen.ImpliesSimp(ctxt.Ctxt.BoogieExprTranslator.Translate(ac.Expr), N));
            }
          }
        }

        var expr = ctxt.Ctxt.BoogieExprTranslator.Translate(ac.Expr);

        var aid = QKeyValue.FindStringAttribute(ac.Attributes, "id");
        if (aid != null)
        {
          var isTry = QKeyValue.FindBoolAttribute(ac.Attributes, "try");
          var v = gen.Variable((isTry ? "try$$" : "assume$$") + aid, Microsoft.Boogie.Type.Bool);
          expr = gen.Function(VCExpressionGenerator.NamedAssumeOp, v, gen.ImpliesSimp(v, expr));
        }
      
        var soft = QKeyValue.FindBoolAttribute(ac.Attributes, "soft");
        var softWeight = QKeyValue.FindIntAttribute(ac.Attributes, "soft", 0);
        if ((soft || 0 < softWeight) && aid != null)
        {
          var v = gen.Variable("soft$$" + aid, Microsoft.Boogie.Type.Bool);
          expr = gen.Function(new VCExprSoftOp(Math.Max(softWeight, 1)), v, gen.ImpliesSimp(v, expr));
        }

        return MaybeWrapWithOptimization(ctxt, gen, ac.Attributes, gen.ImpliesSimp(expr, N));
      }
      else
      {
        Console.WriteLine(cmd.ToString());
        Contract.Assert(false);
        throw new cce.UnreachableException(); // unexpected command
      }
    }

    internal static VCExpr P_Cmd(Block b, HashSet<string> constantsAssumed, Cmd cmd, VCExpr N, VCContext ctxt)
    {
      Contract.Requires(cmd != null);
      Contract.Requires(N != null);
      Contract.Requires(ctxt != null);
      Contract.Ensures(Contract.Result<VCExpr>() != null);

      VCExpressionGenerator gen = ctxt.Ctxt.ExprGen;
      Contract.Assert(gen != null);
      if (cmd is AssertCmd)
      {
        AssertCmd ac = (AssertCmd) cmd;

        var isFullyVerified = false;
        if (ac.VerifiedUnder != null)
        {
          var litExpr = ac.VerifiedUnder as LiteralExpr;
          isFullyVerified = litExpr != null && litExpr.IsTrue;
        }

        if (!isFullyVerified)
        {
          ctxt.Ctxt.BoogieExprTranslator.isPositiveContext = !ctxt.Ctxt.BoogieExprTranslator.isPositiveContext;
        }

        VCExpr C = ctxt.Ctxt.BoogieExprTranslator.Translate(ac.Expr);

        VCExpr VU = null;
        if (!isFullyVerified)
        {
          if (ac.VerifiedUnder != null)
          {
            VU = ctxt.Ctxt.BoogieExprTranslator.Translate(ac.VerifiedUnder);

            if (ctxt.Options.RunDiagnosticsOnTimeout)
            {
              ctxt.Ctxt.TimeoutDiagnosticIDToAssertion[ctxt.Ctxt.TimeoutDiagnosticsCount] =
                new Tuple<AssertCmd, TransferCmd>(ac, b.TransferCmd);
              VU = gen.Or(VU,
                gen.Function(VCExpressionGenerator.TimeoutDiagnosticsOp,
                  gen.Integer(BigNum.FromInt(ctxt.Ctxt.TimeoutDiagnosticsCount++))));
            }
          }
          else if (ctxt.Options.RunDiagnosticsOnTimeout)
          {
            ctxt.Ctxt.TimeoutDiagnosticIDToAssertion[ctxt.Ctxt.TimeoutDiagnosticsCount] =
              new Tuple<AssertCmd, TransferCmd>(ac, b.TransferCmd);
            VU = gen.Function(VCExpressionGenerator.TimeoutDiagnosticsOp,
              gen.Integer(BigNum.FromInt(ctxt.Ctxt.TimeoutDiagnosticsCount++)));
          }

          ctxt.Ctxt.BoogieExprTranslator.isPositiveContext = !ctxt.Ctxt.BoogieExprTranslator.isPositiveContext;
        }

        {
          var subsumption = Subsumption(ctxt.Options, ac);
          if (subsumption == CoreOptions.SubsumptionOption.Always
              || (subsumption == CoreOptions.SubsumptionOption.NotForQuantifiers && !(C is VCExprQuantifier)))
          {
            // Translate ac.Expr again so that we create separate VC expressions for the two different
            // occurrences of the translation of ac.Expr.  Pool-based quantifier instantiation assumes
            // that the only sharing in the verification condition is via explicit let bindings.
            N = gen.ImpliesSimp(ctxt.Ctxt.BoogieExprTranslator.Translate(ac.Expr), N, false);
          }

          if (isFullyVerified)
          {
            return N;
          }
          else if (VU != null)
          {
            C = gen.OrSimp(VU, C);
          }

          ctxt.absyIds.GetId(ac);

          ctxt.AssertionCount++;

          if (ctxt.ControlFlowVariableExpr == null)
          {
            Contract.Assert(ctxt.absyIds != null);
            return gen.AndSimp(C, N);
          }
          else
          {
            VCExpr controlFlowFunctionAppl = gen.ControlFlowFunctionApplication(ctxt.ControlFlowVariableExpr,
              gen.Integer(BigNum.FromInt(ctxt.absyIds.GetId(b))));
            Contract.Assert(controlFlowFunctionAppl != null);
            VCExpr assertFailure = gen.Eq(controlFlowFunctionAppl, gen.Integer(BigNum.FromInt(-ctxt.absyIds.GetId(ac))));
            return gen.AndSimp(gen.Implies(assertFailure, C), N);
          }
        }
      }
      else if (cmd is AssumeCmd)
      {
        AssumeCmd ac = (AssumeCmd) cmd;

        if (ctxt.Options.StratifiedInlining > 0)
        {
          // Label the assume if it is a procedure call
          NAryExpr naryExpr = ac.Expr as NAryExpr;
          if (naryExpr != null)
          {
            if (naryExpr.Fun is FunctionCall)
            {
              ctxt.absyIds.GetId(ac);
              return MaybeWrapWithOptimization(ctxt, gen, ac.Attributes,
                gen.ImpliesSimp(ctxt.Ctxt.BoogieExprTranslator.Translate(ac.Expr), N));
            }
          }
        }
        else if(constantsAssumed != null)
        {
          // check if the assume command contains any of the constantsAssumed (corresponding to existential function calls)?
          var funcsUsed = FunctionCollector.Collect(ac.Expr);
          bool hasAnyConstantAssumed = false;
          foreach (var tup in funcsUsed)
          {
              var constantFunc = tup.Item1;
              if (constantsAssumed.Contains(constantFunc.Name))
              {
                  hasAnyConstantAssumed = true;
                  break;
              }
          }
          if(hasAnyConstantAssumed)
          {
            int id = ac.UniqueId;
            if(ctxt.absyIds != null)
            {
              ctxt.absyIds.GetId(ac);
            }
            
            if(ctxt.ControlFlowVariableExpr == null)
            {
              Contract.Assert(ctxt.absyIds != null);
              return MaybeWrapWithOptimization(ctxt, gen, ac.Attributes,
                gen.ImpliesSimp(ctxt.Ctxt.BoogieExprTranslator.Translate(ac.Expr), N));
            }
          }
          
        }

        var expr = ctxt.Ctxt.BoogieExprTranslator.Translate(ac.Expr);

        var aid = QKeyValue.FindStringAttribute(ac.Attributes, "id");
        if (aid != null)
        {
          var isTry = QKeyValue.FindBoolAttribute(ac.Attributes, "try");
          var v = gen.Variable((isTry ? "try$$" : "assume$$") + aid, Microsoft.Boogie.Type.Bool);
          expr = gen.Function(VCExpressionGenerator.NamedAssumeOp, v, gen.ImpliesSimp(v, expr));
        }

        var soft = QKeyValue.FindBoolAttribute(ac.Attributes, "soft");
        var softWeight = QKeyValue.FindIntAttribute(ac.Attributes, "soft", 0);
        if ((soft || 0 < softWeight) && aid != null)
        {
          var v = gen.Variable("soft$$" + aid, Microsoft.Boogie.Type.Bool);
          expr = gen.Function(new VCExprSoftOp(Math.Max(softWeight, 1)), v, gen.ImpliesSimp(v, expr));
        }

        return MaybeWrapWithOptimization(ctxt, gen, ac.Attributes, gen.ImpliesSimp(expr, N));
      }
      else
      {
        Console.WriteLine(cmd.ToString());
        Contract.Assert(false);
        throw new cce.UnreachableException(); // unexpected command
      }
    }

    private static VCExpr MaybeWrapWithOptimization(VCContext ctxt, VCExpressionGenerator gen, QKeyValue attrs,
      VCExpr expr)
    {
      var min = QKeyValue.FindExprAttribute(attrs, "minimize");
      if (min != null)
      {
        expr = gen.Function(VCExpressionGenerator.MinimizeOp, ctxt.Ctxt.BoogieExprTranslator.Translate(min), expr);
      }

      var max = QKeyValue.FindExprAttribute(attrs, "maximize");
      if (max != null)
      {
        expr = gen.Function(VCExpressionGenerator.MaximizeOp, ctxt.Ctxt.BoogieExprTranslator.Translate(max), expr);
      }

      return expr;
    }

    public static CoreOptions.SubsumptionOption Subsumption(VCGenOptions options, AssertCmd ac)
    {
      Contract.Requires(ac != null);
      int n = QKeyValue.FindIntAttribute(ac.Attributes, "subsumption", -1);
      switch (n)
      {
        case 0: return CoreOptions.SubsumptionOption.Never;
        case 1: return CoreOptions.SubsumptionOption.NotForQuantifiers;
        case 2: return CoreOptions.SubsumptionOption.Always;
        default: return options.UseSubsumption;
      }
    }

    public static VCExpr RegExpr(RE r, VCExpr N, VCContext ctxt)
    {
      Contract.Requires(r != null);
      Contract.Requires(N != null);
      Contract.Requires(ctxt != null);
      Contract.Ensures(Contract.Result<VCExpr>() != null);

      if (r is AtomicRE)
      {
        AtomicRE ar = (AtomicRE) r;
        return Block(ar.b, N, ctxt);
      }
      else if (r is Sequential)
      {
        Sequential s = (Sequential) r;
        return RegExpr(s.first, RegExpr(s.second, N, ctxt), ctxt);
      }
      else if (r is Choice)
      {
        Choice ch = (Choice) r;
        VCExpr res;
        if (ch.rs == null || ch.rs.Count == 0)
        {
          res = N;
        }
        else
        {
          VCExpr currentWLP = RegExpr(cce.NonNull(ch.rs[0]), N, ctxt);
          for (int i = 1, n = ch.rs.Count; i < n; i++)
          {
            currentWLP = ctxt.Ctxt.ExprGen.And(currentWLP, RegExpr(cce.NonNull(ch.rs[i]), N, ctxt));
          }

          res = currentWLP;
        }

        return res;
      }
      else
      {
        Contract.Assert(false);
        throw new cce.UnreachableException(); // unexpected RE subtype
      }
    }

    public static VCExpr P_RegExpr(RE r, HashSet<string> constantsAssumed, VCExpr N, VCContext ctxt)
    {
      Contract.Requires(r != null);
      Contract.Requires(N != null);
      Contract.Requires(ctxt != null);
      Contract.Ensures(Contract.Result<VCExpr>() != null);

      if (r is AtomicRE)
      {
        AtomicRE ar = (AtomicRE) r; 
        return P_Block(ar.b, constantsAssumed, N, ctxt);
      }
      else if (r is Sequential)
      {
        Sequential s = (Sequential) r;
        return P_RegExpr(s.first, constantsAssumed, P_RegExpr(s.second, constantsAssumed, N, ctxt), ctxt);
      }
      else if (r is Choice)
      {
        Choice ch = (Choice) r;
        VCExpr res;
        if (ch.rs == null || ch.rs.Count == 0)
        {
          res = N;
        }
        else
        {
          VCExpr currentWLP = P_RegExpr(cce.NonNull(ch.rs[0]), constantsAssumed, N, ctxt);
          for (int i = 1, n = ch.rs.Count; i < n; i++)
          {
            currentWLP = ctxt.Ctxt.ExprGen.And(currentWLP, P_RegExpr(cce.NonNull(ch.rs[i]), constantsAssumed, N, ctxt));
          }

          res = currentWLP;
        }

        return res;
      }
      else
      {
        Contract.Assert(false);
        throw new cce.UnreachableException(); // unexpected RE subtype
      }
    }
  }

  

  #endregion
}