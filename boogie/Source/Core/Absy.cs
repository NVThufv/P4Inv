using System;
using System.Linq;
using System.Collections;
using System.Diagnostics;
using System.Collections.Generic;
using System.Diagnostics.Contracts;
using Microsoft.BaseTypes;
using Microsoft.Boogie.GraphUtil;

namespace Microsoft.Boogie
{
  [ContractClass(typeof(AbsyContracts))]
  public abstract class Absy
  {
    private IToken /*!*/
      _tok;

    [ContractInvariantMethod]
    void ObjectInvariant()
    {
      Contract.Invariant(this._tok != null);
    }

    public IToken tok
    {
      //Rename this property and "_tok" if possible
      get
      {
        Contract.Ensures(Contract.Result<IToken>() != null);
        return this._tok;
      }
      set
      {
        Contract.Requires(value != null);
        this._tok = value;
      }
    }

    public int Line
    {
      get { return tok != null ? tok.line : -1; }
    }

    public int Col
    {
      get { return tok != null ? tok.col : -1; }
    }

    public Absy(IToken tok)
    {
      Contract.Requires(tok != null);
      this._tok = tok;
      this.UniqueId = System.Threading.Interlocked.Increment(ref CurrentAbsyNodeId);
    }

    private static int CurrentAbsyNodeId = -1;

    // We uniquely number every AST node to make them
    // suitable for our implementation of functional maps.
    //
    public int UniqueId { get; private set; }

    private const int indent_size = 2;

    protected static string Indent(int level)
    {
      return new string(' ', (indent_size * level));
    }
    
    [NeedsContracts]
    public abstract void Resolve(ResolutionContext /*!*/ rc);

    /// <summary>
    /// Requires the object to have been successfully resolved.
    /// </summary>
    /// <param name="tc"></param>
    [NeedsContracts]
    public abstract void Typecheck(TypecheckingContext /*!*/ tc);

    /// <summary>
    /// Introduced so the uniqueId is not the same on a cloned object.
    /// </summary>
    /// <param name="tc"></param>
    public virtual Absy Clone()
    {
      Contract.Ensures(Contract.Result<Absy>() != null);
      Absy /*!*/
        result = cce.NonNull((Absy /*!*/) this.MemberwiseClone());
      result.UniqueId = System.Threading.Interlocked.Increment(ref CurrentAbsyNodeId); // BUGBUG??

      if (InternalNumberedMetadata != null)
      {
        // This should probably use the lock
        result.InternalNumberedMetadata = new List<Object>(this.InternalNumberedMetadata);
      }

      return result;
    }

    public virtual Absy StdDispatch(StandardVisitor visitor)
    {
      Contract.Requires(visitor != null);
      Contract.Ensures(Contract.Result<Absy>() != null);
      System.Diagnostics.Debug.Fail("Unknown Absy node type: " + this.GetType());
      throw new System.NotImplementedException();
    }

    #region numberedmetadata

    // Implementation of Numbered Metadata
    // This allows any number of arbitrary objects to be
    // associated with an instance of an Absy at run time
    // in a type safe manner using an integer as a key.

    // We could use a dictionary but we use a List for look up speed
    // For this to work well the user needs to use small integers as
    // keys. The list is created lazily to minimise memory overhead.
    private volatile List<Object> InternalNumberedMetadata = null;

    // The lock exists to ensure that InternalNumberedMetadata is a singleton
    // for every instance of this class.
    // It is static to minimise the memory overhead (we don't want a lock per instance).
    private static readonly Object NumberedMetadataLock = new object();

    /// <summary>
    /// Gets the number of meta data objects associated with this instance
    /// </summary>
    /// <value>The numbered meta data count.</value>
    public int NumberedMetaDataCount
    {
      get { return InternalNumberedMetadata == null ? 0 : InternalNumberedMetadata.Count; }
    }

    /// <summary>
    /// Gets an IEnumerable over the numbered metadata associated
    /// with this instance.
    /// </summary>
    /// <value>
    /// The numbered meta data enumerable that looks like the Enumerable
    /// of a dictionary.
    /// </value>
    public IEnumerable<KeyValuePair<int, Object>> NumberedMetadata
    {
      get
      {
        if (InternalNumberedMetadata == null)
        {
          return Enumerable.Empty<KeyValuePair<int, Object>>();
        }
        else
        {
          return InternalNumberedMetadata.Select((v, index) => new KeyValuePair<int, Object>(index, v));
        }
      }
    }

    /// <summary>
    /// Gets the metatdata at specified index.
    /// ArgumentOutOfRange exception is raised if it is not available.
    /// InvalidCastExcpetion is raised if the metadata is available but the wrong type was requested.
    /// </summary>
    /// <returns>The stored metadata of type T</returns>
    /// <param name="index">The index of the metadata</param>
    /// <typeparam name="T">The type of the metadata object required</typeparam>
    public T GetMetadata<T>(int index)
    {
      // We aren't using NumberedMetadataLock for speed. Perhaps we should be using it?
      if (InternalNumberedMetadata == null)
      {
        throw new ArgumentOutOfRangeException();
      }

      if (InternalNumberedMetadata[index] is T)
      {
        return (T) InternalNumberedMetadata[index];
      }
      else if (InternalNumberedMetadata[index] == null)
      {
        throw new InvalidCastException("Numbered metadata " + index +
                                       " is null which cannot be casted to " + typeof(T));
      }
      else
      {
        throw new InvalidCastException("Numbered metadata " + index +
                                       " is of type " + InternalNumberedMetadata[index].GetType() +
                                       " rather than requested type " + typeof(T));
      }
    }

    private void InitialiseNumberedMetadata()
    {
      // Ensure InternalNumberedMetadata is a singleton
      if (InternalNumberedMetadata == null)
      {
        lock (NumberedMetadataLock)
        {
          if (InternalNumberedMetadata == null)
          {
            InternalNumberedMetadata = new List<Object>();
          }
        }
      }
    }

    /// <summary>
    /// Sets the metadata for this instace at a specified index.
    /// </summary>
    /// <param name="index">The index of the metadata</param>
    /// <param name="value">The value to set</param>
    /// <typeparam name="T">The type of value</typeparam>
    public void SetMetadata<T>(int index, T value)
    {
      InitialiseNumberedMetadata();

      if (index < 0)
      {
        throw new IndexOutOfRangeException();
      }

      lock (NumberedMetadataLock)
      {
        if (index < InternalNumberedMetadata.Count)
        {
          InternalNumberedMetadata[index] = value;
        }
        else
        {
          // Make sure expansion only happens once whilst we pad
          if (InternalNumberedMetadata.Capacity <= index)
          {
            // Use the next available power of 2
            InternalNumberedMetadata.Capacity = (int) Math.Pow(2, Math.Ceiling(Math.Log(index + 1, 2)));
          }

          // Pad with nulls
          while (InternalNumberedMetadata.Count < index)
          {
            InternalNumberedMetadata.Add(null);
          }

          InternalNumberedMetadata.Add(value);
          Debug.Assert(InternalNumberedMetadata.Count == (index + 1));
        }
      }
    }

    #endregion
  }

  public interface ICarriesAttributes
  {
    QKeyValue Attributes { get; set; }
  }

  [ContractClassFor(typeof(Absy))]
  public abstract class AbsyContracts : Absy
  {
    public override void Resolve(ResolutionContext rc)
    {
      Contract.Requires(rc != null);
      throw new NotImplementedException();
    }

    public AbsyContracts() : base(null)
    {
    }

    public override void Typecheck(TypecheckingContext tc)
    {
      Contract.Requires(tc != null);
      throw new NotImplementedException();
    }
  }

  public class Program : Absy
  {
    [ContractInvariantMethod]
    void ObjectInvariant()
    {
      Contract.Invariant(cce.NonNullElements(this.topLevelDeclarations));
      Contract.Invariant(cce.NonNullElements(this.globalVariablesCache, true));
    }

    public Dictionary<object, List<object>> DeclarationDependencies { get; set; }

    public Program()
      : base(Token.NoToken)
    {
      this.topLevelDeclarations = new List<Declaration>();
    }

    public void Emit(TokenTextWriter stream)
    {
      Contract.Requires(stream != null);
      stream.SetToken(this);
      this.topLevelDeclarations.Emit(stream);
    }

    /// <summary>
    /// Returns the number of name resolution errors.
    /// </summary>
    /// <returns></returns>
    public int Resolve(CoreOptions options)
    {
      return Resolve(options, null);
    }

    public int Resolve(CoreOptions options, IErrorSink errorSink)
    {
      ResolutionContext rc = new ResolutionContext(errorSink, options);
      Resolve(rc);
      return rc.ErrorCount;
    }

    public override void Resolve(ResolutionContext rc)
    {
      Helpers.ExtraTraceInformation(rc.Options, "Starting resolution");

      foreach (var d in TopLevelDeclarations)
      {
        d.Register(rc);
      }

      ResolveTypes(rc);
      
      var prunedTopLevelDeclarations = new List<Declaration /*!*/>();
      foreach (var d in TopLevelDeclarations.Where(d => !QKeyValue.FindBoolAttribute(d.Attributes, "ignore")))
      {
        // resolve all the declarations that have not been resolved yet 
        if (!(d is TypeCtorDecl || d is TypeSynonymDecl))
        {
          int e = rc.ErrorCount;
          d.Resolve(rc);
          if (rc.Options.OverlookBoogieTypeErrors && rc.ErrorCount != e && d is Implementation)
          {
            // ignore this implementation
            Console.WriteLine("Warning: Ignoring implementation {0} because of translation resolution errors",
              ((Implementation) d).Name);
            rc.ErrorCount = e;
            continue;
          }
        }
        prunedTopLevelDeclarations.Add(d);
      }

      ClearTopLevelDeclarations();
      AddTopLevelDeclarations(prunedTopLevelDeclarations);

      foreach (var v in Variables)
      {
        v.ResolveWhere(rc);
      }
    }

    private void ResolveTypes(ResolutionContext rc)
    {
      Contract.Requires(rc != null);
      // first resolve type constructors
      foreach (var d in TopLevelDeclarations.OfType<TypeCtorDecl>())
      {
        if (!QKeyValue.FindBoolAttribute(d.Attributes, "ignore"))
        {
          d.Resolve(rc);
        }
      }

      // collect type synonym declarations
      List<TypeSynonymDecl /*!*/> /*!*/
        synonymDecls = new List<TypeSynonymDecl /*!*/>();
      foreach (var d in TopLevelDeclarations.OfType<TypeSynonymDecl>())
      {
        Contract.Assert(d != null);
        if (!QKeyValue.FindBoolAttribute(d.Attributes, "ignore"))
        {
          synonymDecls.Add(d);
        }
      }

      // then resolve the type synonyms by a simple fixed-point iteration
      TypeSynonymDecl.ResolveTypeSynonyms(synonymDecls, rc);
      
      // and finally resolve the datatype constructors
      foreach (var datatypeTypeCtorDecl in TopLevelDeclarations.OfType<DatatypeTypeCtorDecl>())
      {
        foreach (var constructor in datatypeTypeCtorDecl.Constructors)
        {
          constructor.Resolve(rc);
        }
      }
    }

    public int Typecheck(CoreOptions options)
    {
      return this.Typecheck(options, (IErrorSink) null);
    }

    public int Typecheck(CoreOptions options, IErrorSink errorSink)
    {
      TypecheckingContext tc = new TypecheckingContext(errorSink, options);
      Typecheck(tc);
      return tc.ErrorCount;
    }

    public override void Typecheck(TypecheckingContext tc)
    {
      //Contract.Requires(tc != null);
      Helpers.ExtraTraceInformation(tc.Options, "Starting typechecking");

      int oldErrorCount = tc.ErrorCount;
      foreach (var d in TopLevelDeclarations)
      {
        d.Typecheck(tc);
      }

      if (oldErrorCount == tc.ErrorCount)
      {
        // check whether any type proxies have remained uninstantiated
        TypeAmbiguitySeeker /*!*/
          seeker = new TypeAmbiguitySeeker(tc);
        foreach (var d in TopLevelDeclarations)
        {
          seeker.Visit(d);
        }
      }
    }

    public override Absy Clone()
    {
      var cloned = (Program) base.Clone();
      cloned.topLevelDeclarations = new List<Declaration>();
      cloned.AddTopLevelDeclarations(topLevelDeclarations);
      return cloned;
    }

    [Rep] private List<Declaration /*!*/> /*!*/ topLevelDeclarations;

    public IReadOnlyList<Declaration> TopLevelDeclarations
    {
      get
      {
        Contract.Ensures(cce.NonNullElements(Contract.Result<IEnumerable<Declaration>>()));
        return topLevelDeclarations.AsReadOnly();
      }

      set
      {
        Contract.Requires(value != null);
        // materialize the decls, in case there is any dependency
        // back on topLevelDeclarations
        var v = value.ToList();
        // remove null elements
        v.RemoveAll(d => (d == null));
        // now clear the decls
        ClearTopLevelDeclarations();
        // and add the values
        AddTopLevelDeclarations(v);
      }
    }

    public void AddTopLevelDeclaration(Declaration decl)
    {
      Contract.Requires(!TopLevelDeclarationsAreFrozen);
      Contract.Requires(decl != null);

      topLevelDeclarations.Add(decl);
      this.globalVariablesCache = null;
    }

    public void AddTopLevelDeclarations(IEnumerable<Declaration> decls)
    {
      Contract.Requires(!TopLevelDeclarationsAreFrozen);
      Contract.Requires(cce.NonNullElements(decls));

      topLevelDeclarations.AddRange(decls);
      this.globalVariablesCache = null;
    }

    public void RemoveTopLevelDeclaration(Declaration decl)
    {
      Contract.Requires(!TopLevelDeclarationsAreFrozen);

      topLevelDeclarations.Remove(decl);
      this.globalVariablesCache = null;
    }

    public void RemoveTopLevelDeclarations(Predicate<Declaration> match)
    {
      Contract.Requires(!TopLevelDeclarationsAreFrozen);

      topLevelDeclarations.RemoveAll(match);
      this.globalVariablesCache = null;
    }

    public void ClearTopLevelDeclarations()
    {
      Contract.Requires(!TopLevelDeclarationsAreFrozen);

      topLevelDeclarations.Clear();
      this.globalVariablesCache = null;
    }

    bool topLevelDeclarationsAreFrozen;

    public bool TopLevelDeclarationsAreFrozen
    {
      get { return topLevelDeclarationsAreFrozen; }
    }

    public void FreezeTopLevelDeclarations()
    {
      topLevelDeclarationsAreFrozen = true;
    }

    Dictionary<string, Implementation> implementationsCache;

    public IEnumerable<Implementation> Implementations
    {
      get
      {
        if (implementationsCache != null)
        {
          return implementationsCache.Values;
        }

        var result = TopLevelDeclarations.OfType<Implementation>();
        if (topLevelDeclarationsAreFrozen)
        {
          implementationsCache = result.ToDictionary(p => p.Id);
        }

        return result;
      }
    }

    public Implementation FindImplementation(string id)
    {
      Implementation result = null;
      if (implementationsCache != null && implementationsCache.TryGetValue(id, out result))
      {
        return result;
      }
      else
      {
        return Implementations.FirstOrDefault(i => i.Id == id);
      }
    }

    List<Axiom> axiomsCache;

    public IEnumerable<Axiom> Axioms
    {
      get
      {
        if (axiomsCache != null)
        {
          return axiomsCache;
        }

        var result = TopLevelDeclarations.OfType<Axiom>();
        if (topLevelDeclarationsAreFrozen)
        {
          axiomsCache = result.ToList();
        }

        return result;
      }
    }

    Dictionary<string, Procedure> proceduresCache;

    public IEnumerable<Procedure> Procedures
    {
      get
      {
        if (proceduresCache != null)
        {
          return proceduresCache.Values;
        }

        var result = TopLevelDeclarations.OfType<Procedure>();
        if (topLevelDeclarationsAreFrozen)
        {
          proceduresCache = result.ToDictionary(p => p.Name);
        }

        return result;
      }
    }

    public Procedure FindProcedure(string name)
    {
      Procedure result = null;
      if (proceduresCache != null && proceduresCache.TryGetValue(name, out result))
      {
        return result;
      }
      else
      {
        return Procedures.FirstOrDefault(p => p.Name == name);
      }
    }

    Dictionary<string, Function> functionsCache;

    public IEnumerable<Function> Functions
    {
      get
      {
        if (functionsCache != null)
        {
          return functionsCache.Values;
        }

        var result = TopLevelDeclarations.OfType<Function>();
        if (topLevelDeclarationsAreFrozen)
        {
          functionsCache = result.ToDictionary(f => f.Name);
        }

        return result;
      }
    }

    public Function FindFunction(string name)
    {
      Function result = null;
      if (functionsCache != null && functionsCache.TryGetValue(name, out result))
      {
        return result;
      }
      else
      {
        return Functions.FirstOrDefault(f => f.Name == name);
      }
    }

    public IEnumerable<Variable> Variables
    {
      get { return TopLevelDeclarations.OfType<Variable>(); }
    }

    public IEnumerable<Constant> Constants
    {
      get { return TopLevelDeclarations.OfType<Constant>(); }
    }

    private IEnumerable<GlobalVariable /*!*/> globalVariablesCache = null;

