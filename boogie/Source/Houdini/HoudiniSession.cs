using System;
using System.Diagnostics.Contracts;
using System.Collections.Generic;
using System.IO;
using Microsoft.Boogie.VCExprAST;
using Microsoft.BaseTypes;
using VC;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Boogie.Houdini
{
  public class ExistentialConstantCollector : ReadOnlyVisitor
  {
    public static void CollectHoudiniConstants(Houdini houdini, Implementation impl,
      out ExistentialConstantCollector collector)
    {
      collector = new ExistentialConstantCollector(houdini);
      collector.impl = impl;
      collector.VisitImplementation(impl);
    }

    private ExistentialConstantCollector(Houdini houdini)
    {
      this.houdini = houdini;
      this.houdiniAssertConstants = new HashSet<Variable>();
      this.houdiniAssumeConstants = new HashSet<Variable>();

      this.explainNegative = new HashSet<Variable>();
      this.explainPositive = new HashSet<Variable>();
      this.constToControl = new Dictionary<string, Tuple<Variable, Variable>>();
    }

    private Houdini houdini;
    public HashSet<Variable> houdiniAssertConstants;
    public HashSet<Variable> houdiniAssumeConstants;

    // Explain Houdini stuff
    public HashSet<Variable> explainPositive;
    public HashSet<Variable> explainNegative;
    public Dictionary<string, Tuple<Variable, Variable>> constToControl;
    Implementation impl;

    public override Cmd VisitAssertRequiresCmd(AssertRequiresCmd node)
    {
      AddHoudiniConstant(node);
      return base.VisitAssertRequiresCmd(node);
    }

    public override Cmd VisitAssertEnsuresCmd(AssertEnsuresCmd node)
    {
      AddHoudiniConstant(node);
      return base.VisitAssertEnsuresCmd(node);
    }

    public override Cmd VisitAssertCmd(AssertCmd node)
    {
      AddHoudiniConstant(node);
      return base.VisitAssertCmd(node);
    }

    public override Cmd VisitAssumeCmd(AssumeCmd node)
    {
      AddHoudiniConstant(node);
      return base.VisitAssumeCmd(node);
    }

    private void AddHoudiniConstant(AssertCmd assertCmd)
    {
      if (houdini.MatchCandidate(assertCmd.Expr, out Variable houdiniConstant))
      {
        houdiniAssertConstants.Add(houdiniConstant);
      }

      if (houdiniConstant != null && houdini.Options.ExplainHoudini &&
          !constToControl.ContainsKey(houdiniConstant.Name))
      {
        // For each houdini constant c, create two more constants c_pos and c_neg.
        // Then change the asserted condition (c ==> \phi) to
        // (c ==> (c_pos && (\phi || \not c_neg))
        var control = createNewExplainConstants(houdiniConstant);
        assertCmd.Expr = houdini.InsertCandidateControl(assertCmd.Expr, control.Item1, control.Item2);
        explainPositive.Add(control.Item1);
        explainNegative.Add(control.Item2);
        constToControl.Add(houdiniConstant.Name, control);
      }
    }

    private void AddHoudiniConstant(AssumeCmd assumeCmd)
    {
      if (houdini.MatchCandidate(assumeCmd.Expr, out Variable houdiniConstant))
      {
        houdiniAssumeConstants.Add(houdiniConstant);
      }
    }

    private Tuple<Variable, Variable> createNewExplainConstants(Variable v)
    {
      Contract.Assert(impl != null);
      Contract.Assert(houdini.Options.ExplainHoudini);
      Variable v1 = new Constant(Token.NoToken,
        new TypedIdent(Token.NoToken, string.Format("{0}_{1}_{2}", v.Name, impl.Name, "pos"),
          Microsoft.Boogie.BasicType.Bool));
      Variable v2 = new Constant(Token.NoToken,
        new TypedIdent(Token.NoToken, string.Format("{0}_{1}_{2}", v.Name, impl.Name, "neg"),
          Microsoft.Boogie.BasicType.Bool));

      return Tuple.Create(v1, v2);
    }
  }


  public class HoudiniSession : ProofRun
  {
    public class HoudiniStatistics
    {
      public double proverTime = 0;
      public int numProverQueries = 0;
      public double unsatCoreProverTime = 0;
      public int numUnsatCoreProverQueries = 0;
      public int numUnsatCorePrunings = 0;
    }

    public string Description { get; }
    private readonly Houdini houdini;
    public HoudiniStatistics stats;
    public List<Counterexample> Counterexamples { get; } = new();
    private VCExpr conjecture;
    private ProverInterface.ErrorHandler handler;
    ConditionGeneration.VerificationResultCollector collector;
    HashSet<Variable> unsatCoreSet;
    HashSet<Variable> houdiniConstants;
    public HashSet<Variable> houdiniAssertConstants;
    private HashSet<Variable> houdiniAssumeConstants;

    // Extra constants created for ExplainHoudini
    private HashSet<Variable> explainConstantsPositive;
    private HashSet<Variable> explainConstantsNegative;
    private Dictionary<string, Tuple<Variable, Variable>> constantToControl;

    public bool InUnsatCore(Variable constant)
    {
      if (unsatCoreSet == null)
      {
        return true;
      }

      if (unsatCoreSet.Contains(constant))
      {
        return true;
      }

      stats.numUnsatCorePrunings++;
      return false;
    }

    public HoudiniSession(TextWriter traceWriter, Houdini houdini, VCGen vcgen, ProverInterface proverInterface, Program program,
      Implementation impl, HoudiniStatistics stats, int taskID = -1)
    {
      this.Description = impl.Name;
      this.houdini = houdini;
      this.stats = stats;
      collector = new ConditionGeneration.VerificationResultCollector(houdini.Options);
      collector.OnProgress?.Invoke("HdnVCGen", 0, 0, 0.0);

      vcgen.ConvertCFG2DAG(impl, taskID: taskID);
      var gotoCmdOrigins = vcgen.PassifyImpl(new ImplementationRun(impl, traceWriter), out var mvInfo);

      ExistentialConstantCollector.CollectHoudiniConstants(houdini, impl, out var ecollector);
      this.houdiniAssertConstants = ecollector.houdiniAssertConstants;
      this.houdiniAssumeConstants = ecollector.houdiniAssumeConstants;
      this.explainConstantsNegative = ecollector.explainNegative;
      this.explainConstantsPositive = ecollector.explainPositive;
      this.constantToControl = ecollector.constToControl;

      houdiniConstants = new HashSet<Variable>();
      houdiniConstants.UnionWith(houdiniAssertConstants);
      houdiniConstants.UnionWith(houdiniAssumeConstants);

      var exprGen = proverInterface.Context.ExprGen;
      VCExpr controlFlowVariableExpr = exprGen.Integer(BigNum.ZERO);

      var absyIds = new ControlFlowIdMap<Absy>();
      conjecture = vcgen.GenerateVC(impl, controlFlowVariableExpr, absyIds, proverInterface.Context);

      VCExpr controlFlowFunctionAppl =
        exprGen.ControlFlowFunctionApplication(exprGen.Integer(BigNum.ZERO), exprGen.Integer(BigNum.ZERO));
      VCExpr eqExpr = exprGen.Eq(controlFlowFunctionAppl, exprGen.Integer(BigNum.FromInt(absyIds.GetId(impl.Blocks[0]))));
      conjecture = exprGen.Implies(eqExpr, conjecture);

      Macro macro = new Macro(Token.NoToken, Description, new List<Variable>(),
        new Formal(Token.NoToken, new TypedIdent(Token.NoToken, "", Type.Bool), false));
      proverInterface.DefineMacro(macro, conjecture);
      conjecture = exprGen.Function(macro);
      handler = new VCGen.ErrorReporter(this.houdini.Options, gotoCmdOrigins, absyIds, impl.Blocks, vcgen.debugInfos, collector,
        mvInfo, proverInterface.Context, program, this);
    }

    private VCExpr BuildAxiom(ProverInterface proverInterface, Dictionary<Variable, bool> currentAssignment)
    {
      ProverContext proverContext = proverInterface.Context;
      Boogie2VCExprTranslator exprTranslator = proverContext.BoogieExprTranslator;
      VCExpressionGenerator exprGen = proverInterface.VCExprGen;

      VCExpr expr = VCExpressionGenerator.True;

      foreach (KeyValuePair<Variable, bool> kv in currentAssignment)
      {
        Variable constant = kv.Key;
        VCExprVar exprVar = exprTranslator.LookupVariable(constant);
        if (kv.Value)
        {
          expr = exprGen.And(expr, exprVar);
        }
        else
        {
          expr = exprGen.And(expr, exprGen.Not(exprVar));
        }
      }

      if (houdini.Options.ExplainHoudini)
      {
        // default values for ExplainHoudini control variables
        foreach (var constant in explainConstantsNegative.Concat(explainConstantsPositive))
        {
          expr = exprGen.And(expr, exprTranslator.LookupVariable(constant));
        }
      }

      /*
      foreach (Variable constant in this.houdiniConstants) {
        VCExprVar exprVar = exprTranslator.LookupVariable(constant);
        if (currentAssignment[constant]) {
          expr = exprGen.And(expr, exprVar);
        }
        else {
          expr = exprGen.And(expr, exprGen.Not(exprVar));
        }
      }
       */

      if (Options.Trace)
      {
        Console.WriteLine("Houdini assignment axiom: " + expr);
      }

      return expr;
    }

    public HoudiniOptions Options => houdini.Options;

    public async Task<(ProverInterface.Outcome, List<Counterexample> errors)> Verify(
      ProverInterface proverInterface,
      Dictionary<Variable, bool> assignment,
      int errorLimit)
    {
      collector.examples.Clear();

      if (Options.Trace)
      {
        Console.WriteLine("Verifying " + Description);
      }

      DateTime now = DateTime.UtcNow;

      VCExpr vc = proverInterface.VCExprGen.Implies(BuildAxiom(proverInterface, assignment), conjecture);
      var proverOutcome = await proverInterface.Check(Description, vc, handler, errorLimit, CancellationToken.None);

      double queryTime = (DateTime.UtcNow - now).TotalSeconds;
      stats.proverTime += queryTime;
      stats.numProverQueries++;
      if (Options.Trace)
      {
        Console.WriteLine("Outcome = " + proverOutcome);
        Console.WriteLine("Time taken = " + queryTime);
      }

      return (proverOutcome, collector.examples.ToList());
    }

    // MAXSAT
    public async Task Explain(ProverInterface proverInterface,
      Dictionary<Variable, bool> assignment, Variable refutedConstant)
    {
      Contract.Assert(houdini.Options.ExplainHoudini);

      collector.examples.Clear();

      // debugging
      houdiniAssertConstants.Iter(v => System.Diagnostics.Debug.Assert(assignment.ContainsKey(v)));
      houdiniAssumeConstants.Iter(v => System.Diagnostics.Debug.Assert(assignment.ContainsKey(v)));
      Contract.Assert(assignment.ContainsKey(refutedConstant));
      Contract.Assert(houdiniAssertConstants.Contains(refutedConstant));

      var hardAssumptions = new List<VCExpr>();
      var softAssumptions = new List<VCExpr>();

      Boogie2VCExprTranslator exprTranslator = proverInterface.Context.BoogieExprTranslator;
      VCExpressionGenerator exprGen = proverInterface.VCExprGen;
      var controlExpr = VCExpressionGenerator.True;

      foreach (var tup in assignment)
      {
        Variable constant = tup.Key;
        VCExprVar exprVar = exprTranslator.LookupVariable(constant);
        var val = tup.Value;

        if (houdiniAssumeConstants.Contains(constant))
        {
          if (tup.Value)
          {
            hardAssumptions.Add(exprVar);
          }
          else
          {
            // Previously removed assumed candidates are the soft constraints
            softAssumptions.Add(exprVar);
          }
        }
        else if (houdiniAssertConstants.Contains(constant))
        {
          if (constant == refutedConstant)
          {
            hardAssumptions.Add(exprVar);
          }
          else
          {
            hardAssumptions.Add(exprGen.Not(exprVar));
          }
        }
        else
        {
          if (tup.Value)
          {
            hardAssumptions.Add(exprVar);
          }
          else
          {
            hardAssumptions.Add(exprGen.Not(exprVar));
          }
        }

        // For an asserted condition (c ==> \phi), 
        // ExplainHoudini's extra control constants (c_pos, c_neg) are used as follows:
        //   (true, true): "assert \phi"
        //   (false, _): "assert false"
        //   (true, false): "assert true"
        if (constant != refutedConstant && constantToControl.ContainsKey(constant.Name))
        {
          var posControl = constantToControl[constant.Name].Item1;
          var negControl = constantToControl[constant.Name].Item2;

          // Handle self-recursion
          if (houdiniAssertConstants.Contains(constant) && houdiniAssumeConstants.Contains(constant))
          {
            // disable this assert
            controlExpr = exprGen.And(controlExpr,
              exprGen.And(exprTranslator.LookupVariable(posControl),
                exprGen.Not(exprTranslator.LookupVariable(negControl))));
          }
          else
          {
            // default values for control variables
            controlExpr = exprGen.And(controlExpr,
              exprGen.And(exprTranslator.LookupVariable(posControl), exprTranslator.LookupVariable(negControl)));
          }
        }
      }

      hardAssumptions.Add(exprGen.Not(conjecture));

      // default values for control variables
      Contract.Assert(constantToControl.ContainsKey(refutedConstant.Name));
      var pc = constantToControl[refutedConstant.Name].Item1;
      var nc = constantToControl[refutedConstant.Name].Item2;

      var controlExprNoop = exprGen.And(controlExpr,
        exprGen.And(exprTranslator.LookupVariable(pc), exprTranslator.LookupVariable(nc)));

      var controlExprFalse = exprGen.And(controlExpr,
        exprGen.And(exprGen.Not(exprTranslator.LookupVariable(pc)), exprGen.Not(exprTranslator.LookupVariable(nc))));

      if (Options.Trace)
      {
        Console.WriteLine("Verifying (MaxSat) " + Description);
      }

      DateTime now = DateTime.UtcNow;

      var el = Options.ErrorLimit;
      Options.ErrorLimit = 1;

      var outcome = ProverInterface.Outcome.Undetermined;

      do
      {
        hardAssumptions.Add(controlExprNoop);
        (outcome, var unsatisfiedSoftAssumptions) = await proverInterface.CheckAssumptions(hardAssumptions, softAssumptions,
          handler, CancellationToken.None);
        hardAssumptions.RemoveAt(hardAssumptions.Count - 1);

        if (outcome == ProverInterface.Outcome.TimeOut || outcome == ProverInterface.Outcome.OutOfMemory ||
            outcome == ProverInterface.Outcome.OutOfResource || outcome == ProverInterface.Outcome.Undetermined)
        {
          break;
        }

        var reason = new HashSet<string>();
        unsatisfiedSoftAssumptions.Iter(i => reason.Add(softAssumptions[i].ToString()));
        if (Options.Trace)
        {
          Console.Write("Reason for removal of {0}: ", refutedConstant.Name);
          reason.Iter(r => Console.Write("{0} ", r));
          Console.WriteLine();
        }

        // Get rid of those constants from the "reason" that can even make
        // "assert false" pass

        hardAssumptions.Add(controlExprFalse);
        var softAssumptions2 = new List<VCExpr>();
        for (int i = 0; i < softAssumptions.Count; i++)
        {
          if (unsatisfiedSoftAssumptions.Contains(i))
          {
            softAssumptions2.Add(softAssumptions[i]);
            continue;
          }

          hardAssumptions.Add(softAssumptions[i]);
        }

        (outcome, var unsatisfiedSoftAssumptions2) = await proverInterface.CheckAssumptions(hardAssumptions, softAssumptions2,
          handler, CancellationToken.None);

        if (outcome == ProverInterface.Outcome.TimeOut || outcome == ProverInterface.Outcome.OutOfMemory ||
            outcome == ProverInterface.Outcome.OutOfResource || outcome == ProverInterface.Outcome.Undetermined)
        {
          break;
        }

        unsatisfiedSoftAssumptions2.Iter(i => reason.Remove(softAssumptions2[i].ToString()));
        var reason1 = new HashSet<string>(); //these are the reasons for inconsistency
        unsatisfiedSoftAssumptions2.Iter(i => reason1.Add(softAssumptions2[i].ToString()));

        if (Options.Trace)
        {
          Console.Write("Revised reason for removal of {0}: ", refutedConstant.Name);
          reason.Iter(r => Console.Write("{0} ", r));
          Console.WriteLine();
        }

        foreach (var r in reason)
        {
          Houdini.explainHoudiniDottyFile.WriteLine("{0} -> {1} [ label = \"{2}\" color=red ];", refutedConstant.Name,
            r, Description);
        }

        //also add the removed reasons using dotted edges (requires- x != 0, requires- x == 0 ==> assert x != 0)
        foreach (var r in reason1)
        {
          Houdini.explainHoudiniDottyFile.WriteLine("{0} -> {1} [ label = \"{2}\" color=blue style=dotted ];",
            refutedConstant.Name, r, Description);
        }
      } while (false);

      if (outcome == ProverInterface.Outcome.TimeOut || outcome == ProverInterface.Outcome.OutOfMemory ||
          outcome == ProverInterface.Outcome.OutOfResource || outcome == ProverInterface.Outcome.Undetermined)
      {
        Houdini.explainHoudiniDottyFile.WriteLine("{0} -> {1} [ label = \"{2}\" color=red ];", refutedConstant.Name,
          "TimeOut", Description);
      }

      Options.ErrorLimit = el;

      double queryTime = (DateTime.UtcNow - now).TotalSeconds;
      stats.proverTime += queryTime;
      stats.numProverQueries++;
      if (Options.Trace)
      {
        Console.WriteLine("Time taken = " + queryTime);
      }
    }

    public async Task UpdateUnsatCore(ProverInterface proverInterface, Dictionary<Variable, bool> assignment)
    {
      DateTime now = DateTime.UtcNow;

      Boogie2VCExprTranslator exprTranslator = proverInterface.Context.BoogieExprTranslator;
      proverInterface.Push();
      proverInterface.Assert(conjecture, false);
      foreach (var v in assignment.Keys)
      {
        if (assignment[v])
        {
          continue;
        }

        proverInterface.Assert(exprTranslator.LookupVariable(v), false);
      }

      List<Variable> assumptionVars = new List<Variable>();
      List<VCExpr> assumptionExprs = new List<VCExpr>();
      foreach (var v in assignment.Keys)
      {
        if (!assignment[v])
        {
          continue;
        }

        assumptionVars.Add(v);
        assumptionExprs.Add(exprTranslator.LookupVariable(v));
      }

      (ProverInterface.Outcome tmp, var unsatCore) = await proverInterface.CheckAssumptions(assumptionExprs, handler, CancellationToken.None);
      System.Diagnostics.Debug.Assert(tmp == ProverInterface.Outcome.Valid);
      unsatCoreSet = new HashSet<Variable>();
      foreach (int i in unsatCore)
      {
        unsatCoreSet.Add(assumptionVars[i]);
      }

      proverInterface.Pop();

      double unsatCoreQueryTime = (DateTime.UtcNow - now).TotalSeconds;
      stats.unsatCoreProverTime += unsatCoreQueryTime;
      stats.numUnsatCoreProverQueries++;
    }
  }
}