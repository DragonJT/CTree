using System.Collections.Immutable;

namespace MiniC;

public abstract record PpNode;
public sealed record PpTranslationUnit(IReadOnlyList<PpGroupPart> Parts) : PpNode;

public abstract record PpGroupPart : PpNode;

public sealed record PpText(IReadOnlyList<Token> Tokens) : PpGroupPart;

public sealed record PpIncludeDirective(
    IReadOnlyList<Token> Raw // everything after 'include' to EOL (kept raw; header-name or macro form)
) : PpGroupPart;

public sealed record PpDefineDirective(
    string Name,
    bool IsFunctionLike,
    IReadOnlyList<string> Parameters,
    bool IsVariadic,
    IReadOnlyList<Token> Replacement // raw replacement list, no expansion/eval
) : PpGroupPart;

public sealed record PpUndefDirective(string Name) : PpGroupPart;

public enum PpConditionKind { If, Ifdef, Ifndef }

public sealed record PpIfSection(
    PpIfLike If,
    IReadOnlyList<PpElif> Elifs,
    PpElse? Else
) : PpGroupPart;

public sealed record PpIfLike(
    PpConditionKind Kind,
    IReadOnlyList<Token> ConditionTokens,
    IReadOnlyList<PpGroupPart> Body
) : PpNode;

public sealed record PpElif(
    IReadOnlyList<Token> ConditionTokens,
    IReadOnlyList<PpGroupPart> Body
) : PpNode;

public sealed record PpElse(IReadOnlyList<PpGroupPart> Body) : PpNode;

// Catch-all for things like #pragma, #line, #error, vendor extensions, unknowns
public sealed record PpSimpleDirective(
    TokenKind Keyword,
    IReadOnlyList<Token> RestOfLine
) : PpGroupPart;

public abstract record Macro(string Name);

public sealed record ObjectMacro(
    string Name,
    IReadOnlyList<Token> Replacement // raw tokens captured from #define line
) : Macro(Name);

public sealed record FunctionMacro(
    string Name,
    IReadOnlyList<string> Parameters,
    bool IsVariadic,
    IReadOnlyList<Token> Replacement
) : Macro(Name);

public interface ITokenReader
{
    public Token NextToken();
}

public sealed class PpParser
{
    private readonly List<Token> _tokens ;
    private int _idx;

    public PpParser(List<Token> tokens)
    {
        _tokens = tokens;
    }

    // ---------------- core lookahead/consume ----------------

    private Token LA(int k = 0)
    {
        return _tokens[_idx + k];
    }

    private Token Consume() { var t = LA(0); _idx++; return t; }
    private bool Check(TokenKind k) => LA(0).Kind == k;

    private bool Match(TokenKind k)
    {
        if (Check(k)) { Consume(); return true; }
        return false;
    }

    private Token Eat(TokenKind k)
    {
        if (!Check(k)) throw new Exception($"PP: expected {k} but found {LA(0)}");
        return Consume();
    }

    private int Mark() => _idx;
    private void Reset(int m) => _idx = m;

    // ---------------- helpers ----------------

    private static bool LeadingEndsWithNewline(Token t)
    {
        var lead = t.Leading.Span;
        return lead.Length > 0 && lead[^1].Kind == TriviaKind.Newline;
    }

    private static bool IsEndOfLine(Token t) =>
        t.Kind == TokenKind.EOF || LeadingEndsWithNewline(t);

    private static bool AreAdjacent(Token a, Token b) =>
        ReferenceEquals(a.Source, b.Source) && (a.Start + a.Length == b.Start);

    private bool PeekDirective(PpTokenKind kw)
    {
        int m = Mark();
        if (!Check(TokenKind.DirectiveHash)) { Reset(m); return false; }
        Consume(); // '#'
        var ok = LA(0).PpKind == kw;
        Reset(m);
        return ok;
    }

    private PpGroupPart ConsumeThen(Func<PpGroupPart> parse)
    {
        Consume(); 
        return parse();
    }

    private void ConsumeDirective(PpTokenKind expected)
    {
        Eat(TokenKind.DirectiveHash);
        if (LA(0).PpKind != expected)
            throw new Exception($"Expected #{expected} but saw #{LA(0).PpKind}");
        Consume(); 
    }

    private PpDefineDirective ParseDefine()
    {
        var nameTok = Eat(TokenKind.Identifier); // GLAPI
        string name = AsString(nameTok);

        bool fnLike = false;
        var parameters = new List<string>();
        bool variadic = false;

        // function-like only if '(' is ADJACENT to the name (no space)
        if (Check(TokenKind.LParen) && AreAdjacent(nameTok, LA(0)))
        {
            fnLike = true;
            Eat(TokenKind.LParen);
            if (!Check(TokenKind.RParen))
            {
                do
                {
                    if (TryConsumeEllipsis(out _)) { variadic = true; break; }
                    var p = Eat(TokenKind.Identifier);
                    parameters.Add(AsString(p));
                    if (TryConsumeEllipsis(out _)) { variadic = true; }
                } while (Match(TokenKind.Comma));
            }
            Eat(TokenKind.RParen);
        }

        // replacement list: everything to end-of-line (attributes, extern, whatever)
        var repl = CollectRestOfLine();
        return new PpDefineDirective(name, fnLike, parameters, variadic, repl);
    }