    public List<GlobalVariable /*!*/> /*!*/ GlobalVariables
    {
      get
      {
        Contract.Ensures(cce.NonNullElements(Contract.Result<List<GlobalVariable>>()));

        if (globalVariablesCache == null)
        {
          globalVariablesCache = TopLevelDeclarations.OfType<GlobalVariable>();
        }

        return new List<GlobalVariable>(globalVariablesCache);
      }
    }

    public readonly ISet<string> NecessaryAssumes = new HashSet<string>();

    public IEnumerable<Block> Blocks()
    {
      return Implementations.Select(Item => Item.Blocks).SelectMany(Item => Item);
    }

    public void ComputeStronglyConnectedComponents()
    {
      foreach (var d in this.TopLevelDeclarations)
      {
        d.ComputeStronglyConnectedComponents();
      }
    }

    /// <summary>
    /// Reset the abstract stated computed before
    /// </summary>
    public void ResetAbstractInterpretationState()
    {
      foreach (var d in this.TopLevelDeclarations)
      {
        d.ResetAbstractInterpretationState();
      }
    }

    public void UnrollLoops(int n, bool uc)
    {
      Contract.Requires(0 <= n);
      foreach (var impl in Implementations)
      {
        if (impl.Blocks != null && impl.Blocks.Count > 0)
        {
          cce.BeginExpose(impl);
          {
            Block start = impl.Blocks[0];
            Contract.Assume(start != null);
            Contract.Assume(cce.IsConsistent(start));
            impl.Blocks = LoopUnroll.UnrollLoops(start, n, uc);
            impl.FreshenCaptureStates();
          }
          cce.EndExpose();
        }
      }
    }




    public static Graph<Implementation> BuildCallGraph(CoreOptions options, Program program)
    {
      Graph<Implementation> callGraph = new Graph<Implementation>();
      Dictionary<Procedure, HashSet<Implementation>> procToImpls = new Dictionary<Procedure, HashSet<Implementation>>();
      foreach (var proc in program.Procedures)
      {
        procToImpls[proc] = new HashSet<Implementation>();
      }

      foreach (var impl in program.Implementations)
      {
        if (impl.IsSkipVerification(options))
        {
          continue;
        }

        callGraph.AddSource(impl);
        procToImpls[impl.Proc].Add(impl);
      }

      foreach (var impl in program.Implementations)
      {
        if (impl.IsSkipVerification(options))
        {
          continue;
        }

        foreach (Block b in impl.Blocks)
        {
          foreach (Cmd c in b.Cmds)
          {
            CallCmd cc = c as CallCmd;
            if (cc == null)
            {
              continue;
            }

            foreach (Implementation callee in procToImpls[cc.Proc])
            {
              callGraph.AddEdge(impl, callee);
            }
          }
        }
      }

      return callGraph;
    }

    public static Graph<Block> GraphFromBlocks(List<Block> blocks, bool forward = true)
    {
      Graph<Block> g = new Graph<Block>();
      void AddEdge(Block a, Block b) {
        Contract.Assert(a != null && b != null);
        if (forward) {
          g.AddEdge(a, b);
        } else {
          g.AddEdge(b, a);
        }
      }

      g.AddSource(cce.NonNull(blocks[0])); // there is always at least one node in the graph
      foreach (Block b in blocks)
      {
        if (b.TransferCmd is GotoCmd gtc)
        {
          Contract.Assume(gtc.labelTargets != null);
          gtc.labelTargets.ForEach(dest => AddEdge(b, dest));
        }
      }
      return g;
    }

    public static Graph<Block /*!*/> /*!*/ GraphFromImpl(Implementation impl, bool forward = true)
    {
      Contract.Requires(impl != null);
      Contract.Ensures(cce.NonNullElements(Contract.Result<Graph<Block>>().Nodes));
      Contract.Ensures(Contract.Result<Graph<Block>>() != null);

      return GraphFromBlocks(impl.Blocks, forward);
    }

    public class IrreducibleLoopException : Exception
    {
    }

    public Graph<Block> ProcessLoops(CoreOptions options, Implementation impl)
    {
      impl.PruneUnreachableBlocks(options);
      impl.ComputePredecessorsForBlocks();
      Graph<Block> g = GraphFromImpl(impl);
      g.ComputeLoops();
      if (g.Reducible)
      {
        return g;
      }
      throw new IrreducibleLoopException();
    }


    public override Absy StdDispatch(StandardVisitor visitor)
    {
      //Contract.Requires(visitor != null);
      Contract.Ensures(Contract.Result<Absy>() != null);
      return visitor.VisitProgram(this);
    }

    int extractedFunctionCount;

    public string FreshExtractedFunctionName()
    {
      var c = System.Threading.Interlocked.Increment(ref extractedFunctionCount);
      return string.Format("##extracted_function##{0}", c);
    }

    private int invariantGenerationCounter = 0;

    public Constant MakeExistentialBoolean()
    {
      Constant ExistentialBooleanConstant = new Constant(Token.NoToken,
        new TypedIdent(tok, "_b" + invariantGenerationCounter, Microsoft.Boogie.Type.Bool), false);
      invariantGenerationCounter++;
      ExistentialBooleanConstant.AddAttribute("existential", new object[] {Expr.True});
      AddTopLevelDeclaration(ExistentialBooleanConstant);
      return ExistentialBooleanConstant;
    }

    public PredicateCmd CreateCandidateInvariant(Expr e, string tag = null)
    {
      Constant ExistentialBooleanConstant = MakeExistentialBoolean();
      IdentifierExpr ExistentialBoolean = new IdentifierExpr(Token.NoToken, ExistentialBooleanConstant);
      PredicateCmd invariant = new AssertCmd(Token.NoToken, Expr.Imp(ExistentialBoolean, e));
      if (tag != null)
      {
        invariant.Attributes = new QKeyValue(Token.NoToken, "tag", new List<object>(new object[] {tag}), null);
      }

      return invariant;
    }

    public Monomorphizer monomorphizer;
  }

  //---------------------------------------------------------------------
  // Declarations

  [ContractClass(typeof(DeclarationContracts))]
  public abstract class Declaration : Absy, ICarriesAttributes
  {
    public virtual int ContentHash => 1; 
    
    public QKeyValue Attributes { get; set; }

    public Declaration(IToken tok)
      : base(tok)
    {
      Contract.Requires(tok != null);
    }

    protected void EmitAttributes(TokenTextWriter stream)
    {
      Contract.Requires(stream != null);
      for (QKeyValue kv = this.Attributes; kv != null; kv = kv.Next)
      {
        kv.Emit(stream);
        stream.Write(" ");
      }
    }

    protected void ResolveAttributes(ResolutionContext rc)
    {
      Contract.Requires(rc != null);
      for (QKeyValue kv = this.Attributes; kv != null; kv = kv.Next)
      {
        kv.Resolve(rc);
      }
    }

    protected void TypecheckAttributes(TypecheckingContext rc)
    {
      Contract.Requires(rc != null);
      for (QKeyValue kv = this.Attributes; kv != null; kv = kv.Next)
      {
        kv.Typecheck(rc);
      }
    }

    /// <summary>
    /// If the declaration has an attribute {:name} or {:name true}, then set "result" to "true" and return "true".
    /// If the declaration has an attribute {:name false}, then set "result" to "false" and return "true".
    /// Otherwise, return "false" and leave "result" unchanged (which gives the caller an easy way to indicate
    /// a default value if the attribute is not mentioned).
    /// If there is more than one attribute called :name, then the last attribute rules.
    /// </summary>
    public bool CheckBooleanAttribute(string name, ref bool result)
    {
      Contract.Requires(name != null);
      var kv = FindAttribute(name);
      if (kv != null)
      {
        if (kv.Params.Count == 0)
        {
          result = true;
          return true;
        }
        else if (kv.Params.Count == 1)
        {
          var lit = kv.Params[0] as LiteralExpr;
          if (lit != null && lit.isBool)
          {
            result = lit.asBool;
            return true;
          }
        }
      }

      return false;
    }

    /// <summary>
    /// Find and return the last occurrence of an attribute with the name "name", if any.  If none, return null.
    /// </summary>
    public QKeyValue FindAttribute(string name)
    {
      Contract.Requires(name != null);
      QKeyValue res = null;
      for (QKeyValue kv = this.Attributes; kv != null; kv = kv.Next)
      {
        if (kv.Key == name)
        {
          res = kv;
        }
      }

      return res;
    }
    
    public QKeyValue FindIdenticalAttribute(QKeyValue target)
    {
      for (QKeyValue kv = this.Attributes; kv != null; kv = kv.Next)
      {
        if (kv.Key == target.Key && kv.Params.Count == target.Params.Count &&
            kv.Params.Zip(target.Params).All(x => x.Item1.Equals(x.Item2)))
        {
          return kv;
        }
      }
      return null;
    }

    // Look for {:name expr} in list of attributes.
    public Expr FindExprAttribute(string name)
    {
      Contract.Requires(name != null);
      Expr res = null;
      for (QKeyValue kv = this.Attributes; kv != null; kv = kv.Next)
      {
        if (kv.Key == name)
        {
          if (kv.Params.Count == 1 && kv.Params[0] is Expr)
          {
            res = (Expr) kv.Params[0];
          }
        }
      }

      return res;
    }

    // Look for {:name string} in list of attributes.
    public string FindStringAttribute(string name)
    {
      Contract.Requires(name != null);
      return QKeyValue.FindStringAttribute(this.Attributes, name);
    }

    // Look for {:name N} in list of attributes. Return false if attribute
    // 'name' does not exist or if N is not an integer constant.  Otherwise,
    // return true and update 'result' with N.
    public bool CheckIntAttribute(string name, ref int result)
    {
      Contract.Requires(name != null);
      Expr expr = FindExprAttribute(name);
      if (expr != null)
      {
        if (expr is LiteralExpr && ((LiteralExpr) expr).isBigNum)
        {
          result = ((LiteralExpr) expr).asBigNum.ToInt;
          return true;
        }
      }

      return false;
    }

    public bool CheckUIntAttribute(string name, ref uint result)
    {
      Contract.Requires(name != null);
      Expr expr = FindExprAttribute(name);
      if (expr != null)
      {
        if (expr is LiteralExpr && ((LiteralExpr) expr).isBigNum)
        {
          BigNum big = ((LiteralExpr) expr).asBigNum;
          if (big.IsNegative) {
            return false;
          }

          result = (uint)big.ToIntSafe;
          return true;
        }
      }

      return false;
    }

    public void AddAttribute(string name, params object[] vals)
    {
      Contract.Requires(name != null);
      QKeyValue kv;
      for (kv = this.Attributes; kv != null; kv = kv.Next)
      {
        if (kv.Key == name)
        {
          kv.AddParams(vals);
          break;
        }
      }

      if (kv == null)
      {
        Attributes = new QKeyValue(tok, name, new List<object /*!*/>(vals), Attributes);
      }
    }

    public abstract void Emit(TokenTextWriter /*!*/ stream, int level);
    public abstract void Register(ResolutionContext /*!*/ rc);

    /// <summary>
    /// Compute the strongly connected components of the declaration.
    /// By default, it does nothing
    /// </summary>
    public virtual void ComputeStronglyConnectedComponents()
    {
      /* Does nothing */
    }

    /// <summary>
    /// Reset the abstract stated computed before
    /// </summary>
    public virtual void ResetAbstractInterpretationState()
    {
      /* does nothing */
    }
  }

  [ContractClassFor(typeof(Declaration))]
  public abstract class DeclarationContracts : Declaration
  {
    public DeclarationContracts() : base(null)
    {
    }

    public override void Register(ResolutionContext rc)
    {
      Contract.Requires(rc != null);
      throw new NotImplementedException();
    }

    public override void Emit(TokenTextWriter stream, int level)
    {
      Contract.Requires(stream != null);
      throw new NotImplementedException();
    }
  }

  public class Axiom : Declaration
  {
    public override string ToString()
    {
      return "Axiom: " + expression.ToString();
    }

    private Expr /*!*/
      expression;

    public Expr Expr
    {
      get
      {
        Contract.Ensures(Contract.Result<Expr>() != null);
        return this.expression;
      }
      set
      {
        Contract.Requires(value != null);
        this.expression = value;
      }
    }

    [ContractInvariantMethod]
    void ExprInvariant()
    {
      Contract.Invariant(this.expression != null);
    }

    public string Comment;

    public Axiom(IToken tok, Expr expr)
      : this(tok, expr, null)
    {
      Contract.Requires(expr != null);
      Contract.Requires(tok != null);
    }

    public Axiom(IToken /*!*/ tok, Expr /*!*/ expr, string comment)
      : base(tok)
    {
      Contract.Requires(tok != null);
      Contract.Requires(expr != null);
      this.expression = expr;
      Comment = comment;
    }

    public Axiom(IToken tok, Expr expr, string comment, QKeyValue kv)
      : this(tok, expr, comment)
    {
      Contract.Requires(expr != null);
      Contract.Requires(tok != null);
      this.Attributes = kv;
    }

    public bool DependenciesCollected { get; set; }

    ISet<Function> functionDependencies;

    public ISet<Function> FunctionDependencies
    {
      get { return functionDependencies; }
    }

    public void AddFunctionDependency(Function function)
    {
      Contract.Requires(function != null);

      if (functionDependencies == null)
      {
        functionDependencies = new HashSet<Function>();
      }

      functionDependencies.Add(function);
    }

    public override int ContentHash => Util.GetHashCode(1218192003, expression.ContentHash);

    public override void Emit(TokenTextWriter stream, int level)
    {
      //Contract.Requires(stream != null);
      if (Comment != null)
      {
        stream.WriteLine(this, level, "// " + Comment);
      }

      stream.Write(this, level, "axiom ");
      EmitAttributes(stream);
      this.Expr.Emit(stream);
      stream.WriteLine(";");
    }

    public override void Register(ResolutionContext rc)
    {
      //Contract.Requires(rc != null);
      rc.AddAxiom(this);
    }

    public override void Resolve(ResolutionContext rc)
    {
      //Contract.Requires(rc != null);
      ResolveAttributes(rc);
      rc.StateMode = ResolutionContext.State.StateLess;
      Expr.Resolve(rc);
      rc.StateMode = ResolutionContext.State.Single;
    }

    public override void Typecheck(TypecheckingContext tc)
    {
      //Contract.Requires(tc != null);
      TypecheckAttributes(tc);
      Expr.Typecheck(tc);
      Contract.Assert(Expr.Type != null); // follows from postcondition of Expr.Typecheck
      if (!Expr.Type.Unify(Type.Bool))
      {
        tc.Error(this, "axioms must be of type bool");
      }
    }

    public override Absy StdDispatch(StandardVisitor visitor)
    {
      //Contract.Requires(visitor != null);
      Contract.Ensures(Contract.Result<Absy>() != null);
      return visitor.VisitAxiom(this);
    }
  }

  public abstract class NamedDeclaration : Declaration
  {
    private string name;

    // A name for a declaration that may be more verbose than, and use
    // characters not allowed by, standard Boogie identifiers. This can
    // be useful for encoding the original source names of languages
    // translated to Boogie.
    public string VerboseName =>
      QKeyValue.FindStringAttribute(Attributes, "verboseName") ?? Name;

    public string Name
    {
      get => this.name;
      set => this.name = value;
    }

    public uint GetTimeLimit(CoreOptions options)
    {
      uint tl = options.TimeLimit;
      CheckUIntAttribute("timeLimit", ref tl);
      if (tl < 0) {
        tl = options.TimeLimit;
      }
      return tl;
    }

    public uint GetResourceLimit(CoreOptions options)
    {
      uint rl = options.ResourceLimit;
      CheckUIntAttribute("rlimit", ref rl);
      if (rl < 0) {
        rl = options.ResourceLimit;
      }
      return rl;
    }

    public int? RandomSeed
    {
      get
      {
        int rs = 0;
        if (CheckIntAttribute("random_seed", ref rs))
        {
          return rs;
        }
        return null;
      }
    }

    public NamedDeclaration(IToken tok, string name)
      : base(tok)
    {
      this.name = name;
    }

    [Pure]
    public override string ToString()
    {
      return cce.NonNull(Name);
    }
    
    public virtual bool MayRename => true;
  }

  public class TypeCtorDecl : NamedDeclaration
  {
    public readonly int Arity;

    public TypeCtorDecl(IToken /*!*/ tok, string /*!*/ name, int Arity)
      : base(tok, name)
    {
      Contract.Requires(tok != null);
      Contract.Requires(name != null);
      this.Arity = Arity;
    }

    public TypeCtorDecl(IToken /*!*/ tok, string /*!*/ name, int Arity, QKeyValue kv)
      : base(tok, name)
    {
      Contract.Requires(tok != null);
      Contract.Requires(name != null);
      this.Arity = Arity;
      this.Attributes = kv;
    }

    public override void Emit(TokenTextWriter stream, int level)
    {
      //Contract.Requires(stream != null);
      stream.Write(this, level, "type ");
      EmitAttributes(stream);
      stream.Write("{0}", TokenTextWriter.SanitizeIdentifier(Name));
      for (int i = 0; i < Arity; ++i)
      {
        stream.Write(" _");
      }

      stream.WriteLine(";");
    }

    public override void Register(ResolutionContext rc)
    {
      //Contract.Requires(rc != null);
      rc.AddType(this);
    }

    public override void Resolve(ResolutionContext rc)
    {
      //Contract.Requires(rc != null);
      ResolveAttributes(rc);
    }

