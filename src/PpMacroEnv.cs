namespace MiniC;

public sealed class MacroEnv
{
    private readonly Dictionary<string, Macro> _map =
        new(StringComparer.Ordinal);

    public bool TryGet(string name, out Macro? m) => _map.TryGetValue(name, out m);

    public void Define(Macro m) => _map[m.Name] = m;

    public void Undef(string name) => _map.Remove(name);

    public IEnumerable<Macro> All() => _map.Values;
}

public static class MacroBuilder
{
    public static Macro FromDefine(PpDefineDirective d)
    {
        if (!d.IsFunctionLike)
            return new ObjectMacro(d.Name, d.Replacement);

        return new FunctionMacro(
            d.Name,
            d.Parameters,
            d.IsVariadic,
            d.Replacement
        );
    }
}

public sealed class MacroExpander
{
    private readonly MacroEnv _env;

    // recursion guard – names currently being expanded
    private readonly HashSet<string> _expanding = new(StringComparer.Ordinal);

    public MacroExpander(MacroEnv env) { _env = env; }

    /// Expand a flat token list (e.g., from a PpText node) and yield expanded tokens.
    public IEnumerable<Token> ExpandTokens(IEnumerable<Token> input)
    {
        foreach (var tok in input)
        {
            if (tok.Kind == TokenKind.Identifier)
            {
                var name = tok.Source.Src.AsSpan(tok.Start, tok.Length).ToString();
                if (_env.TryGet(name, out var macro))
                {
                    // function-like macros will be supported later
                    if (macro is ObjectMacro obj)
                    {
                        foreach (var t in ExpandObjectMacro(obj))
                            yield return t;
                        continue; // skip the original identifier
                    }
                }
            }

            // default: pass through
            yield return tok;
        }
    }

    private IEnumerable<Token> ExpandObjectMacro(ObjectMacro m)
    {
        // prevent immediate recursive self-expansion
        if (!_expanding.Add(m.Name))
            return Enumerable.Empty<Token>();

        // Expand the replacement recursively (object macros inside object macros)
        var expanded = new List<Token>();
        foreach (var t in m.Replacement)
        {
            if (t.Kind == TokenKind.Identifier)
            {
                var name = t.Source.Src.AsSpan(t.Start, t.Length).ToString();
                if (_env.TryGet(name, out var nested) && nested is ObjectMacro obj)
                {
                    expanded.AddRange(ExpandObjectMacro(obj));
                    continue;
                }
            }
            expanded.Add(t);
        }

        _expanding.Remove(m.Name);
        return expanded;
    }
}

public abstract class PpWalker
{
    public virtual void Visit(PpTranslationUnit node)
    {
        foreach (var part in node.Parts)
            Visit(part);
    }

    public virtual void Visit(PpGroupPart part)
    {
        switch (part)
        {
            case PpText t: VisitText(t); break;
            case PpIncludeDirective inc: VisitInclude(inc); break;
            case PpDefineDirective def: VisitDefine(def); break;
            case PpUndefDirective u: VisitUndef(u); break;
            case PpIfSection s: VisitIfSection(s); break;
            case PpSimpleDirective s: VisitSimple(s); break;
            default: break;
        }
    }

    protected virtual void VisitText(PpText node) { }

    protected virtual void VisitInclude(PpIncludeDirective node) { }

    protected virtual void VisitDefine(PpDefineDirective node) { }

    protected virtual void VisitUndef(PpUndefDirective node) { }

    protected virtual void VisitIfSection(PpIfSection node)
    {
        VisitIfLike(node.If);
        foreach (var e in node.Elifs)
            VisitElif(e);
        if (node.Else is not null)
            VisitElse(node.Else);
    }

    protected virtual void VisitSimple(PpSimpleDirective node) { }

    protected virtual void VisitIfLike(PpIfLike node)
    {
        foreach (var p in node.Body)
            Visit(p);
    }

    protected virtual void VisitElif(PpElif node)
    {
        foreach (var p in node.Body)
            Visit(p);
    }

    protected virtual void VisitElse(PpElse node)
    {
        foreach (var p in node.Body)
            Visit(p);
    }
}

public sealed class MacroCollector : PpWalker
{
    private readonly MacroEnv _env;

    public MacroCollector(MacroEnv env) => _env = env;

    protected override void VisitDefine(PpDefineDirective node)
    {
        var m = MacroBuilder.FromDefine(node);
        _env.Define(m);
    }

    protected override void VisitUndef(PpUndefDirective node)
    {
        _env.Undef(node.Name);
    }
}

public sealed class PpProjector : PpWalker
{
    private readonly MacroEnv _env;
    private readonly MacroExpander _expander;
    private readonly List<Token> _out = [];

    public PpProjector(MacroEnv env)
    {
        _env = env;
        _expander = new MacroExpander(env);
    }

    public List<Token> Project(PpTranslationUnit tu)
    {
        Visit(tu);
        return _out;
    }

    protected override void VisitDefine(PpDefineDirective node)
        => _env.Define(MacroBuilder.FromDefine(node));

    protected override void VisitUndef(PpUndefDirective node)
        => _env.Undef(node.Name);

    protected override void VisitText(PpText node)
    {
        foreach (var tok in _expander.ExpandTokens(node.Tokens))
            _out.Add(tok);
    }

    protected override void VisitInclude(PpIncludeDirective node)
    {
        // you’ll plug include resolution here later
    }

    protected override void VisitIfSection(PpIfSection node)
    {
        // later: evaluate #if condition and walk correct branch
        VisitIfLike(node.If);
    }
}