    private static string AsString(Token t) => new(t.Source.Src.AsSpan(t.Start, t.Length));

    // Collect tokens until the next token starts on a new line or EOF
    private ImmutableArray<Token> CollectRestOfLine()
    {
        var b = ImmutableArray.CreateBuilder<Token>();
        while (true)
        {
            var n = LA(0);
            if (IsEndOfLine(n)) break;
            b.Add(Consume());
        }
        return b.ToImmutable();
    }

    // Read a group (sequence of parts) until we hit an elif/else/endif (caller decides which)
    private ImmutableArray<PpGroupPart> ParseGroupUntil(params PpTokenKind[] terminators)
    {
        var parts = ImmutableArray.CreateBuilder<PpGroupPart>();

        while (!Check(TokenKind.EOF))
        {
            if (Check(TokenKind.DirectiveHash))
            {
                // Look ahead to see keyword
                int m = Mark();
                Consume(); // '#'
                PpTokenKind kw = LA(0).PpKind;
                Reset(m);

                // stop at a terminator (but do not consume it)
                if (terminators.Any(t => t == kw))
                    break;

                parts.Add(ParseDirective());
            }
            else
            {
                parts.Add(ParseTextRun());
            }
        }

        return parts.ToImmutable();
    }

    private PpText ParseTextRun()
    {
        var toks = ImmutableArray.CreateBuilder<Token>();
        while (!Check(TokenKind.EOF))
        {
            if (Check(TokenKind.DirectiveHash)) break; // potential directive at BOL
            toks.Add(Consume());
        }
        return new PpText(toks.ToImmutable());
    }

    // ---------------- entry point ----------------

    public PpTranslationUnit Parse()
    {
        var parts = ImmutableArray.CreateBuilder<PpGroupPart>();

        while (!Check(TokenKind.EOF))
        {
            if (Check(TokenKind.DirectiveHash))
                parts.Add(ParseDirective());
            else
                parts.Add(ParseTextRun());
        }

        return new PpTranslationUnit(parts.ToImmutable());
    }

    // ---------------- directives ----------------

    private PpGroupPart ParseDirective()
    {
        Eat(TokenKind.DirectiveHash);
        var id = LA(0);
        Consume();

        return id.PpKind switch
        {
            PpTokenKind.Include => ParseInclude(),
            PpTokenKind.Define => ParseDefine(),
            PpTokenKind.Undef => ParseUndef(),
            PpTokenKind.If => ParseIfSection(PpConditionKind.If),
            PpTokenKind.Ifdef => ParseIfSection(PpConditionKind.Ifdef),
            PpTokenKind.Ifndef => ParseIfSection(PpConditionKind.Ifndef),
            PpTokenKind.Elif => throw new Exception("#elif without matching #if"),
            PpTokenKind.Else => throw new Exception("#else without matching #if"),
            PpTokenKind.Endif => throw new Exception("#endif without matching #if"),
            _ => new PpSimpleDirective(id.Kind, CollectRestOfLine())
        };
    }

    private PpIncludeDirective ParseInclude()
    {
        // We don’t try to interpret header-name vs macro-form here;
        // just capture raw tokens to end of line. The projector will resolve it.
        var rest = CollectRestOfLine();
        return new PpIncludeDirective(rest);
    }

    private PpUndefDirective ParseUndef()
    {
        var nameTok = Eat(TokenKind.Identifier);
        var name = AsString(nameTok);
        CollectRestOfLine(); // ignore trailing garbage to EOL
        return new PpUndefDirective(name);
    }

    private bool TryConsumeEllipsis(out Token? firstDot)
    {
        firstDot = null;
        int m = Mark();
        if (Check(TokenKind.Dot))
        {
            var d1 = Consume();
            if (Check(TokenKind.Dot) && AreAdjacent(d1, LA(0)))
            {
                var d2 = Consume();
                if (Check(TokenKind.Dot) && AreAdjacent(d2, LA(0)))
                {
                    var d3 = Consume();
                    firstDot = d1;
                    return true;
                }
            }
        }
        Reset(m);
        return false;
    }

    private PpIfSection ParseIfSection(PpConditionKind kind)
    {
        var cond = CollectRestOfLine();
        var ifLike = new PpIfLike(kind, cond, ParseGroupUntil(PpTokenKind.Elif, PpTokenKind.Else, PpTokenKind.Endif));

        var elifs = ImmutableArray.CreateBuilder<PpElif>();
        PpElse? els = null;

        // zero or more #elif
        while (PeekDirective(PpTokenKind.Elif))
        {
            ConsumeDirective(PpTokenKind.Elif);
            var c = CollectRestOfLine();
            var body = ParseGroupUntil(PpTokenKind.Elif, PpTokenKind.Else, PpTokenKind.Endif);
            elifs.Add(new PpElif(c, body));
        }

        // optional #else
        if (PeekDirective(PpTokenKind.Else))
        {
            ConsumeDirective(PpTokenKind.Else);
            CollectRestOfLine(); // ignore anything to EOL
            var body = ParseGroupUntil(PpTokenKind.Endif);
            els = new PpElse(body);
        }

        // mandatory #endif
        ConsumeDirective(PpTokenKind.Endif);
        CollectRestOfLine();

        return new PpIfSection(ifLike, elifs.ToImmutable(), els);
    }
}