    public override void Typecheck(TypecheckingContext tc)
    {
      //Contract.Requires(tc != null);
      TypecheckAttributes(tc);
    }

    public override Absy StdDispatch(StandardVisitor visitor)
    {
      //Contract.Requires(visitor != null);
      Contract.Ensures(Contract.Result<Absy>() != null);
      return visitor.VisitTypeCtorDecl(this);
    }
  }

  public class DatatypeAccessor
  {
    public int ConstructorIndex;
    public int FieldIndex;

    public DatatypeAccessor(int constructorIndex, int fieldIndex)
    {
      ConstructorIndex = constructorIndex;
      FieldIndex = fieldIndex;
    }
  }
  
  public class DatatypeTypeCtorDecl : TypeCtorDecl
  {
    private List<TypeVariable> typeParameters;
    private List<DatatypeConstructor> constructors;
    private Dictionary<string, DatatypeConstructor> nameToConstructor;
    private Dictionary<string, List<DatatypeAccessor>> accessors;

    public List<DatatypeConstructor> Constructors => constructors;

    public DatatypeTypeCtorDecl(TypeCtorDecl typeCtorDecl)
      : base(typeCtorDecl.tok, typeCtorDecl.Name, typeCtorDecl.Arity, typeCtorDecl.Attributes)
    {
      this.typeParameters = new List<TypeVariable>();
      this.constructors = new List<DatatypeConstructor>();
      this.nameToConstructor = new Dictionary<string, DatatypeConstructor>();
      this.accessors = new Dictionary<string, List<DatatypeAccessor>>();
    }

    public DatatypeTypeCtorDecl(IToken tok, string name, List<TypeVariable> typeParams, QKeyValue kv)
      : base(tok, name, typeParams.Count, kv)
    {
      this.typeParameters = typeParams;
      this.constructors = new List<DatatypeConstructor>();
      this.nameToConstructor = new Dictionary<string, DatatypeConstructor>();
      this.accessors = new Dictionary<string, List<DatatypeAccessor>>();
    }

    public bool AddConstructor(DatatypeConstructor constructor)
    {
      if (this.nameToConstructor.ContainsKey(constructor.Name))
      {
        return false;
      }
      constructor.datatypeTypeCtorDecl = this;
      constructor.index = constructors.Count;
      this.constructors.Add(constructor);
      this.nameToConstructor.Add(constructor.Name, constructor);
      for (int i = 0; i < constructor.InParams.Count; i++)
      {
        var v = constructor.InParams[i];
        if (!accessors.ContainsKey(v.Name))
        {
          accessors.Add(v.Name, new List<DatatypeAccessor>());
        }
        accessors[v.Name].Add(new DatatypeAccessor(constructor.index, i));
      }
      return true;
    }

    public bool AddConstructor(IToken tok, string name, List<TypedIdent> fields)
    {
      var returnType = new CtorType(this.tok, this, new List<Type>(this.typeParameters));
      var function = new Function(tok, name, new List<TypeVariable>(this.typeParameters),
        fields.Select(field => new Formal(field.tok, field, true)).ToList<Variable>(),
        new Formal(Token.NoToken, new TypedIdent(Token.NoToken, TypedIdent.NoName, returnType), false));
      var constructor = new DatatypeConstructor(function);
      return AddConstructor(constructor);
    }

    public override void Register(ResolutionContext rc)
    {
      foreach (var constructor in constructors)
      {
        constructor.Register(rc);
      }
      base.Register(rc);
    }

    public override void Typecheck(TypecheckingContext tc)
    {
      accessors.Where(kv => kv.Value.Count > 1).Iter(kv =>
      {
        var firstAccessor = kv.Value[0];
        var firstConstructor = constructors[firstAccessor.ConstructorIndex];
        var firstSelector = firstConstructor.InParams[firstAccessor.FieldIndex];
        for (int i = 1; i < kv.Value.Count; i++)
        {
          var accessor = kv.Value[i];
          var constructor = constructors[accessor.ConstructorIndex];
          var index = accessor.FieldIndex;
          var field = constructor.InParams[index];
          if (!NormalizedFieldType(firstConstructor, constructor, index).Equals(firstSelector.TypedIdent.Type))
          {
            tc.Error(field,
              "type mismatch between field {0} and identically-named field in constructor {1}",
              field.Name, firstConstructor);
          }
        }
      });
      base.Typecheck(tc);
    }

    private static Type NormalizedFieldType(DatatypeConstructor firstConstructor, DatatypeConstructor constructor, int index)
    {
      var firstConstructorType = (CtorType)firstConstructor.OutParams[0].TypedIdent.Type;
      var constructorType = (CtorType)constructor.OutParams[0].TypedIdent.Type;
      var typeSubst = constructorType.Arguments.Zip(firstConstructorType.Arguments).ToDictionary(
        x => x.Item1 as TypeVariable,
        x => x.Item2);
      return constructor.InParams[index].TypedIdent.Type.Substitute(typeSubst);
    }

    public DatatypeConstructor GetConstructor(string constructorName)
    {
      if (!nameToConstructor.ContainsKey(constructorName))
      {
        return null;
      }
      return nameToConstructor[constructorName];
    }

    public List<DatatypeAccessor> GetAccessors(string fieldName)
    {
      if (!accessors.ContainsKey(fieldName))
      {
        return null;
      }
      return accessors[fieldName];
    }

    private void EmitCommaSeparatedListOfStrings(TokenTextWriter stream, int level, IEnumerable<string> values)
    {
      bool first = true;
      values.Iter(value =>
      {
        if (!first)
        {
          stream.WriteLine(this, level, ",");
        }
        stream.Write(this, level, value);
        first = false;
      });
    }

    public override void Emit(TokenTextWriter stream, int level)
    {
      stream.Write(this, level, "datatype ");
      stream.Write(this, level, Name);
      if (typeParameters.Count > 0)
      {
        stream.Write(this, level, "<");
        EmitCommaSeparatedListOfStrings(stream, level, typeParameters.Select(tp => tp.Name));
        stream.Write(this, level, ">");
      }
      stream.WriteLine(this, level, " {");
      bool firstConstructor = true;
      constructors.Iter(constructor =>
      {
        if (!firstConstructor)
        {
          stream.WriteLine(this, level, ",");
        }
        stream.Write(this, level + 1, constructor.Name);
        stream.Write(this, level, "(");
        constructor.InParams.Emit(stream, false);
        stream.Write(this, level, ")");
        firstConstructor = false;
      });
      stream.WriteLine();
      stream.WriteLine(this, level, "}");
    }
  }

  public class TypeSynonymDecl : NamedDeclaration
  {
    private List<TypeVariable> /*!*/
      typeParameters;

    public List<TypeVariable> TypeParameters
    {
      get
      {
        Contract.Ensures(Contract.Result<List<TypeVariable>>() != null);
        return this.typeParameters;
      }
      set
      {
        Contract.Requires(value != null);
        this.typeParameters = value;
      }
    }

    private Type /*!*/
      body;

    public Type Body
    {
      get
      {
        Contract.Ensures(Contract.Result<Type>() != null);
        return this.body;
      }
      set
      {
        Contract.Requires(value != null);
        this.body = value;
      }
    }

    [ContractInvariantMethod]
    void ObjectInvariant()
    {
      Contract.Invariant(this.body != null);
      Contract.Invariant(this.typeParameters != null);
    }

    public TypeSynonymDecl(IToken /*!*/ tok, string /*!*/ name,
      List<TypeVariable> /*!*/ typeParams, Type /*!*/ body)
      : base(tok, name)
    {
      Contract.Requires(tok != null);
      Contract.Requires(name != null);
      Contract.Requires(typeParams != null);
      Contract.Requires(body != null);
      this.typeParameters = typeParams;
      this.body = body;
    }

    public TypeSynonymDecl(IToken /*!*/ tok, string /*!*/ name,
      List<TypeVariable> /*!*/ typeParams, Type /*!*/ body, QKeyValue kv)
      : base(tok, name)
    {
      Contract.Requires(tok != null);
      Contract.Requires(name != null);
      Contract.Requires(typeParams != null);
      Contract.Requires(body != null);
      this.typeParameters = typeParams;
      this.body = body;
      this.Attributes = kv;
    }

    public override void Emit(TokenTextWriter stream, int level)
    {
      //Contract.Requires(stream != null);
      stream.Write(this, level, "type ");
      EmitAttributes(stream);
      stream.Write("{0}", TokenTextWriter.SanitizeIdentifier(Name));
      if (TypeParameters.Count > 0)
      {
        stream.Write(" ");
      }

      TypeParameters.Emit(stream, " ");
      stream.Write(" = ");
      Body.Emit(stream);
      stream.WriteLine(";");
    }

    public override void Register(ResolutionContext rc)
    {
      //Contract.Requires(rc != null);
      rc.AddType(this);
    }

    public override void Resolve(ResolutionContext rc)
    {
      //Contract.Requires(rc != null);
      ResolveAttributes(rc);

      int previousState = rc.TypeBinderState;
      try
      {
        foreach (TypeVariable /*!*/ v in TypeParameters)
        {
          Contract.Assert(v != null);
          rc.AddTypeBinder(v);
        }

        Body = Body.ResolveType(rc);
      }
      finally
      {
        rc.TypeBinderState = previousState;
      }
    }

    public override void Typecheck(TypecheckingContext tc)
    {
      //Contract.Requires(tc != null);
      TypecheckAttributes(tc);
    }

    public static void ResolveTypeSynonyms(List<TypeSynonymDecl /*!*/> /*!*/ synonymDecls, ResolutionContext /*!*/ rc)
    {
      Contract.Requires(cce.NonNullElements(synonymDecls));
      Contract.Requires(rc != null);
      // then discover all dependencies between type synonyms
      IDictionary<TypeSynonymDecl /*!*/, List<TypeSynonymDecl /*!*/> /*!*/> /*!*/
        deps =
          new Dictionary<TypeSynonymDecl /*!*/, List<TypeSynonymDecl /*!*/> /*!*/>();
      foreach (TypeSynonymDecl /*!*/ decl in synonymDecls)
      {
        Contract.Assert(decl != null);
        List<TypeSynonymDecl /*!*/> /*!*/
          declDeps = new List<TypeSynonymDecl /*!*/>();
        FindDependencies(decl.Body, declDeps, rc);
        deps.Add(decl, declDeps);
      }

      List<TypeSynonymDecl /*!*/> /*!*/
        resolved = new List<TypeSynonymDecl /*!*/>();

      int unresolved = synonymDecls.Count - resolved.Count;
      while (unresolved > 0)
      {
        foreach (TypeSynonymDecl /*!*/ decl in synonymDecls)
        {
          Contract.Assert(decl != null);
          if (!resolved.Contains(decl) &&
              deps[decl].All(d => resolved.Contains(d)))
          {
            decl.Resolve(rc);
            resolved.Add(decl);
          }
        }

        int newUnresolved = synonymDecls.Count - resolved.Count;
        if (newUnresolved < unresolved)
        {
          // we are making progress
          unresolved = newUnresolved;
        }
        else
        {
          // there have to be cycles in the definitions
          foreach (TypeSynonymDecl /*!*/ decl in synonymDecls)
          {
            Contract.Assert(decl != null);
            if (!resolved.Contains(decl))
            {
              rc.Error(decl,
                "type synonym could not be resolved because of cycles: {0}" +
                " (replacing body with \"bool\" to continue resolving)",
                decl.Name);

              // we simply replace the bodies of all remaining type
              // synonyms with "bool" so that resolution can continue
              decl.Body = Type.Bool;
              decl.Resolve(rc);
            }
          }

          unresolved = 0;
        }
      }
    }

    // determine a list of all type synonyms that occur in "type"
    private static void FindDependencies(Type /*!*/ type, List<TypeSynonymDecl /*!*/> /*!*/ deps,
      ResolutionContext /*!*/ rc)
    {
      Contract.Requires(type != null);
      Contract.Requires(cce.NonNullElements(deps));
      Contract.Requires(rc != null);
      if (type.IsVariable || type.IsBasic)
      {
        // nothing
      }
      else if (type.IsUnresolved)
      {
        UnresolvedTypeIdentifier /*!*/
          unresType = type.AsUnresolved;
        Contract.Assert(unresType != null);
        TypeSynonymDecl dep = rc.LookUpTypeSynonym(unresType.Name);
        if (dep != null)
        {
          deps.Add(dep);
        }

        foreach (Type /*!*/ subtype in unresType.Arguments)
        {
          Contract.Assert(subtype != null);
          FindDependencies(subtype, deps, rc);
        }
      }
      else if (type.IsMap)
      {
        MapType /*!*/
          mapType = type.AsMap;
        Contract.Assert(mapType != null);
        foreach (Type /*!*/ subtype in mapType.Arguments)
        {
          Contract.Assert(subtype != null);
          FindDependencies(subtype, deps, rc);
        }

        FindDependencies(mapType.Result, deps, rc);
      }
      else if (type.IsCtor)
      {
        // this can happen because we allow types to be resolved multiple times
        CtorType /*!*/
          ctorType = type.AsCtor;
        Contract.Assert(ctorType != null);
        foreach (Type /*!*/ subtype in ctorType.Arguments)
        {
          Contract.Assert(subtype != null);
          FindDependencies(subtype, deps, rc);
        }
      }
      else
      {
        System.Diagnostics.Debug.Fail("Did not expect this type during resolution: "
                                      + type);
      }
    }


    public override Absy StdDispatch(StandardVisitor visitor)
    {
      //Contract.Requires(visitor != null);
      Contract.Ensures(Contract.Result<Absy>() != null);
      return visitor.VisitTypeSynonymDecl(this);
    }
  }

  public abstract class Variable : NamedDeclaration
  {
    private TypedIdent /*!*/
      typedIdent;

    public TypedIdent TypedIdent
    {
      get
      {
        Contract.Ensures(Contract.Result<TypedIdent>() != null);
        return this.typedIdent;
      }
      set
      {
        Contract.Requires(value != null);
        this.typedIdent = value;
      }
    }

    [ContractInvariantMethod]
    void ObjectInvariant()
    {
      Contract.Invariant(this.typedIdent != null);
    }

    public Variable(IToken /*!*/ tok, TypedIdent /*!*/ typedIdent)
      : base(tok, typedIdent.Name)
    {
      Contract.Requires(tok != null);
      Contract.Requires(typedIdent != null);
      this.typedIdent = typedIdent;
    }

    public Variable(IToken /*!*/ tok, TypedIdent /*!*/ typedIdent, QKeyValue kv)
      : base(tok, typedIdent.Name)
    {
      Contract.Requires(tok != null);
      Contract.Requires(typedIdent != null);
      this.typedIdent = typedIdent;
      this.Attributes = kv;
    }

    public abstract bool IsMutable { get; }

    public override void Emit(TokenTextWriter stream, int level)
    {
      //Contract.Requires(stream != null);
      stream.Write(this, level, "var ");
      EmitVitals(stream, level, true);
      stream.WriteLine(";");
    }

    public void EmitVitals(TokenTextWriter stream, int level, bool emitAttributes, bool emitType = true)
    {
      Contract.Requires(stream != null);
      if (emitAttributes)
      {
        EmitAttributes(stream);
      }

      if (stream.Options.PrintWithUniqueASTIds && this.TypedIdent.HasName)
      {
        stream.Write("h{0}^^",
          this.GetHashCode()); // the idea is that this will prepend the name printed by TypedIdent.Emit
      }

      this.TypedIdent.Emit(stream, emitType);
    }

    public override void Resolve(ResolutionContext rc)
    {
      //Contract.Requires(rc != null);
      this.TypedIdent.Resolve(rc);
      ResolveAttributes(rc);
    }

    public void ResolveWhere(ResolutionContext rc)
    {
      Contract.Requires(rc != null);
      if (QKeyValue.FindBoolAttribute(Attributes, "assumption") && this.TypedIdent.WhereExpr != null)
      {
        rc.Error(tok, "assumption variable may not be declared with a where clause");
      }

      if (this.TypedIdent.WhereExpr != null)
      {
        this.TypedIdent.WhereExpr.Resolve(rc);
      }
    }

    public override void Typecheck(TypecheckingContext tc)
    {
      //Contract.Requires(tc != null);
      TypecheckAttributes(tc);
      this.TypedIdent.Typecheck(tc);
      if (QKeyValue.FindBoolAttribute(Attributes, "assumption") && !this.TypedIdent.Type.IsBool)
      {
        tc.Error(tok, "assumption variable must be of type 'bool'");
      }
    }
  }

  public class VariableComparer : IComparer
  {
    public int Compare(object a, object b)
    {
      Variable A = a as Variable;
      Variable B = b as Variable;
      if (A == null || B == null)
      {
        throw new ArgumentException("VariableComparer works only on objects of type Variable");
      }

      return cce.NonNull(A.Name).CompareTo(B.Name);
    }
  }

  // class to specify the <:-parents of the values of constants
  public class ConstantParent
  {
    public readonly IdentifierExpr /*!*/
      Parent;

    [ContractInvariantMethod]
    void ObjectInvariant()
    {
      Contract.Invariant(Parent != null);
    }

    // if true, the sub-dag underneath this constant-parent edge is
    // disjoint from all other unique sub-dags
    public readonly bool Unique;

    public ConstantParent(IdentifierExpr parent, bool unique)
    {
      Contract.Requires(parent != null);
      Parent = parent;
      Unique = unique;
    }
  }

  public class Constant : Variable
  {
    // when true, the value of this constant is meant to be distinct
    // from all other constants.
    public readonly bool Unique;

    public IList<Axiom> DefinitionAxioms { get; }

    public Constant(IToken /*!*/ tok, TypedIdent /*!*/ typedIdent)
      : this(tok, typedIdent, true)
    {
    }

    public Constant(IToken /*!*/ tok, TypedIdent /*!*/ typedIdent, bool unique)
      : this(tok, typedIdent, unique, null, new List<Axiom>())
    {
    }

    public Constant(IToken /*!*/ tok, TypedIdent /*!*/ typedIdent,
      bool unique,
      QKeyValue kv = null,
      IList<Axiom> definitionAxioms = null)
      : base(tok, typedIdent, kv)
    {
      Contract.Requires(tok != null);
      Contract.Requires(typedIdent != null);
      Contract.Requires(typedIdent.Name != null && typedIdent.Name.Length > 0);
      Contract.Requires(typedIdent.WhereExpr == null);
      this.Unique = unique;
      this.DefinitionAxioms = definitionAxioms ?? new List<Axiom>();
    }

    public override bool IsMutable => false;

    public override void Emit(TokenTextWriter stream, int level)
    {
      //Contract.Requires(stream != null);
      stream.Write(this, level, "const ");
      EmitAttributes(stream);
      if (this.Unique)
      {
        stream.Write(this, level, "unique ");
      }

      EmitVitals(stream, level, false);

      stream.WriteLine(";");
    }

    public override void Register(ResolutionContext rc)
    {
      //Contract.Requires(rc != null);
      rc.AddVariable(this);
    }

    public override Absy StdDispatch(StandardVisitor visitor)
    {
      //Contract.Requires(visitor != null);
      Contract.Ensures(Contract.Result<Absy>() != null);
      return visitor.VisitConstant(this);
    }
  }

  public class GlobalVariable : Variable
  {
    public GlobalVariable(IToken /*!*/ tok, TypedIdent /*!*/ typedIdent)
      : base(tok, typedIdent)
    {
      Contract.Requires(tok != null);
      Contract.Requires(typedIdent != null);
    }

    public GlobalVariable(IToken /*!*/ tok, TypedIdent /*!*/ typedIdent, QKeyValue kv)
      : base(tok, typedIdent, kv)
    {
      Contract.Requires(tok != null);
      Contract.Requires(typedIdent != null);
    }

    public override bool IsMutable
    {
      get { return true; }
    }

    public override void Register(ResolutionContext rc)
    {
      //Contract.Requires(rc != null);
      rc.AddVariable(this);
    }

    public override Absy StdDispatch(StandardVisitor visitor)
    {
      //Contract.Requires(visitor != null);
      Contract.Ensures(Contract.Result<Absy>() != null);
      return visitor.VisitGlobalVariable(this);
    }
  }

  public class Formal : Variable
  {
    public bool InComing;

    public Formal(IToken tok, TypedIdent typedIdent, bool incoming, QKeyValue kv)
      : base(tok, typedIdent, kv)
    {
      Contract.Requires(typedIdent != null);
      Contract.Requires(tok != null);
      InComing = incoming;
    }

    public Formal(IToken tok, TypedIdent typedIdent, bool incoming)
      : this(tok, typedIdent, incoming, null)
    {
      Contract.Requires(typedIdent != null);
      Contract.Requires(tok != null);
    }

    public override bool IsMutable
    {
      get { return !InComing; }
    }

    public override void Register(ResolutionContext rc)
    {
      //Contract.Requires(rc != null);
      rc.AddVariable(this);
    }

    /// <summary>
    /// Given a sequence of Formal declarations, returns sequence of Formals like the given one but without where clauses
    /// and without any attributes.
    /// The Type of each Formal is cloned.
    /// </summary>
    public static List<Variable> StripWhereClauses(List<Variable> w)
    {
      Contract.Requires(w != null);
      Contract.Ensures(Contract.Result<List<Variable>>() != null);
      List<Variable> s = new List<Variable>();
      foreach (Variable /*!*/ v in w)
      {
        Contract.Assert(v != null);
        Formal f = (Formal) v;
        TypedIdent ti = f.TypedIdent;
        s.Add(new Formal(f.tok, new TypedIdent(ti.tok, ti.Name, ti.Type.CloneUnresolved()), f.InComing, null));
      }

      return s;
    }

    public override Absy StdDispatch(StandardVisitor visitor)
    {
      //Contract.Requires(visitor != null);
      Contract.Ensures(Contract.Result<Absy>() != null);
      return visitor.VisitFormal(this);
    }
  }

  public class LocalVariable : Variable
  {
    public LocalVariable(IToken tok, TypedIdent typedIdent, QKeyValue kv)
      : base(tok, typedIdent, kv)
    {
      Contract.Requires(typedIdent != null);
      Contract.Requires(tok != null);
    }

    public LocalVariable(IToken tok, TypedIdent typedIdent)
      : base(tok, typedIdent, null)
    {
      Contract.Requires(typedIdent != null);
      Contract.Requires(tok != null);
    }

    public override bool IsMutable
    {
      get { return true; }
    }

    public override void Register(ResolutionContext rc)
    {
      //Contract.Requires(rc != null);
      rc.AddVariable(this);
    }

    public override Absy StdDispatch(StandardVisitor visitor)
    {
      //Contract.Requires(visitor != null);
      Contract.Ensures(Contract.Result<Absy>() != null);
      return visitor.VisitLocalVariable(this);
    }
  }

  public class Incarnation : LocalVariable
  {
    public int incarnationNumber;
    public readonly Variable OriginalVariable;

    public Incarnation(Variable /*!*/ var, int i) :
      base(
        var.tok,
        new TypedIdent(var.TypedIdent.tok, var.TypedIdent.Name + "@" + i, var.TypedIdent.Type)
      )
    {
      Contract.Requires(var != null);
      incarnationNumber = i;
      OriginalVariable = var;
    }
  }

  public class BoundVariable : Variable
  {
    public BoundVariable(IToken tok, TypedIdent typedIdent)
      : base(tok, typedIdent)
    {
      Contract.Requires(typedIdent != null);
      Contract.Requires(tok != null);
      Contract.Requires(typedIdent.WhereExpr == null);
    }

    public BoundVariable(IToken tok, TypedIdent typedIdent, QKeyValue kv)
      : base(tok, typedIdent, kv)
    {
      Contract.Requires(typedIdent != null);
      Contract.Requires(tok != null);
      Contract.Requires(typedIdent.WhereExpr == null);
    }

    public override bool IsMutable
    {
      get { return false; }
    }

    public override void Register(ResolutionContext rc)
    {
      //Contract.Requires(rc != null);
      rc.AddVariable(this);
    }

    public override Absy StdDispatch(StandardVisitor visitor)
    {
      //Contract.Requires(visitor != null);
      Contract.Ensures(Contract.Result<Absy>() != null);
      return visitor.VisitBoundVariable(this);
    }
  }

  public abstract class DeclWithFormals : NamedDeclaration
  {
    public List<TypeVariable> /*!*/
      TypeParameters;

    private /*readonly--except in StandardVisitor*/ List<Variable> /*!*/
      inParams, outParams;

    public List<Variable> /*!*/ InParams
    {
      get
      {
        Contract.Ensures(Contract.Result<List<Variable>>() != null);
        return this.inParams;
      }
      set
      {
        Contract.Requires(value != null);
        this.inParams = value;
      }
    }

    public List<Variable> /*!*/ OutParams
    {
      get
      {
        Contract.Ensures(Contract.Result<List<Variable>>() != null);
        return this.outParams;
      }
      set
      {
        Contract.Requires(value != null);
        this.outParams = value;
      }
    }

    [ContractInvariantMethod]
    void ObjectInvariant()
    {
      Contract.Invariant(TypeParameters != null);
      Contract.Invariant(this.inParams != null);
      Contract.Invariant(this.outParams != null);
    }

    public DeclWithFormals(IToken tok, string name, List<TypeVariable> typeParams,
      List<Variable> inParams, List<Variable> outParams)
      : base(tok, name)
    {
      Contract.Requires(inParams != null);
      Contract.Requires(outParams != null);
      Contract.Requires(typeParams != null);
      Contract.Requires(name != null);
      Contract.Requires(tok != null);
      this.TypeParameters = typeParams;
      this.inParams = inParams;
      this.outParams = outParams;
    }

    protected DeclWithFormals(DeclWithFormals that)
      : base(that.tok, cce.NonNull(that.Name))
    {
      Contract.Requires(that != null);
      this.TypeParameters = that.TypeParameters;
      this.inParams = cce.NonNull(that.InParams);
      this.outParams = cce.NonNull(that.OutParams);
    }

    public byte[] MD5Checksum_;

    public byte[] MD5Checksum
    {
      get
      {
        if (MD5Checksum_ == null)
        {
          var c = Checksum;
          if (c != null)
          {
            MD5Checksum_ = System.Security.Cryptography.MD5.Create().ComputeHash(System.Text.Encoding.UTF8.GetBytes(c));
          }
        }

        return MD5Checksum_;
      }
    }

    public byte[] MD5DependencyChecksum_;

    public byte[] MD5DependencyChecksum
    {
      get
      {
        Contract.Requires(DependenciesCollected);

        if (MD5DependencyChecksum_ == null && MD5Checksum != null)
        {
          var c = MD5Checksum;
          var transFuncDeps = new HashSet<Function>();
          if (procedureDependencies != null)
          {
            foreach (var p in procedureDependencies)
            {
              if (p.FunctionDependencies != null)
              {
                foreach (var f in p.FunctionDependencies)
                {
                  transFuncDeps.Add(f);
                }
              }

              var pc = p.MD5Checksum;
              if (pc == null)
              {
                return null;
              }

              c = ChecksumHelper.CombineChecksums(c, pc, true);
            }
          }

          if (FunctionDependencies != null)
          {
            foreach (var f in FunctionDependencies)
            {
              transFuncDeps.Add(f);
            }
          }

          var q = new Queue<Function>(transFuncDeps);
          while (q.Any())
          {
            var f = q.Dequeue();
            var fc = f.MD5Checksum;
            if (fc == null)
            {
              return null;
            }

            c = ChecksumHelper.CombineChecksums(c, fc, true);
            if (f.FunctionDependencies != null)
            {
              foreach (var d in f.FunctionDependencies)
              {
                if (!transFuncDeps.Contains(d))
                {
                  transFuncDeps.Add(d);
                  q.Enqueue(d);
                }
              }
            }
          }

          MD5DependencyChecksum_ = c;
        }

        return MD5DependencyChecksum_;
      }
    }

    public string Checksum
    {
      get { return FindStringAttribute("checksum"); }
    }

    string dependencyChecksum;

    public string DependencyChecksum
    {
      get
      {
        if (dependencyChecksum == null && DependenciesCollected && MD5DependencyChecksum != null)
        {
          dependencyChecksum = BitConverter.ToString(MD5DependencyChecksum);
        }

        return dependencyChecksum;
      }
    }

    public bool DependenciesCollected { get; set; }

    ISet<Procedure> procedureDependencies;

    public ISet<Procedure> ProcedureDependencies
    {
      get { return procedureDependencies; }
    }

    public void AddProcedureDependency(Procedure procedure)
    {
      Contract.Requires(procedure != null);

      if (procedureDependencies == null)
      {
        procedureDependencies = new HashSet<Procedure>();
      }

      procedureDependencies.Add(procedure);
    }

    ISet<Function> functionDependencies;

    public ISet<Function> FunctionDependencies
    {
      get { return functionDependencies; }
    }

    public void AddFunctionDependency(Function function)
    {
      Contract.Requires(function != null);

      if (functionDependencies == null)
      {
        functionDependencies = new HashSet<Function>();
      }

      functionDependencies.Add(function);
    }

    public bool SignatureEquals(CoreOptions options, DeclWithFormals other)
    {
      Contract.Requires(other != null);

      string sig = null;
      string otherSig = null;
      using (var strWr = new System.IO.StringWriter())
      using (var tokTxtWr = new TokenTextWriter("<no file>", strWr, false, false, options))
      {
        EmitSignature(tokTxtWr, this is Function);
        sig = strWr.ToString();
      }

      using (var otherStrWr = new System.IO.StringWriter())
      using (var otherTokTxtWr = new TokenTextWriter("<no file>", otherStrWr, false, false, options))
      {
        EmitSignature(otherTokTxtWr, other is Function);
        otherSig = otherStrWr.ToString();
      }

      return sig == otherSig;
    }

    protected void EmitSignature(TokenTextWriter stream, bool shortRet)
    {
      Contract.Requires(stream != null);
      Type.EmitOptionalTypeParams(stream, TypeParameters);
      stream.Write("(");
      stream.push();
      InParams.Emit(stream, true);
      stream.Write(")");
      stream.sep();

      if (shortRet)
      {
        Contract.Assert(OutParams.Count == 1);
        stream.Write(" : ");
        cce.NonNull(OutParams[0]).TypedIdent.Type.Emit(stream);
      }
      else if (OutParams.Count > 0)
      {
        stream.Write(" returns (");
        OutParams.Emit(stream, true);
        stream.Write(")");
      }

      stream.pop();
    }

    // Register all type parameters at the resolution context
    protected void RegisterTypeParameters(ResolutionContext rc)
    {
      Contract.Requires(rc != null);
      foreach (TypeVariable /*!*/ v in TypeParameters)
      {
        Contract.Assert(v != null);
        rc.AddTypeBinder(v);
      }
    }

    protected void SortTypeParams()
    {
      List<Type> /*!*/
        allTypes = InParams.Select(Item => Item.TypedIdent.Type).ToList();
      Contract.Assert(allTypes != null);
      allTypes.AddRange(OutParams.Select(Item => Item.TypedIdent.Type));
      TypeParameters = Type.SortTypeParams(TypeParameters, allTypes, null);
    }

    /// <summary>
    /// Adds the given formals to the current variable context, and then resolves
    /// the types of those formals.  Does NOT resolve the where clauses of the
    /// formals.
    /// Relies on the caller to first create, and later tear down, that variable
    /// context.
    /// </summary>
    /// <param name="rc"></param>
    protected void RegisterFormals(List<Variable> formals, ResolutionContext rc)
    {
      Contract.Requires(rc != null);
      Contract.Requires(formals != null);
      foreach (Formal /*!*/ f in formals)
      {
        Contract.Assert(f != null);
        if (f.Name != TypedIdent.NoName)
        {
          rc.AddVariable(f);
        }

        f.Resolve(rc);
      }
    }

    /// <summary>
    /// Resolves the where clauses (and attributes) of the formals.
    /// </summary>
    /// <param name="rc"></param>
    protected void ResolveFormals(List<Variable> formals, ResolutionContext rc)
    {
      Contract.Requires(rc != null);
      Contract.Requires(formals != null);
      foreach (Formal /*!*/ f in formals)
      {
        Contract.Assert(f != null);
        f.ResolveWhere(rc);
      }
    }

    public override void Typecheck(TypecheckingContext tc)
    {
      //Contract.Requires(tc != null);
      TypecheckAttributes(tc);
      foreach (Formal /*!*/ p in InParams)
      {
        Contract.Assert(p != null);
        p.Typecheck(tc);
      }

      foreach (Formal /*!*/ p in OutParams)
      {
        Contract.Assert(p != null);
        p.Typecheck(tc);
      }
    }
  }

  public class DatatypeConstructor : Function
  {
    public DatatypeTypeCtorDecl datatypeTypeCtorDecl;
    public int index;

    public DatatypeConstructor(Function func)
      : base(func.tok, func.Name, func.TypeParameters, func.InParams, func.OutParams[0], func.Comment, func.Attributes)
    {
    }

    public override bool MayRename => false;
  }

  public class Function : DeclWithFormals
  {
    public string Comment;

    public Expr Body; // Only set if the function is declared with {:inline}
    public NAryExpr DefinitionBody; // Only set if the function is declared with {:define}
    public Axiom DefinitionAxiom;

    public IList<Axiom> otherDefinitionAxioms = new List<Axiom>();
    public IEnumerable<Axiom> DefinitionAxioms => 
      (DefinitionAxiom == null ? Enumerable.Empty<Axiom>() : new[]{ DefinitionAxiom }).Concat(otherDefinitionAxioms);

    public IEnumerable<Axiom> OtherDefinitionAxioms => otherDefinitionAxioms;

    public void AddOtherDefinitionAxiom(Axiom axiom)
    {
      Contract.Requires(axiom != null);

      otherDefinitionAxioms.Add(axiom);
    }

    private bool neverTrigger;
    private bool neverTriggerComputed;

    public string OriginalLambdaExprAsString;

    public Function(IToken tok, string name, List<Variable> args, Variable result)
      : this(tok, name, new List<TypeVariable>(), args, result, null)
    {
      Contract.Requires(result != null);
      Contract.Requires(args != null);
      Contract.Requires(name != null);
      Contract.Requires(tok != null);
      //:this(tok, name, new List<TypeVariable>(), args, result, null);
    }

    public Function(IToken tok, string name, List<TypeVariable> typeParams, List<Variable> args, Variable result)
      : this(tok, name, typeParams, args, result, null)
    {
      Contract.Requires(result != null);
      Contract.Requires(args != null);
      Contract.Requires(typeParams != null);
      Contract.Requires(name != null);
      Contract.Requires(tok != null);
      //:this(tok, name, typeParams, args, result, null);
    }

    public Function(IToken tok, string name, List<Variable> args, Variable result, string comment)
      : this(tok, name, new List<TypeVariable>(), args, result, comment)
    {
      Contract.Requires(result != null);
      Contract.Requires(args != null);
      Contract.Requires(name != null);
      Contract.Requires(tok != null);
      //:this(tok, name, new List<TypeVariable>(), args, result, comment);
    }

    public Function(IToken tok, string name, List<TypeVariable> typeParams, List<Variable> args, Variable /*!*/ result,
      string comment)
      : base(tok, name, typeParams, args, new List<Variable> {result})
    {
      Contract.Requires(result != null);
      Contract.Requires(args != null);
      Contract.Requires(typeParams != null);
      Contract.Requires(name != null);
      Contract.Requires(tok != null);
      Comment = comment;
    }

    public Function(IToken tok, string name, List<TypeVariable> typeParams, List<Variable> args, Variable result,
      string comment, QKeyValue kv)
      : this(tok, name, typeParams, args, result, comment)
    {
      Contract.Requires(args != null);
      Contract.Requires(result != null);
      Contract.Requires(typeParams != null);
      Contract.Requires(name != null);
      Contract.Requires(tok != null);
      //:this(tok, name, typeParams, args, result, comment);
      this.Attributes = kv;
    }

    public override void Emit(TokenTextWriter stream, int level)
    {
      //Contract.Requires(stream != null);
      if (Comment != null)
      {
        stream.WriteLine(this, level, "// " + Comment);
      }

      stream.Write(this, level, "function ");
      EmitAttributes(stream);
      if (Body != null && !QKeyValue.FindBoolAttribute(Attributes, "inline"))
      {
        Contract.Assert(DefinitionBody == null);
        // Boogie inlines any function whose .Body field is non-null.  The parser populates the .Body field
        // if the :inline attribute is present, but if someone creates the Boogie file directly as an AST, then
        // the :inline attribute may not be there.  We'll make sure it's printed, so one can see that this means
        // that the body will be inlined.
        stream.Write("{:inline} ");
      }

      if (DefinitionBody != null && !QKeyValue.FindBoolAttribute(Attributes, "define"))
      {
        // Boogie defines any function whose .DefinitionBody field is non-null.  The parser populates the .DefinitionBody field
        // if the :define attribute is present, but if someone creates the Boogie file directly as an AST, then
        // the :define attribute may not be there.  We'll make sure it's printed, so one can see that this means
        // that the function will be defined.
        stream.Write("{:define} ");
      }

      if (stream.Options.PrintWithUniqueASTIds)
      {
        stream.Write("h{0}^^{1}", this.GetHashCode(), TokenTextWriter.SanitizeIdentifier(this.Name));
      }
      else
      {
        stream.Write("{0}", TokenTextWriter.SanitizeIdentifier(this.Name));
      }

      EmitSignature(stream, true);
      if (Body != null)
      {
        Contract.Assert(DefinitionBody == null);
        stream.WriteLine();
        stream.WriteLine("{");
        stream.Write(level + 1, "");
        Body.Emit(stream);
        stream.WriteLine();
        stream.WriteLine("}");
      }
      else if (DefinitionBody != null)
      {
        stream.WriteLine();
        stream.WriteLine("{");
        stream.Write(level + 1, "");
        DefinitionBody.Args[1].Emit(stream);
        stream.WriteLine();
        stream.WriteLine("}");
      }
      else
      {
        stream.WriteLine(";");
      }
    }

    public override void Register(ResolutionContext rc)
    {
      //Contract.Requires(rc != null);
      rc.AddFunction(this);
    }

    public override void Resolve(ResolutionContext rc)
    {
      //Contract.Requires(rc != null);
      int previousTypeBinderState = rc.TypeBinderState;
      try
      {
        RegisterTypeParameters(rc);
        rc.PushVarContext();
        RegisterFormals(InParams, rc);
        RegisterFormals(OutParams, rc);
        ResolveAttributes(rc);
        if (Body != null)
        {
          Contract.Assert(DefinitionBody == null);
          rc.StateMode = ResolutionContext.State.StateLess;
          Body.Resolve(rc);
          rc.StateMode = ResolutionContext.State.Single;
        }
        else if (DefinitionBody != null)
        {
          rc.StateMode = ResolutionContext.State.StateLess;
          DefinitionBody.Resolve(rc);
          rc.StateMode = ResolutionContext.State.Single;
        }

        rc.PopVarContext();
        Type.CheckBoundVariableOccurrences(TypeParameters,
          InParams.Select(Item => Item.TypedIdent.Type).ToList(),
          OutParams.Select(Item => Item.TypedIdent.Type).ToList(),
          this.tok, "function arguments",
          rc);
      }
      finally
      {
        rc.TypeBinderState = previousTypeBinderState;
      }

      SortTypeParams();
    }

    public override void Typecheck(TypecheckingContext tc)
    {
      //Contract.Requires(tc != null);
      // PR: why was the base call left out previously?
      base.Typecheck(tc);
      // TypecheckAttributes(tc);
      if (Body != null)
      {
        Contract.Assert(DefinitionBody == null);
        Body.Typecheck(tc);
        if (!cce.NonNull(Body.Type).Unify(cce.NonNull(OutParams[0]).TypedIdent.Type))
        {
          tc.Error(Body,
            "function body with invalid type: {0} (expected: {1})",
            Body.Type, cce.NonNull(OutParams[0]).TypedIdent.Type);
        }
      }
      else if (DefinitionBody != null)
      {
        DefinitionBody.Typecheck(tc);

        // We are matching the type of the function body with output param, and not the type
        // of DefinitionBody, which is always going to be bool (since it is of the form func_call == func_body)
        if (!cce.NonNull(DefinitionBody.Args[1].Type).Unify(cce.NonNull(OutParams[0]).TypedIdent.Type))
        {
          tc.Error(DefinitionBody.Args[1],
            "function body with invalid type: {0} (expected: {1})",
            DefinitionBody.Args[1].Type, cce.NonNull(OutParams[0]).TypedIdent.Type);
        }
      }
    }

    public bool NeverTrigger
    {
      get
      {
        if (!neverTriggerComputed)
        {
          this.CheckBooleanAttribute("never_pattern", ref neverTrigger);
          neverTriggerComputed = true;
        }

        return neverTrigger;
      }
    }

    public override Absy StdDispatch(StandardVisitor visitor)
    {
      //Contract.Requires(visitor != null);
      Contract.Ensures(Contract.Result<Absy>() != null);
      return visitor.VisitFunction(this);
    }

    public Axiom CreateDefinitionAxiom(Expr definition, QKeyValue kv = null)
    {
      Contract.Requires(definition != null);

      List<Variable> dummies = new List<Variable>();
      List<Expr> callArgs = new List<Expr>();
      int i = 0;
      foreach (Formal /*!*/ f in InParams)
      {
        Contract.Assert(f != null);
        string nm = f.TypedIdent.HasName ? f.TypedIdent.Name : "_" + i;
        dummies.Add(new BoundVariable(f.tok, new TypedIdent(f.tok, nm, f.TypedIdent.Type)));
        callArgs.Add(new IdentifierExpr(f.tok, nm));
        i++;
      }

      List<TypeVariable> /*!*/
        quantifiedTypeVars = new List<TypeVariable>();
      foreach (TypeVariable /*!*/ t in TypeParameters)
      {
        Contract.Assert(t != null);
        quantifiedTypeVars.Add(new TypeVariable(tok, t.Name));
      }

      Expr call = new NAryExpr(tok, new FunctionCall(new IdentifierExpr(tok, Name)), callArgs);
      // specify the type of the function, because it might be that
      // type parameters only occur in the output type
      call = Expr.CoerceType(tok, call, (Type) OutParams[0].TypedIdent.Type.Clone());
      Expr def = Expr.Binary(tok, BinaryOperator.Opcode.Eq, call, definition);
      if (quantifiedTypeVars.Count != 0 || dummies.Count != 0)
      {
        def = new ForallExpr(tok, quantifiedTypeVars, dummies,
          kv,
          new Trigger(tok, true, new List<Expr> {call}, null),
          def);
      }

      DefinitionAxiom = new Axiom(tok, def);
      return DefinitionAxiom;
    }

    // Generates function definition of the form func_call == func_body
    // For example, for
    // function {:define} foo(x:int) returns(int) { x + 1 }
    // this will generate
    // foo(x):int == x + 1
    // We need the left hand call part later on to be able to generate
    // the appropriate SMTlib style function definition. Hence, it is
    // important that it goes through the resolution and type checking passes,
    // since otherwise it is hard to connect function parameters to the resolved
    // variables in the function body.
    public NAryExpr CreateFunctionDefinition(Expr body)
    {
      Contract.Requires(body != null);

      List<Expr> callArgs = new List<Expr>();
      int i = 0;
      foreach (Formal /*!*/ f in InParams)
      {
        Contract.Assert(f != null);
        string nm = f.TypedIdent.HasName ? f.TypedIdent.Name : "_" + i;
        callArgs.Add(new IdentifierExpr(f.tok, nm));
        i++;
      }

      Expr call = new NAryExpr(tok, new FunctionCall(new IdentifierExpr(tok, Name)), callArgs);
      // specify the type of the function, because it might be that
      // type parameters only occur in the output type
      call = Expr.CoerceType(tok, call, (Type) OutParams[0].TypedIdent.Type.Clone());
      NAryExpr def = Expr.Binary(tok, BinaryOperator.Opcode.Eq, call, body);
      return def;
    }
  }

  public class Macro : Function
  {
    public Macro(IToken tok, string name, List<Variable> args, Variable result)
      : base(tok, name, args, result)
    {
    }
  }

  public class Requires : Absy, ICarriesAttributes
  {
    public readonly bool Free;

    private Expr /*!*/
      _condition;

    public ProofObligationDescription Description { get; set; } = new RequiresDescription();

    public Expr /*!*/ Condition
    {
      get
      {
        Contract.Ensures(Contract.Result<Expr>() != null);
        return this._condition;
      }
      set
      {
        Contract.Requires(value != null);
        this._condition = value;
      }
    }

    public string Comment;

    [ContractInvariantMethod]
    void ObjectInvariant()
    {
      Contract.Invariant(this._condition != null);
    }

    private MiningStrategy errorDataEnhanced;

    public MiningStrategy ErrorDataEnhanced
    {
      get { return errorDataEnhanced; }
      set { errorDataEnhanced = value; }
    }

    public QKeyValue Attributes { get; set; }

    public String ErrorMessage
    {
      get { return QKeyValue.FindStringAttribute(Attributes, "msg"); }
    }

    public bool CanAlwaysAssume()
    {
      return Free && QKeyValue.FindBoolAttribute(Attributes, "always_assume");
    }


    public Requires(IToken token, bool free, Expr condition, string comment, QKeyValue kv)
      : base(token)
    {
      Contract.Requires(condition != null);
      Contract.Requires(token != null);
      this.Free = free;
      this._condition = condition;
      this.Comment = comment;
      this.Attributes = kv;
    }

    public Requires(IToken token, bool free, Expr condition, string comment)
      : this(token, free, condition, comment, null)
    {
      Contract.Requires(condition != null);
      Contract.Requires(token != null);
      //:this(token, free, condition, comment, null);
    }

    public Requires(bool free, Expr condition)
      : this(Token.NoToken, free, condition, null)
    {
      Contract.Requires(condition != null);
      //:this(Token.NoToken, free, condition, null);
    }

    public Requires(bool free, Expr condition, string comment)
      : this(Token.NoToken, free, condition, comment)
    {
      Contract.Requires(condition != null);
      //:this(Token.NoToken, free, condition, comment);
    }

    public void Emit(TokenTextWriter stream, int level)
    {
      Contract.Requires(stream != null);
      if (Comment != null)
      {
        stream.WriteLine(this, level, "// " + Comment);
      }

      stream.Write(this, level, "{0}requires ", Free ? "free " : "");
      Cmd.EmitAttributes(stream, Attributes);
      this.Condition.Emit(stream);
      stream.WriteLine(";");
    }

    public override void Resolve(ResolutionContext rc)
    {
      //Contract.Requires(rc != null);
      this.Condition.Resolve(rc);
    }

    public override void Typecheck(TypecheckingContext tc)
    {
      //Contract.Requires(tc != null);
      this.Condition.Typecheck(tc);
      Contract.Assert(this.Condition.Type != null); // follows from postcondition of Expr.Typecheck
      if (!this.Condition.Type.Unify(Type.Bool))
      {
        tc.Error(this, "preconditions must be of type bool");
      }
    }

    public override Absy StdDispatch(StandardVisitor visitor)
    {
      return visitor.VisitRequires(this);
    }
  }

  public class Ensures : Absy, ICarriesAttributes
  {
    public readonly bool Free;

    public ProofObligationDescription Description { get; set; } = new EnsuresDescription();

    private Expr /*!*/
      _condition;

    public Expr /*!*/ Condition
    {
      get
      {
        Contract.Ensures(Contract.Result<Expr>() != null);
        return this._condition;
      }
      set
      {
        Contract.Requires(value != null);
        this._condition = value;
      }
    }

    [ContractInvariantMethod]
    void ObjectInvariant()
    {
      Contract.Invariant(this._condition != null);
    }

    public string Comment;

    private MiningStrategy errorDataEnhanced;

    public MiningStrategy ErrorDataEnhanced
    {
      get { return errorDataEnhanced; }
      set { errorDataEnhanced = value; }
    }

    public String ErrorMessage
    {
      get { return QKeyValue.FindStringAttribute(Attributes, "msg"); }
    }

    public QKeyValue Attributes { get; set; }

    public bool CanAlwaysAssume ()
    {
      return Free && QKeyValue.FindBoolAttribute(this.Attributes, "always_assume");
    }

    public Ensures(IToken token, bool free, Expr /*!*/ condition, string comment, QKeyValue kv)
      : base(token)
    {
      Contract.Requires(condition != null);
      Contract.Requires(token != null);
      this.Free = free;
      this._condition = condition;
      this.Comment = comment;
      this.Attributes = kv;
    }

    public Ensures(IToken token, bool free, Expr condition, string comment)
      : this(token, free, condition, comment, null)
    {
      Contract.Requires(condition != null);
      Contract.Requires(token != null);
      //:this(token, free, condition, comment, null);
    }

    public Ensures(bool free, Expr condition)
      : this(Token.NoToken, free, condition, null)
    {
      Contract.Requires(condition != null);
      //:this(Token.NoToken, free, condition, null);
    }

    public Ensures(bool free, Expr condition, string comment)
      : this(Token.NoToken, free, condition, comment)
    {
      Contract.Requires(condition != null);
      //:this(Token.NoToken, free, condition, comment);
    }

    public void Emit(TokenTextWriter stream, int level)
    {
      Contract.Requires(stream != null);
      if (Comment != null)
      {
        stream.WriteLine(this, level, "// " + Comment);
      }

      stream.Write(this, level, "{0}ensures ", Free ? "free " : "");
      Cmd.EmitAttributes(stream, Attributes);
      this.Condition.Emit(stream);
      stream.WriteLine(";");
    }

    public override void Resolve(ResolutionContext rc)
    {
      //Contract.Requires(rc != null);
      this.Condition.Resolve(rc);
    }

    public override void Typecheck(TypecheckingContext tc)
    {
      this.Condition.Typecheck(tc);
      Contract.Assert(this.Condition.Type != null); // follows from postcondition of Expr.Typecheck
      if (!this.Condition.Type.Unify(Type.Bool))
      {
        tc.Error(this, "postconditions must be of type bool");
      }
    }

    public override Absy StdDispatch(StandardVisitor visitor)
    {
      return visitor.VisitEnsures(this);
    }
  }

  public class Procedure : DeclWithFormals
  {
    public List<Requires> /*!*/
      Requires;

    public List<IdentifierExpr> /*!*/
      Modifies;

    public List<Ensures> /*!*/
      Ensures;

    [ContractInvariantMethod]
    void ObjectInvariant()
    {
      Contract.Invariant(Requires != null);
      Contract.Invariant(Modifies != null);
      Contract.Invariant(Ensures != null);
    }

    public Procedure(IToken /*!*/ tok, string /*!*/ name, List<TypeVariable> /*!*/ typeParams,
      List<Variable> /*!*/ inParams, List<Variable> /*!*/ outParams,
      List<Requires> /*!*/ requires, List<IdentifierExpr> /*!*/ modifies, List<Ensures> /*!*/ ensures)
      : this(tok, name, typeParams, inParams, outParams, requires, modifies, ensures, null)
    {
      Contract.Requires(tok != null);
      Contract.Requires(name != null);
      Contract.Requires(typeParams != null);
      Contract.Requires(inParams != null);
      Contract.Requires(outParams != null);
      Contract.Requires(requires != null);
      Contract.Requires(modifies != null);
      Contract.Requires(ensures != null);
      //:this(tok, name, typeParams, inParams, outParams, requires, modifies, ensures, null);
    }

    public Procedure(IToken /*!*/ tok, string /*!*/ name, List<TypeVariable> /*!*/ typeParams,
      List<Variable> /*!*/ inParams, List<Variable> /*!*/ outParams,
      List<Requires> /*!*/ @requires, List<IdentifierExpr> /*!*/ @modifies, List<Ensures> /*!*/ @ensures, QKeyValue kv
    )
      : base(tok, name, typeParams, inParams, outParams)
    {
      Contract.Requires(tok != null);
      Contract.Requires(name != null);
      Contract.Requires(typeParams != null);
      Contract.Requires(inParams != null);
      Contract.Requires(outParams != null);
      Contract.Requires(@requires != null);
      Contract.Requires(@modifies != null);
      Contract.Requires(@ensures != null);
      this.Requires = @requires;
      this.Modifies = @modifies;
      this.Ensures = @ensures;
      this.Attributes = kv;
    }

    public override void Emit(TokenTextWriter stream, int level)
    {
      //Contract.Requires(stream != null);
      stream.Write(this, level, "procedure ");
      EmitAttributes(stream);
      stream.Write(this, level, "{0}", TokenTextWriter.SanitizeIdentifier(this.Name));
      EmitSignature(stream, false);
      stream.WriteLine(";");

      level++;

      foreach (Requires /*!*/ e in this.Requires)
      {
        Contract.Assert(e != null);
        e.Emit(stream, level);
      }

      if (this.Modifies.Count > 0)
      {
        stream.Write(level, "modifies ");
        this.Modifies.Emit(stream, false);
        stream.WriteLine(";");
      }

      foreach (Ensures /*!*/ e in this.Ensures)
      {
        Contract.Assert(e != null);
        e.Emit(stream, level);
      }

      stream.WriteLine();
      stream.WriteLine();
    }

    public override void Register(ResolutionContext rc)
    {
      //Contract.Requires(rc != null);
      rc.AddProcedure(this);
    }

    public override void Resolve(ResolutionContext rc)
    {
      //Contract.Requires(rc != null);
      rc.PushVarContext();

      foreach (IdentifierExpr /*!*/ ide in Modifies)
      {
        Contract.Assert(ide != null);
        ide.Resolve(rc);
      }

      int previousTypeBinderState = rc.TypeBinderState;
      try
      {
        RegisterTypeParameters(rc);

        RegisterFormals(InParams, rc);
        ResolveFormals(InParams,
          rc); // "where" clauses of in-parameters are resolved without the out-parameters in scope
        foreach (Requires /*!*/ e in Requires)
        {
          Contract.Assert(e != null);
          e.Resolve(rc);
        }

        RegisterFormals(OutParams, rc);
        ResolveFormals(OutParams,
          rc); // "where" clauses of out-parameters are resolved with both in- and out-parameters in scope

        rc.StateMode = ResolutionContext.State.Two;
        foreach (Ensures /*!*/ e in Ensures)
        {
          Contract.Assert(e != null);
          e.Resolve(rc);
        }

        rc.StateMode = ResolutionContext.State.Single;
        ResolveAttributes(rc);

        Type.CheckBoundVariableOccurrences(TypeParameters,
          InParams.Select(Item => Item.TypedIdent.Type).ToList(),
          OutParams.Select(Item => Item.TypedIdent.Type).ToList(),
          this.tok, "procedure arguments",
          rc);
      }
      finally
      {
        rc.TypeBinderState = previousTypeBinderState;
      }

      rc.PopVarContext();

      SortTypeParams();
    }

    public override void Typecheck(TypecheckingContext tc)
    {
      //Contract.Requires(tc != null);
      base.Typecheck(tc);
      foreach (IdentifierExpr /*!*/ ide in Modifies)
      {
        Contract.Assert(ide != null);
        Contract.Assume(ide.Decl != null);
        if (!ide.Decl.IsMutable)
        {
          tc.Error(this, "modifies list contains constant: {0}", ide.Name);
        }

        ide.Typecheck(tc);
      }

      foreach (Requires /*!*/ e in Requires)
      {
        Contract.Assert(e != null);
        e.Typecheck(tc);
      }

      bool oldYields = tc.Yields;
      tc.Yields = QKeyValue.FindBoolAttribute(Attributes, CivlAttributes.YIELDS);
      foreach (Ensures /*!*/ e in Ensures)
      {
        Contract.Assert(e != null);
        e.Typecheck(tc);
      }

      tc.Yields = oldYields;
    }

    public override Absy StdDispatch(StandardVisitor visitor)
    {
      //Contract.Requires(visitor != null);
      Contract.Ensures(Contract.Result<Absy>() != null);
      return visitor.VisitProcedure(this);
    }
  }

  public class YieldInvariantDecl : Procedure
  {
    private int layerNum;

    public int LayerNum
    {
      get { return layerNum; }
      set { layerNum = value; }
    }

    public YieldInvariantDecl(IToken tok, string name, List<Variable> inParams, List<Requires> requires, QKeyValue kv) :
      base(tok, name, new List<TypeVariable>(), inParams, new List<Variable>(), requires, new List<IdentifierExpr>(),
        requires.Select(x => new Ensures(x.tok, false, x.Condition, null)).ToList(), kv)
    {
    }
  }

  public enum ActionQualifier
  {
      Async,
      Invariant,
      Link,
      Proxy,
      None
  }

  public class ActionDeclRef : Absy
  {
    public string actionName;
    public ActionDecl actionDecl;

    public ActionDeclRef(IToken tok, string name) : base(tok)
    {
      actionName = name;
    }

    public override void Resolve(ResolutionContext rc)
    {
      if (actionDecl != null)
      {
        return;
      }
      actionDecl = rc.LookUpProcedure(actionName) as ActionDecl;
      if (actionDecl == null)
      {
        rc.Error(this, "undeclared action: {0}", actionName);
      }
    }

    public override void Typecheck(TypecheckingContext tc)
    {
      throw new NotImplementedException();
    }
  }

  public enum MoverType
  {
    Non,
    Right,
    Left,
    Both
  }

  public class ActionDecl : Procedure
  {
    private MoverType moverType;
    private ActionQualifier actionQualifier;
    private List<ActionDeclRef> creates;
    private ActionDeclRef refinedAction;

    public ActionDecl(IToken tok, string name, MoverType moverType, ActionQualifier actionQualifier,
      List<Variable> inParams, List<Variable> outParams, List<ActionDeclRef> creates, ActionDeclRef refinedAction,
      List<IdentifierExpr> modifies, QKeyValue kv) : base(tok, name, new List<TypeVariable>(), inParams, outParams,
      new List<Requires>(), modifies, new List<Ensures>(), kv)
    {
      this.moverType = moverType;
      this.actionQualifier = actionQualifier;
      this.creates = creates;
      this.refinedAction = refinedAction;
    }

    public override void Resolve(ResolutionContext rc)
    {
      base.Resolve(rc);
      if (moverType != MoverType.Non)
      {
        if (actionQualifier == ActionQualifier.Invariant)
        {
          rc.Error(this, "Mover may not be a invariant action");
        }
        if (actionQualifier == ActionQualifier.Link)
        {
          rc.Error(this, "Mover may not be a link action");
        }
        if (actionQualifier == ActionQualifier.Proxy)
        {
          rc.Error(this, "Mover may not be a proxy action");
        }
      }
      if (creates.Any())
      {
        if (actionQualifier == ActionQualifier.Link)
        {
          rc.Error(this, "Link action may not create pending asyncs");
        }
        if (moverType == MoverType.Right || moverType == MoverType.Both)
        {
          rc.Error(this, "Right mover may not create pending asyncs");
        }
      }
      creates.Iter(create => create.Resolve(rc));
    }
  }

  public class YieldProcedureDecl : Procedure
  {
    private List<CallCmd> yieldRequires;
    private List<CallCmd> yieldEnsures;
    private ActionDeclRef refinedAction;

    public YieldProcedureDecl(IToken tok, string name, List<Variable> inParams, List<Variable> outParams,
      List<Requires> requires, List<CallCmd> yieldRequires, List<Ensures> ensures, List<CallCmd> yieldEnsures,
      ActionDeclRef refinedAction, QKeyValue kv) : base(tok, name, new List<TypeVariable>(), inParams, outParams,
      requires, null, ensures, kv)
    {
      this.yieldRequires = yieldRequires;
      this.yieldEnsures = yieldEnsures;
      this.refinedAction = refinedAction;
    }

    public override void Resolve(ResolutionContext rc)
    {
      base.Resolve(rc);
      yieldRequires.Iter(callCmd => callCmd.Resolve(rc));
      yieldEnsures.Iter(callCmd => callCmd.Resolve(rc));
      refinedAction.Resolve(rc);
    }

    public override void Typecheck(TypecheckingContext tc)
    {
      base.Typecheck(tc);
      yieldRequires.Iter(callCmd => callCmd.Typecheck(tc));
      yieldEnsures.Iter(callCmd => callCmd.Typecheck(tc));
    }
  }

  public class LoopProcedure : Procedure
  {
    public Implementation enclosingImpl;
    private Dictionary<Block, Block> blockMap;
    private Dictionary<string, Block> blockLabelMap;

    public LoopProcedure(Implementation impl, Block header,
      List<Variable> inputs, List<Variable> outputs, List<IdentifierExpr> globalMods)
      : base(Token.NoToken, impl.Name + "_loop_" + header.ToString(),
        new List<TypeVariable>(), inputs, outputs,
        new List<Requires>(), globalMods, new List<Ensures>())
    {
      enclosingImpl = impl;
    }

    public void setBlockMap(Dictionary<Block, Block> bm)
    {
      blockMap = bm;
      blockLabelMap = new Dictionary<string, Block>();
      foreach (var kvp in bm)
      {
        blockLabelMap.Add(kvp.Key.Label, kvp.Value);
      }
    }

    public Block getBlock(string label)
    {
      if (blockLabelMap.ContainsKey(label))
      {
        return blockLabelMap[label];
      }

      return null;
    }
  }

  public class Implementation : DeclWithFormals {

    public List<Variable> /*!*/
      LocVars;

    [Rep] public StmtList StructuredStmts;
    [Rep] public List<Block /*!*/> /*!*/ Blocks;
    public Procedure Proc;

    // Blocks before applying passification etc.
    // Both are used only when /inline is set.
    public List<Block /*!*/> OriginalBlocks;
    public List<Variable> OriginalLocVars;

    public readonly ISet<byte[]> AssertionChecksums = new HashSet<byte[]>(ChecksumComparer.Default);

    public sealed class ChecksumComparer : IEqualityComparer<byte[]>
    {
      static IEqualityComparer<byte[]> defaultComparer;

      public static IEqualityComparer<byte[]> Default
      {
        get
        {
          if (defaultComparer == null)
          {
            defaultComparer = new ChecksumComparer();
          }

          return defaultComparer;
        }
      }

      public bool Equals(byte[] x, byte[] y)
      {
        if (x == null || y == null)
        {
          return x == y;
        }
        else
        {
          return x.SequenceEqual(y);
        }
      }

      public int GetHashCode(byte[] checksum)
      {
        if (checksum == null)
        {
          throw new ArgumentNullException("checksum");
        }
        else
        {
          var result = 17;
          for (int i = 0; i < checksum.Length; i++)
          {
            result = result * 23 + checksum[i];
          }

          return result;
        }
      }
    }

    public void AddAssertionChecksum(byte[] checksum)
    {
      Contract.Requires(checksum != null);

      if (AssertionChecksums != null)
      {
        AssertionChecksums.Add(checksum);
      }
    }

    public ISet<byte[]> AssertionChecksumsInCachedSnapshot { get; set; }

    public bool IsAssertionChecksumInCachedSnapshot(byte[] checksum)
    {
      Contract.Requires(AssertionChecksumsInCachedSnapshot != null);

      return AssertionChecksumsInCachedSnapshot.Contains(checksum);
    }

    public IList<AssertCmd> RecycledFailingAssertions { get; protected set; }

    public void AddRecycledFailingAssertion(AssertCmd assertion)
    {
      if (RecycledFailingAssertions == null)
      {
        RecycledFailingAssertions = new List<AssertCmd>();
      }

      RecycledFailingAssertions.Add(assertion);
    }

    public Cmd ExplicitAssumptionAboutCachedPrecondition { get; set; }

    // Strongly connected components
    private StronglyConnectedComponents<Block /*!*/> scc;

    [ContractInvariantMethod]
    void ObjectInvariant()
    {
      Contract.Invariant(LocVars != null);
      Contract.Invariant(cce.NonNullElements(Blocks));
      Contract.Invariant(cce.NonNullElements(OriginalBlocks, true));
      Contract.Invariant(cce.NonNullElements(scc, true));
    }

    private bool BlockPredecessorsComputed;

    public bool StronglyConnectedComponentsComputed
    {
      get { return this.scc != null; }
    }

    public bool IsSkipVerification(CoreOptions options)
    {
      bool verify = true;
      cce.NonNull(this.Proc).CheckBooleanAttribute("verify", ref verify);
      this.CheckBooleanAttribute("verify", ref verify);
      if (!verify) {
        return true;
      }

      if (options.ProcedureInlining == CoreOptions.Inlining.Assert ||
          options.ProcedureInlining == CoreOptions.Inlining.Assume) {
        Expr inl = this.FindExprAttribute("inline");
        if (inl == null) {
          inl = this.Proc.FindExprAttribute("inline");
        }

        if (inl != null) {
          return true;
        }
      }

      if (options.StratifiedInlining > 0) {
        return !QKeyValue.FindBoolAttribute(Attributes, "entrypoint");
      }

      return false;
    }

    public string Id
    {
      get
      {
        var id = FindStringAttribute("id");
        if (id == null)
        {
          id = Name + GetHashCode().ToString() + ":0";
        }

        return id;
      }
    }

    public int Priority
    {
      get
      {
        int priority = 0;
        CheckIntAttribute("priority", ref priority);
        if (priority <= 0)
        {
          priority = 1;
        }

        return priority;
      }
    }

    public IDictionary<byte[], object> ErrorChecksumToCachedError { get; private set; }

    public bool IsErrorChecksumInCachedSnapshot(byte[] checksum)
    {
      Contract.Requires(ErrorChecksumToCachedError != null);

      return ErrorChecksumToCachedError.ContainsKey(checksum);
    }

    public void SetErrorChecksumToCachedError(IEnumerable<Tuple<byte[], byte[], object>> errors)
    {
      Contract.Requires(errors != null);

      ErrorChecksumToCachedError = new Dictionary<byte[], object>(ChecksumComparer.Default);
      foreach (var kv in errors)
      {
        ErrorChecksumToCachedError[kv.Item1] = kv.Item3;
        if (kv.Item2 != null)
        {
          ErrorChecksumToCachedError[kv.Item2] = null;
        }
      }
    }

    public bool HasCachedSnapshot
    {
      get { return ErrorChecksumToCachedError != null && AssertionChecksumsInCachedSnapshot != null; }
    }

    public bool AnyErrorsInCachedSnapshot
    {
      get
      {
        Contract.Requires(ErrorChecksumToCachedError != null);

        return ErrorChecksumToCachedError.Any();
      }
    }

    IList<LocalVariable> injectedAssumptionVariables;

    public IList<LocalVariable> InjectedAssumptionVariables
    {
      get { return injectedAssumptionVariables != null ? injectedAssumptionVariables : new List<LocalVariable>(); }
    }

    IList<LocalVariable> doomedInjectedAssumptionVariables;

    public IList<LocalVariable> DoomedInjectedAssumptionVariables
    {
      get
      {
        return doomedInjectedAssumptionVariables != null
          ? doomedInjectedAssumptionVariables
          : new List<LocalVariable>();
      }
    }

    public List<LocalVariable> RelevantInjectedAssumptionVariables(Dictionary<Variable, Expr> incarnationMap)
    {
      return InjectedAssumptionVariables.Where(v =>
      {
        if (incarnationMap.TryGetValue(v, out var e))
        {
          var le = e as LiteralExpr;
          return le == null || !le.IsTrue;
        }
        else
        {
          return false;
        }
      }).ToList();
    }

    public List<LocalVariable> RelevantDoomedInjectedAssumptionVariables(Dictionary<Variable, Expr> incarnationMap)
    {
      return DoomedInjectedAssumptionVariables.Where(v =>
      {
        if (incarnationMap.TryGetValue(v, out var e))
        {
          var le = e as LiteralExpr;
          return le == null || !le.IsTrue;
        }
        else
        {
          return false;
        }
      }).ToList();
    }

    public Expr ConjunctionOfInjectedAssumptionVariables(Dictionary<Variable, Expr> incarnationMap, out bool isTrue)
    {
      Contract.Requires(incarnationMap != null);

      var vars = RelevantInjectedAssumptionVariables(incarnationMap).Select(v => incarnationMap[v]).ToList();
      isTrue = vars.Count == 0;
      return LiteralExpr.BinaryTreeAnd(vars);
    }

    public void InjectAssumptionVariable(LocalVariable variable, bool isDoomed = false)
    {
      LocVars.Add(variable);
      if (isDoomed)
      {
        if (doomedInjectedAssumptionVariables == null)
        {
          doomedInjectedAssumptionVariables = new List<LocalVariable>();
        }

        doomedInjectedAssumptionVariables.Add(variable);
      }
      else
      {
        if (injectedAssumptionVariables == null)
        {
          injectedAssumptionVariables = new List<LocalVariable>();
        }

        injectedAssumptionVariables.Add(variable);
      }
    }

    public Implementation(IToken tok, string name, List<TypeVariable> typeParams, List<Variable> inParams,
      List<Variable> outParams, List<Variable> localVariables, [Captured] StmtList structuredStmts, QKeyValue kv)
      : this(tok, name, typeParams, inParams, outParams, localVariables, structuredStmts, kv, new Errors())
    {
      Contract.Requires(structuredStmts != null);
      Contract.Requires(localVariables != null);
      Contract.Requires(outParams != null);
      Contract.Requires(inParams != null);
      Contract.Requires(typeParams != null);
      Contract.Requires(name != null);
      Contract.Requires(tok != null);
      //:this(tok, name, typeParams, inParams, outParams, localVariables, structuredStmts, null, new Errors());
    }

    public Implementation(IToken tok, string name, List<TypeVariable> typeParams, List<Variable> inParams,
      List<Variable> outParams, List<Variable> localVariables, [Captured] StmtList structuredStmts)
      : this(tok, name, typeParams, inParams, outParams, localVariables, structuredStmts, null, new Errors())
    {
      Contract.Requires(structuredStmts != null);
      Contract.Requires(localVariables != null);
      Contract.Requires(outParams != null);
      Contract.Requires(inParams != null);
      Contract.Requires(typeParams != null);
      Contract.Requires(name != null);
      Contract.Requires(tok != null);
      //:this(tok, name, typeParams, inParams, outParams, localVariables, structuredStmts, null, new Errors());
    }

    public Implementation(IToken tok, string name, List<TypeVariable> typeParams, List<Variable> inParams,
      List<Variable> outParams, List<Variable> localVariables, [Captured] StmtList structuredStmts, Errors errorHandler)
      : this(tok, name, typeParams, inParams, outParams, localVariables, structuredStmts, null, errorHandler)
    {
      Contract.Requires(errorHandler != null);
      Contract.Requires(structuredStmts != null);
      Contract.Requires(localVariables != null);
      Contract.Requires(outParams != null);
      Contract.Requires(inParams != null);
      Contract.Requires(typeParams != null);
      Contract.Requires(name != null);
      Contract.Requires(tok != null);
      //:this(tok, name, typeParams, inParams, outParams, localVariables, structuredStmts, null, errorHandler);
    }

    public Implementation(IToken /*!*/ tok,
      string /*!*/ name,
      List<TypeVariable> /*!*/ typeParams,
      List<Variable> /*!*/ inParams,
      List<Variable> /*!*/ outParams,
      List<Variable> /*!*/ localVariables,
      [Captured] StmtList /*!*/ structuredStmts,
      QKeyValue kv,
      Errors /*!*/ errorHandler)
      : base(tok, name, typeParams, inParams, outParams)
    {
      Contract.Requires(tok != null);
      Contract.Requires(name != null);
      Contract.Requires(typeParams != null);
      Contract.Requires(inParams != null);
      Contract.Requires(outParams != null);
      Contract.Requires(localVariables != null);
      Contract.Requires(structuredStmts != null);
      Contract.Requires(errorHandler != null);
      LocVars = localVariables;
      StructuredStmts = structuredStmts;
      BigBlocksResolutionContext ctx = new BigBlocksResolutionContext(structuredStmts, errorHandler);
      Blocks = ctx.Blocks;
      BlockPredecessorsComputed = false;
      scc = null;
      Attributes = kv;
    }

    public Implementation(IToken tok, string name, List<TypeVariable> typeParams, List<Variable> inParams,
      List<Variable> outParams, List<Variable> localVariables, [Captured] List<Block /*!*/> block)
      : this(tok, name, typeParams, inParams, outParams, localVariables, block, null)
    {
      Contract.Requires(cce.NonNullElements(block));
      Contract.Requires(localVariables != null);
      Contract.Requires(outParams != null);
      Contract.Requires(inParams != null);
      Contract.Requires(typeParams != null);
      Contract.Requires(name != null);
      Contract.Requires(tok != null);
      //:this(tok, name, typeParams, inParams, outParams, localVariables, block, null);
    }

    public Implementation(IToken /*!*/ tok,
      string /*!*/ name,
      List<TypeVariable> /*!*/ typeParams,
      List<Variable> /*!*/ inParams,
      List<Variable> /*!*/ outParams,
      List<Variable> /*!*/ localVariables,
      [Captured] List<Block /*!*/> /*!*/ blocks,
      QKeyValue kv)
      : base(tok, name, typeParams, inParams, outParams)
    {
      Contract.Requires(name != null);
      Contract.Requires(inParams != null);
      Contract.Requires(outParams != null);
      Contract.Requires(localVariables != null);
      Contract.Requires(cce.NonNullElements(blocks));
      LocVars = localVariables;
      Blocks = blocks;
      BlockPredecessorsComputed = false;
      scc = null;
      Attributes = kv;
    }

    public override void Emit(TokenTextWriter stream, int level)
    {
      //Contract.Requires(stream != null);
      stream.Write(this, level, "implementation ");
      EmitAttributes(stream);
      stream.Write(this, level, "{0}", TokenTextWriter.SanitizeIdentifier(this.Name));
      EmitSignature(stream, false);
      stream.WriteLine();

      stream.WriteLine(level, "{0}", '{');

      foreach (Variable /*!*/ v in this.LocVars)
      {
        Contract.Assert(v != null);
        v.Emit(stream, level + 1);
      }

      if (this.StructuredStmts != null && !stream.Options.PrintInstrumented &&
          !stream.Options.PrintInlined)
      {
        if (this.LocVars.Count > 0)
        {
          stream.WriteLine();
        }

        if (stream.Options.PrintUnstructured < 2)
        {
          if (stream.Options.PrintUnstructured == 1)
          {
            stream.WriteLine(this, level + 1, "/*** structured program:");
          }

          this.StructuredStmts.Emit(stream, level + 1);
          if (stream.Options.PrintUnstructured == 1)
          {
            stream.WriteLine(level + 1, "**** end structured program */");
          }
        }
      }

      if (this.StructuredStmts == null || 1 <= stream.Options.PrintUnstructured ||
          stream.Options.PrintInstrumented || stream.Options.PrintInlined)
      {
        foreach (Block b in this.Blocks)
        {
          b.Emit(stream, level + 1);
        }
      }

      stream.WriteLine(level, "{0}", '}');

      stream.WriteLine();
      stream.WriteLine();
    }

    public override void Register(ResolutionContext rc)
    {
      //Contract.Requires(rc != null);
      // nothing to register
    }

    public override void Resolve(ResolutionContext rc)
    {
      //Contract.Requires(rc != null);
      if (Proc != null)
      {
        // already resolved
        return;
      }

      Proc = rc.LookUpProcedure(cce.NonNull(this.Name));
      if (Proc == null)
      {
        rc.Error(this, "implementation given for undeclared procedure: {0}", this.Name);
      }

      int previousTypeBinderState = rc.TypeBinderState;
      try
      {
        RegisterTypeParameters(rc);

        rc.PushVarContext();
        RegisterFormals(InParams, rc);
        RegisterFormals(OutParams, rc);

        foreach (Variable /*!*/ v in LocVars)
        {
          Contract.Assert(v != null);
          v.Register(rc);
          v.Resolve(rc);
        }

        foreach (Variable /*!*/ v in LocVars)
        {
          Contract.Assert(v != null);
          v.ResolveWhere(rc);
        }

        rc.PushProcedureContext();
        foreach (Block b in Blocks)
        {
          b.Register(rc);
        }

        ResolveAttributes(rc);

        rc.StateMode = ResolutionContext.State.Two;
        foreach (Block b in Blocks)
        {
          b.Resolve(rc);
        }

        rc.StateMode = ResolutionContext.State.Single;

        rc.PopProcedureContext();
        rc.PopVarContext();

        Type.CheckBoundVariableOccurrences(TypeParameters,
          InParams.Select(Item => Item.TypedIdent.Type).ToList(),
          OutParams.Select(Item => Item.TypedIdent.Type).ToList(),
          this.tok, "implementation arguments",
          rc);
      }
      finally
      {
        rc.TypeBinderState = previousTypeBinderState;
      }

      SortTypeParams();
    }

    public override void Typecheck(TypecheckingContext tc)
    {
      //Contract.Requires(tc != null);
      base.Typecheck(tc);

      Contract.Assume(this.Proc != null);

      if (this.TypeParameters.Count != Proc.TypeParameters.Count)
      {
        tc.Error(this, "mismatched number of type parameters in procedure implementation: {0}",
          this.Name);
      }
      else
      {
        // if the numbers of type parameters are different, it is
        // difficult to compare the argument types
        MatchFormals(this.InParams, Proc.InParams, "in", tc);
        MatchFormals(this.OutParams, Proc.OutParams, "out", tc);
      }

      foreach (Variable /*!*/ v in LocVars)
      {
        Contract.Assert(v != null);
        v.Typecheck(tc);
      }

      List<IdentifierExpr> oldFrame = tc.Frame;
      bool oldYields = tc.Yields;
      tc.Frame = Proc.Modifies;
      tc.Yields = QKeyValue.FindBoolAttribute(Proc.Attributes, CivlAttributes.YIELDS);
      foreach (Block b in Blocks)
      {
        b.Typecheck(tc);
      }

      Contract.Assert(tc.Frame == Proc.Modifies);
      tc.Frame = oldFrame;
      tc.Yields = oldYields;
    }

    void MatchFormals(List<Variable> /*!*/ implFormals, List<Variable> /*!*/ procFormals, string /*!*/ inout,
      TypecheckingContext /*!*/ tc)
    {
      Contract.Requires(implFormals != null);
      Contract.Requires(procFormals != null);
      Contract.Requires(inout != null);
      Contract.Requires(tc != null);
      if (implFormals.Count != procFormals.Count)
      {
        tc.Error(this, "mismatched number of {0}-parameters in procedure implementation: {1}",
          inout, this.Name);
      }
      else
      {
        // unify the type parameters so that types can be compared
        Contract.Assert(Proc != null);
        Contract.Assert(this.TypeParameters.Count == Proc.TypeParameters.Count);

        IDictionary<TypeVariable /*!*/, Type /*!*/> /*!*/
          subst1 =
            new Dictionary<TypeVariable /*!*/, Type /*!*/>();
        IDictionary<TypeVariable /*!*/, Type /*!*/> /*!*/
          subst2 =
            new Dictionary<TypeVariable /*!*/, Type /*!*/>();

        for (int i = 0; i < this.TypeParameters.Count; ++i)
        {
          TypeVariable /*!*/
            newVar =
              new TypeVariable(Token.NoToken, Proc.TypeParameters[i].Name);
          Contract.Assert(newVar != null);
          subst1.Add(Proc.TypeParameters[i], newVar);
          subst2.Add(this.TypeParameters[i], newVar);
        }

        for (int i = 0; i < implFormals.Count; i++)
        {
          // the names of the formals are allowed to change from the proc to the impl

          // but types must be identical
          Type t = cce.NonNull((Variable) implFormals[i]).TypedIdent.Type.Substitute(subst2);
          Type u = cce.NonNull((Variable) procFormals[i]).TypedIdent.Type.Substitute(subst1);
          if (!t.Equals(u))
          {
            string /*!*/
              a = cce.NonNull((Variable) implFormals[i]).Name;
            Contract.Assert(a != null);
            string /*!*/
              b = cce.NonNull((Variable) procFormals[i]).Name;
            Contract.Assert(b != null);
            string /*!*/
              c;
            if (a == b)
            {
              c = a;
            }
            else
            {
              c = String.Format("{0} (named {1} in implementation)", b, a);
            }

            tc.Error(this, "mismatched type of {0}-parameter in implementation {1}: {2}", inout, this.Name, c);
          }
        }
      }
    }

    private Dictionary<Variable, Expr> /*?*/
      formalMap = null;

    public void ResetImplFormalMap()
    {
      this.formalMap = null;
    }

    public Dictionary<Variable, Expr> /*!*/ GetImplFormalMap(CoreOptions options)
    {
      Contract.Ensures(Contract.Result<Dictionary<Variable, Expr>>() != null);

      if (this.formalMap != null)
      {
        return this.formalMap;
      }
      else
      {
        Dictionary<Variable, Expr> /*!*/
          map = new Dictionary<Variable, Expr>(InParams.Count + OutParams.Count);

        Contract.Assume(this.Proc != null);
        Contract.Assume(InParams.Count == Proc.InParams.Count);
        for (int i = 0; i < InParams.Count; i++)
        {
          Variable /*!*/
            v = InParams[i];
          Contract.Assert(v != null);
          IdentifierExpr ie = new IdentifierExpr(v.tok, v);
          Variable /*!*/
            pv = Proc.InParams[i];
          Contract.Assert(pv != null);
          map.Add(pv, ie);
        }

        System.Diagnostics.Debug.Assert(OutParams.Count == Proc.OutParams.Count);
        for (int i = 0; i < OutParams.Count; i++)
        {
          Variable /*!*/
            v = cce.NonNull(OutParams[i]);
          IdentifierExpr ie = new IdentifierExpr(v.tok, v);
          Variable pv = cce.NonNull(Proc.OutParams[i]);
          map.Add(pv, ie);
        }

        this.formalMap = map;

        if (options.PrintWithUniqueASTIds)
        {
          Console.WriteLine("Implementation.GetImplFormalMap on {0}:", this.Name);
          using TokenTextWriter stream =
            new TokenTextWriter("<console>", Console.Out, /*setTokens=*/false, /*pretty=*/ false, options);
          foreach (var e in map)
          {
            Console.Write("  ");
            cce.NonNull((Variable /*!*/) e.Key).Emit(stream, 0);
            Console.Write("  --> ");
            cce.NonNull((Expr) e.Value).Emit(stream);
            Console.WriteLine();
          }
        }

        return map;
      }
    }

    /// <summary>
    /// Return a collection of blocks that are reachable from the block passed as a parameter.
    /// The block must be defined in the current implementation
    /// </summary>
    public ICollection<Block /*!*/> GetConnectedComponents(Block startingBlock)
    {
      Contract.Requires(startingBlock != null);
      Contract.Ensures(cce.NonNullElements(Contract.Result<ICollection<Block>>(), true));
      Contract.Assert(this.Blocks.Contains(startingBlock));

      if (!this.BlockPredecessorsComputed)
      {
        ComputeStronglyConnectedComponents();
      }

#if DEBUG_PRINT
      System.Console.WriteLine("* Strongly connected components * \n{0} \n ** ", scc);
#endif

      foreach (ICollection<Block /*!*/> component in cce.NonNull(this.scc))
      {
        foreach (Block /*!*/ b in component)
        {
          Contract.Assert(b != null);
          if (b == startingBlock) // We found the compontent that owns the startingblock
          {
            return component;
          }
        }
      }

      {
        Contract.Assert(false);
        throw new cce.UnreachableException();
      } // if we are here, it means that the block is not in one of the components. This is an error.
    }

    /// <summary>
    /// Compute the strongly connected compontents of the blocks in the implementation.
    /// As a side effect, it also computes the "predecessor" relation for the block in the implementation
    /// </summary>
    override public void ComputeStronglyConnectedComponents()
    {
      if (!this.BlockPredecessorsComputed)
      {
        ComputePredecessorsForBlocks();
      }

      Adjacency<Block /*!*/> next = new Adjacency<Block /*!*/>(Successors);
      Adjacency<Block /*!*/> prev = new Adjacency<Block /*!*/>(Predecessors);

      this.scc = new StronglyConnectedComponents<Block /*!*/>(this.Blocks, next, prev);
      scc.Compute();


      foreach (Block /*!*/ block in this.Blocks)
      {
        Contract.Assert(block != null);
        block.Predecessors = new List<Block>();
      }
    }

    /// <summary>
    /// Reset the abstract stated computed before
    /// </summary>
    override public void ResetAbstractInterpretationState()
    {
      foreach (Block /*!*/ b in this.Blocks)
      {
        Contract.Assert(b != null);
        b.ResetAbstractInterpretationState();
      }
    }

    /// <summary>
    /// A private method used as delegate for the strongly connected components.
    /// It return, given a node, the set of its successors
    /// </summary>
    private IEnumerable /*<Block!>*/ /*!*/ Successors(Block node)
    {
      Contract.Requires(node != null);
      Contract.Ensures(Contract.Result<IEnumerable>() != null);

      GotoCmd gotoCmd = node.TransferCmd as GotoCmd;

      if (gotoCmd != null)
      {
        // If it is a gotoCmd
        Contract.Assert(gotoCmd.labelTargets != null);

        return gotoCmd.labelTargets;
      }
      else
      {
        // otherwise must be a ReturnCmd
        Contract.Assert(node.TransferCmd is ReturnCmd);

        return new List<Block /*!*/>();
      }
    }

    /// <summary>
    /// A private method used as delegate for the strongly connected components.
    /// It return, given a node, the set of its predecessors
    /// </summary>
    private IEnumerable /*<Block!>*/ /*!*/ Predecessors(Block node)
    {
      Contract.Requires(node != null);
      Contract.Ensures(Contract.Result<IEnumerable>() != null);

      Contract.Assert(this.BlockPredecessorsComputed);

      return node.Predecessors;
    }

    /// <summary>
    /// Compute the predecessor informations for the blocks
    /// </summary>
    public void ComputePredecessorsForBlocks()
    {
      foreach (Block b in this.Blocks)
      {
        b.Predecessors = new List<Block>();
      }

      foreach (Block b in this.Blocks)
      {
        GotoCmd gtc = b.TransferCmd as GotoCmd;
        if (gtc != null)
        {
          Contract.Assert(gtc.labelTargets != null);
          foreach (Block /*!*/ dest in gtc.labelTargets)
          {
            Contract.Assert(dest != null);
            dest.Predecessors.Add(b);
          }
        }
      }

      this.BlockPredecessorsComputed = true;
    }

    public void PruneUnreachableBlocks(CoreOptions options)
    {
      ArrayList /*Block!*/
        visitNext = new ArrayList /*Block!*/();
      List<Block /*!*/> reachableBlocks = new List<Block /*!*/>();
      HashSet<Block> reachable = new HashSet<Block>(); // the set of elements in "reachableBlocks"

      visitNext.Add(this.Blocks[0]);
      while (visitNext.Count != 0)
      {
        Block b = cce.NonNull((Block) visitNext[visitNext.Count - 1]);
        visitNext.RemoveAt(visitNext.Count - 1);
        if (!reachable.Contains(b))
        {
          reachableBlocks.Add(b);
          reachable.Add(b);
          if (b.TransferCmd is GotoCmd)
          {
            if (options.PruneInfeasibleEdges)
            {
              foreach (Cmd /*!*/ s in b.Cmds)
              {
                Contract.Assert(s != null);
                if (s is PredicateCmd)
                {
                  LiteralExpr e = ((PredicateCmd) s).Expr as LiteralExpr;
                  if (e != null && e.IsFalse)
                  {
                    // This statement sequence will never reach the end, because of this "assume false" or "assert false".
                    // Hence, it does not reach its successors.
                    b.TransferCmd = new ReturnCmd(b.TransferCmd.tok);
                    goto NEXT_BLOCK;
                  }
                }
              }
            }

            // it seems that the goto statement at the end may be reached
            foreach (Block succ in cce.NonNull((GotoCmd) b.TransferCmd).labelTargets)
            {
              Contract.Assume(succ != null);
              visitNext.Add(succ);
            }
          }
        }

        NEXT_BLOCK:
        {
        }
      }

      this.Blocks = reachableBlocks;
    }

    public override Absy StdDispatch(StandardVisitor visitor)
    {
      //Contract.Requires(visitor != null);
      Contract.Ensures(Contract.Result<Absy>() != null);
      return visitor.VisitImplementation(this);
    }

    public void FreshenCaptureStates()
    {
      // Assume commands with the "captureState" attribute allow model states to be
      // captured for error reporting.
      // Some program transformations, such as loop unrolling, duplicate parts of the
      // program, leading to "capture-state-assumes" being duplicated.  This leads
      // to ambiguity when getting a state from the model.
      // This method replaces the key of every "captureState" attribute with something
      // unique

      int FreshCounter = 0;
      foreach (var b in Blocks)
      {
        List<Cmd> newCmds = new List<Cmd>();
        for (int i = 0; i < b.Cmds.Count(); i++)
        {
          var a = b.Cmds[i] as AssumeCmd;
          if (a != null && (QKeyValue.FindStringAttribute(a.Attributes, "captureState") != null))
          {
            string StateName = QKeyValue.FindStringAttribute(a.Attributes, "captureState");
            newCmds.Add(new AssumeCmd(Token.NoToken, a.Expr, FreshenCaptureState(a.Attributes, FreshCounter)));
            FreshCounter++;
          }
          else
          {
            newCmds.Add(b.Cmds[i]);
          }
        }

        b.Cmds = newCmds;
      }
    }

    private QKeyValue FreshenCaptureState(QKeyValue Attributes, int FreshCounter)
    {
      // Returns attributes identical to Attributes, but:
      // - reversed (for ease of implementation; should not matter)
      // - with the value for "captureState" replaced by a fresh value
      Contract.Requires(QKeyValue.FindStringAttribute(Attributes, "captureState") != null);
      string FreshValue = QKeyValue.FindStringAttribute(Attributes, "captureState") + "$renamed$" + Name + "$" +
                          FreshCounter;

      QKeyValue result = null;
      while (Attributes != null)
      {
        if (Attributes.Key.Equals("captureState"))
        {
          result = new QKeyValue(Token.NoToken, Attributes.Key, new List<object>() {FreshValue}, result);
        }
        else
        {
          result = new QKeyValue(Token.NoToken, Attributes.Key, Attributes.Params, result);
        }

        Attributes = Attributes.Next;
      }

      return result;
    }
  }


  public class TypedIdent : Absy
  {
    public const string NoName = "";

    private string /*!*/
      _name;

    public string /*!*/ Name
    {
      get
      {
        Contract.Ensures(Contract.Result<string>() != null);
        return this._name;
      }
      set
      {
        Contract.Requires(value != null);
        this._name = value;
      }
    }

    private Type /*!*/
      _type;

    public Type /*!*/ Type
    {
      get
      {
        Contract.Ensures(Contract.Result<Type>() != null);
        return this._type;
      }
      set
      {
        Contract.Requires(value != null);
        this._type = value;
      }
    }

    [ContractInvariantMethod]
    void ObjectInvariant()
    {
      Contract.Invariant(this._name != null);
      Contract.Invariant(this._type != null);
    }

    public Expr WhereExpr;

    // [NotDelayed]
    public TypedIdent(IToken /*!*/ tok, string /*!*/ name, Type /*!*/ type)
      : this(tok, name, type, null)
    {
      Contract.Requires(tok != null);
      Contract.Requires(name != null);
      Contract.Requires(type != null);
      Contract.Ensures(this.WhereExpr == null); //PM: needed to verify BoogiePropFactory.FreshBoundVariable
      //:this(tok, name, type, null); // here for aesthetic reasons
    }

    // [NotDelayed]
    public TypedIdent(IToken /*!*/ tok, string /*!*/ name, Type /*!*/ type, Expr whereExpr)
      : base(tok)
    {
      Contract.Requires(tok != null);
      Contract.Requires(name != null);
      Contract.Requires(type != null);
      Contract.Ensures(this.WhereExpr == whereExpr);
      this._name = name;
      this._type = type;
      this.WhereExpr = whereExpr;
    }

    public bool HasName
    {
      get { return this.Name != NoName; }
    }

    /// <summary>
    /// An "emitType" value of "false" is ignored if "this.Name" is "NoName".
    /// </summary>
    public void Emit(TokenTextWriter stream, bool emitType)
    {
      Contract.Requires(stream != null);
      stream.SetToken(this);
      stream.push();
      if (this.Name != NoName && emitType)
      {
        stream.Write("{0}: ", TokenTextWriter.SanitizeIdentifier(this.Name));
        this.Type.Emit(stream);
      }
      else if (this.Name != NoName)
      {
        stream.Write("{0}", TokenTextWriter.SanitizeIdentifier(this.Name));
      }
      else
      {
        this.Type.Emit(stream);
      }

      if (this.WhereExpr != null)
      {
        stream.sep();
        stream.Write(" where ");
        this.WhereExpr.Emit(stream);
      }

      stream.pop();
    }

    public override void Resolve(ResolutionContext rc)
    {
      //Contract.Requires(rc != null);
      // NOTE: WhereExpr needs to be resolved by the caller, because the caller must provide a modified ResolutionContext
      this.Type = this.Type.ResolveType(rc);
    }

    public override void Typecheck(TypecheckingContext tc)
    {
      //Contract.Requires(tc != null);
      //   type variables can occur when working with polymorphic functions/procedures
      //      if (!this.Type.IsClosed)
      //        tc.Error(this, "free variables in type of an identifier: {0}",
      //                 this.Type.FreeVariables);
      if (this.WhereExpr != null)
      {
        this.WhereExpr.Typecheck(tc);
        Contract.Assert(this.WhereExpr.Type != null); // follows from postcondition of Expr.Typecheck
        if (!this.WhereExpr.Type.Unify(Type.Bool))
        {
          tc.Error(this, "where clauses must be of type bool");
        }
      }
    }

    public override Absy StdDispatch(StandardVisitor visitor)
    {
      //Contract.Requires(visitor != null);
      Contract.Ensures(Contract.Result<Absy>() != null);
      return visitor.VisitTypedIdent(this);
    }
  }

  #region Helper methods for generic Sequences

  public static class TypeVariableSeqAlgorithms
  {
    public static void AppendWithoutDups(this List<TypeVariable> tvs, List<TypeVariable> s1)
    {
      Contract.Requires(s1 != null);
      for (int i = 0; i < s1.Count; i++)
      {
        TypeVariable /*!*/
          next = s1[i];
        Contract.Assert(next != null);
        if (!tvs.Contains(next))
        {
          tvs.Add(next);
        }
      }
    }
  }

  public static class Emitter
  {
    public static void Emit(this List<Declaration /*!*/> /*!*/ decls, TokenTextWriter stream)
    {
      Contract.Requires(stream != null);
      Contract.Requires(cce.NonNullElements(decls));
      bool first = true;
      foreach (Declaration d in decls)
      {
        if (d == null)
        {
          continue;
        }

        if (first)
        {
          first = false;
        }
        else
        {
          stream.WriteLine();
        }

        d.Emit(stream, 0);
      }
    }

    public static void Emit(this List<String> ss, TokenTextWriter stream)
    {
      Contract.Requires(stream != null);
      string sep = "";
      foreach (string /*!*/ s in ss)
      {
        Contract.Assert(s != null);
        stream.Write(sep);
        sep = ", ";
        stream.Write(s);
      }
    }

    public static void Emit(this IList<Expr> ts, TokenTextWriter stream)
    {
      Contract.Requires(stream != null);
      string sep = "";
      stream.push();
      foreach (Expr /*!*/ e in ts)
      {
        Contract.Assert(e != null);
        stream.Write(sep);
        sep = ", ";
        stream.sep();
        e.Emit(stream);
      }

      stream.pop();
    }

    public static void Emit(this List<IdentifierExpr> ids, TokenTextWriter stream, bool printWhereComments)
    {
      Contract.Requires(stream != null);
      string sep = "";
      foreach (IdentifierExpr /*!*/ e in ids)
      {
        Contract.Assert(e != null);
        stream.Write(sep);
        sep = ", ";
        e.Emit(stream);

        if (printWhereComments && e.Decl != null && e.Decl.TypedIdent.WhereExpr != null)
        {
          stream.Write(" /* where ");
          e.Decl.TypedIdent.WhereExpr.Emit(stream);
          stream.Write(" */");
        }
      }
    }

    public static void Emit(this List<Variable> vs, TokenTextWriter stream, bool emitAttributes)
    {
      Contract.Requires(stream != null);
      string sep = "";
      stream.push();
      foreach (Variable /*!*/ v in vs)
      {
        Contract.Assert(v != null);
        stream.Write(sep);
        sep = ", ";
        stream.sep();
        v.EmitVitals(stream, 0, emitAttributes);
      }

      stream.pop();
    }

    public static void Emit(this List<Type> tys, TokenTextWriter stream, string separator)
    {
      Contract.Requires(separator != null);
      Contract.Requires(stream != null);
      string sep = "";
      foreach (Type /*!*/ v in tys)
      {
        Contract.Assert(v != null);
        stream.Write(sep);
        sep = separator;
        v.Emit(stream);
      }
    }

    public static void Emit(this List<TypeVariable> tvs, TokenTextWriter stream, string separator)
    {
      Contract.Requires(separator != null);
      Contract.Requires(stream != null);
      string sep = "";
      foreach (TypeVariable /*!*/ v in tvs)
      {
        Contract.Assert(v != null);
        stream.Write(sep);
        sep = separator;
        v.Emit(stream);
      }
    }
  }

  #endregion


  #region Regular Expressions

  // a data structure to recover the "program structure" from the flow graph
  public abstract class RE : Cmd
  {
    public RE()
      : base(Token.NoToken)
    {
    }

    public override void AddAssignedVariables(List<Variable> vars)
    {
      //Contract.Requires(vars != null);
      throw new NotImplementedException();
    }
  }

  public class AtomicRE : RE
  {
    private Block /*!*/
      _b;

    public Block b
    {
      get
      {
        Contract.Ensures(Contract.Result<Block>() != null);
        return this._b;
      }
      set
      {
        Contract.Requires(value != null);
        this._b = value;
      }
    }

    [ContractInvariantMethod]
    void ObjectInvariant()
    {
      Contract.Invariant(this._b != null);
    }

    public AtomicRE(Block block)
    {
      Contract.Requires(block != null);
      this._b = block;
    }

    public override void Resolve(ResolutionContext rc)
    {
      //Contract.Requires(rc != null);
      b.Resolve(rc);
    }

    public override void Typecheck(TypecheckingContext tc)
    {
      //Contract.Requires(tc != null);
      b.Typecheck(tc);
    }

    public override void Emit(TokenTextWriter stream, int level)
    {
      //Contract.Requires(stream != null);
      b.Emit(stream, level);
    }

    public override Absy StdDispatch(StandardVisitor visitor)
    {
      //Contract.Requires(visitor != null);
      Contract.Ensures(Contract.Result<Absy>() != null);
      return visitor.VisitAtomicRE(this);
    }
  }

  public abstract class CompoundRE : RE
  {
    public override void Resolve(ResolutionContext rc)
    {
      //Contract.Requires(rc != null);
      return;
    }

    public override void Typecheck(TypecheckingContext tc)
    {
      //Contract.Requires(tc != null);
      return;
    }
  }

  public class Sequential : CompoundRE
  {
    private RE /*!*/
      _first;

    public RE /*!*/ first
    {
      get
      {
        Contract.Ensures(Contract.Result<RE>() != null);
        return this._first;
      }
      set
      {
        Contract.Requires(value != null);
        this._first = value;
      }
    }

    private RE /*!*/
      _second;

    public RE /*!*/ second
    {
      get
      {
        Contract.Ensures(Contract.Result<RE>() != null);
        return this._second;
      }
      set
      {
        Contract.Requires(value != null);
        this._second = value;
      }
    }

    [ContractInvariantMethod]
    void ObjectInvariant()
    {
      Contract.Invariant(this._first != null);
      Contract.Invariant(this._second != null);
    }

    public Sequential(RE first, RE second)
    {
      Contract.Requires(first != null);
      Contract.Requires(second != null);
      this._first = first;
      this._second = second;
    }

    public override void Emit(TokenTextWriter stream, int level)
    {
      //Contract.Requires(stream != null);
      stream.WriteLine();
      stream.WriteLine("{0};", Indent(stream.UseForComputingChecksums ? 0 : level));
      first.Emit(stream, level + 1);
      second.Emit(stream, level + 1);
    }

    public override Absy StdDispatch(StandardVisitor visitor)
    {
      //Contract.Requires(visitor != null);
      Contract.Ensures(Contract.Result<Absy>() != null);
      return visitor.VisitSequential(this);
    }
  }

  public class Choice : CompoundRE
  {
    [ContractInvariantMethod]
    void ObjectInvariant()
    {
      Contract.Invariant(this._rs != null);
    }

    private List<RE> /*!*/
      _rs;

    public List<RE> /*!*/ rs
    {
      //Rename this (and _rs) if possible
      get
      {
        Contract.Ensures(Contract.Result<List<RE>>() != null);
        return this._rs;
      }
      set
      {
        Contract.Requires(value != null);
        this._rs = value;
      }
    }

    public Choice(List<RE> operands)
    {
      Contract.Requires(operands != null);
      this._rs = operands;
    }

    public override void Emit(TokenTextWriter stream, int level)
    {
      //Contract.Requires(stream != null);
      stream.WriteLine();
      stream.WriteLine("{0}[]", Indent(stream.UseForComputingChecksums ? 0 : level));
      foreach (RE /*!*/ r in rs)
      {
        Contract.Assert(r != null);
        r.Emit(stream, level + 1);
      }
    }

    public override Absy StdDispatch(StandardVisitor visitor)
    {
      //Contract.Requires(visitor != null);
      Contract.Ensures(Contract.Result<Absy>() != null);
      return visitor.VisitChoice(this);
    }
  }

  public class DAG2RE
  {
    public static RE Transform(Block b)
    {
      Contract.Requires(b != null);
      Contract.Ensures(Contract.Result<RE>() != null);
      TransferCmd tc = b.TransferCmd;
      if (tc is ReturnCmd)
      {
        return new AtomicRE(b);
      }
      else if (tc is GotoCmd)
      {
        GotoCmd /*!*/
          g = (GotoCmd) tc;
        Contract.Assert(g != null);
        Contract.Assume(g.labelTargets != null);
        if (g.labelTargets.Count == 1)
        {
          return new Sequential(new AtomicRE(b), Transform(cce.NonNull(g.labelTargets[0])));
        }
        else
        {
          List<RE> rs = new List<RE>();
          foreach (Block /*!*/ target in g.labelTargets)
          {
            Contract.Assert(target != null);
            RE r = Transform(target);
            rs.Add(r);
          }

          RE second = new Choice(rs);
          return new Sequential(new AtomicRE(b), second);
        }
      }
      else
      {
        Contract.Assume(false);
        throw new cce.UnreachableException();
      }
    }
  }

  #endregion

  // NOTE: This class is here for convenience, since this file's
  // classes are used pretty much everywhere.

  public class BoogieDebug
  {
    public static bool DoPrinting = false;

    public static void Write(string format, params object[] args)
    {
      Contract.Requires(args != null);
      Contract.Requires(format != null);
      if (DoPrinting)
      {
        Console.Error.Write(format, args);
      }
    }

    public static void WriteLine(string format, params object[] args)
    {
      Contract.Requires(args != null);
      Contract.Requires(format != null);
      if (DoPrinting)
      {
        Console.Error.WriteLine(format, args);
      }
    }

    public static void WriteLine()
    {
      if (DoPrinting)
      {
        Console.Error.WriteLine();
      }
    }
  }
}
