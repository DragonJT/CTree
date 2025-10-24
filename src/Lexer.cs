using System.Text;

namespace MiniC;

public enum TokenKind
{
    EOF, Identifier, IntLiteral, FloatLiteral, DoubleLiteral, Dot,
    Return, If, Else, While,
    LParen, RParen, LBrace, RBrace, Comma, Semicolon,
    Plus, Minus, Star, Slash, Bang,
    Amp, // '&'
    Assign, // '='
    Eq, Neq, Lt, Gt, Le, Ge,
    AndAnd, OrOr, For,
    PlusPlus, MinusMinus,
    Break, Continue,
    Extern, String,
    Null,
    Const, Volatile, Restrict,
    Typedef, Struct, DirectiveHash, Attribute, Unsigned
}

public enum PpTokenKind
{
    Other, If, Else, Define, Undef, Include, Ifdef, Ifndef, Elif, Endif
}

public enum TriviaKind { Space, Newline, LineComment, BlockComment }

public readonly struct Trivia
{
    public TriviaKind Kind { get; }
    public int Start { get; }     // offset into the source
    public int Length { get; }    // zero-copy slice, no string allocation
    public Trivia(TriviaKind k, int start, int length) { Kind = k; Start = start; Length = length; }
}

public class Token
{
    public TokenKind Kind { get; }
    public PpTokenKind PpKind { get; }
    public readonly Lexer Source;
    public int Start { get; }
    public int Length { get; }
    public ReadOnlyMemory<Trivia> Leading { get; }
    public string Lexeme => Source.Src[Start..(Start + Length)];

    public (int line, int col) LineCol
    {
        get
        {
            var src = Source.Src;
            int line = 1;
            int lastLineStart = 0;
            int limit = Math.Min(Start, src.Length);

            for (int i = 0; i < limit; i++)
            {
                if (src[i] == '\n')
                {
                    line++;
                    lastLineStart = i + 1;
                }
            }

            int col = Start - lastLineStart + 1;
            return (line, col);
        }
    }

    public Token(TokenKind kind, Lexer source, int start, int length, ReadOnlyMemory<Trivia> leading, PpTokenKind ppKind)
    { Kind = kind; Source = source; Start = start; Length = length; Leading = leading; PpKind = ppKind; }

    public void ToCode(StringBuilder sb)
    {
        var span = Leading.Span;

        for (int i = 0; i < span.Length; i++)
        {
            var tr = span[i];
            sb.Append(Source.Src, tr.Start, tr.Length);
        }
        sb.Append(Lexeme);
    }

    public string Unquote()
    {
        var s = Lexeme;
        return s.Length >= 2 && s[0] == '"' && s[^1] == '"' ? s[1..^1] : s;
    }

    public override string ToString()
    {
        var (line, col) = LineCol;
        return $"({Kind}:{Lexeme}:{line}:{col})";
    }
}

public sealed class Lexer
{
    public readonly string File;
    public readonly string Src;
    private int _i;
    private bool _atBol = true; //beginning of line

    public Lexer(string file)
    {
        File = file;
        Src = System.IO.File.ReadAllText(file);
    }
    private char Peek(int k = 0) => _i + k < Src.Length ? Src[_i + k] : '\0';
    private char Next() => _i < Src.Length ? Src[_i++] : '\0';
    private bool Match(char c) { if (Peek() == c) { _i++; return true; } return false; }

    private void CollectLeadingTrivia(List<Trivia> trivia)
    {
        for (; ; )
        {
            int start = _i;
            char c = Peek();

            // newline
            if (c == '\n') { Next(); trivia.Add(new(TriviaKind.Newline, start, 1)); _atBol = true; continue; }
            if (c == '\r' && Peek(1) == '\n') { _i += 2; trivia.Add(new(TriviaKind.Newline, start, 2)); _atBol = true; continue; }

            // spaces/tabs
            if (c == ' ' || c == '\t' || c == '\f' || c == '\v')
            {
                while (Peek() is ' ' or '\t' or '\f' or '\v') Next();
                trivia.Add(new(TriviaKind.Space, start, _i - start));
                // BOL stays whatever it was (only newline flips it to true, consuming text flips it to false)
                continue;
            }

            // line comment
            if (c == '/' && Peek(1) == '/')
            {
                _i += 2; start = start + 2;
                while (Peek() != '\n' && Peek() != '\0') Next();
                trivia.Add(new(TriviaKind.LineComment, start - 2, _i - (start - 2)));
                continue;
            }

            // block comment
            if (c == '/' && Peek(1) == '*')
            {
                _i += 2;
                while (!(Peek() == '*' && Peek(1) == '/') && Peek() != '\0') Next();
                if (Peek() == '\0') throw new Exception("Unterminated block comment");
                _i += 2;
                trivia.Add(new(TriviaKind.BlockComment, start, _i - start));
                continue;
            }

            break; // no more trivia
        }
    }

    private Token ScanNumber(int start, bool startedWithDot, List<Trivia> leading)
    {
        bool haveDigits = !startedWithDot;

        if (!startedWithDot)
        {
            while (char.IsDigit(Peek())) { Next(); haveDigits = true; }
        }

        bool hasDot = false;
        if (Peek() == '.')
        {
            hasDot = true; Next();
            while (char.IsDigit(Peek())) { Next(); haveDigits = true; }
        }

        bool hasExp = false;
        if (Peek() is 'e' or 'E')
        {
            int save = _i;
            Next();
            if (Peek() is '+' or '-') Next();
            if (char.IsDigit(Peek()))
            {
                hasExp = true;
                while (char.IsDigit(Peek())) Next();
            }
            else
            {
                // rollback if it's not a real exponent
                _i = save;
            }
        }

        bool hasSuffix = false;
        if (Peek() is 'f' or 'F') { hasSuffix = true; Next(); }

        if (!haveDigits)
        {
            // not a valid number; backtrack to just the dot token
            _i = start + 1;
            return Create(TokenKind.Dot, start, 1, leading);
        }

        // classify
        if (hasDot || hasExp || hasSuffix || startedWithDot)
            return Create(TokenKind.FloatLiteral, start, _i - start, leading);

        return Create(TokenKind.IntLiteral, start, _i - start, leading);
    }
    
    Token Create(TokenKind kind, int start, int length, List<Trivia> leading, PpTokenKind ppKind = PpTokenKind.Other)
    {
        return new(kind, this, start, length, leading.ToArray(), ppKind);
    }

    public Token NextToken()
    {
        var leading = new List<Trivia>(capacity: 2);
        CollectLeadingTrivia(leading);
        int start = _i;

        char c = Next();
        if (c == '\0') return Create(TokenKind.EOF, start, 0, leading);

        // Detect directives at BOL:  #include, #define, ...
        if (_atBol)
        {
            // Skip spaces in leading trivia ⇒ next real char determines if directive
            // Because we already consumed trivia, `c` is the first non-trivia char.
            if (c == '#')
            {
                _atBol = false;
                return Create(TokenKind.DirectiveHash, start, 1, leading);
            }
        }

        _atBol = false;

        switch (c)
        {
            case '(': return Create(TokenKind.LParen, start, 1, leading);
            case ')': return Create(TokenKind.RParen, start, 1, leading);
            case '{': return Create(TokenKind.LBrace, start, 1, leading);
            case '}': return Create(TokenKind.RBrace, start, 1, leading);
            case ',': return Create(TokenKind.Comma, start, 1, leading);
            case ';': return Create(TokenKind.Semicolon, start, 1, leading);
            case '+':
                if (Match('+')) return Create(TokenKind.PlusPlus, start, 2, leading);
                return Create(TokenKind.Plus, start, 1, leading);
            case '-':
                if (Match('-')) return Create(TokenKind.MinusMinus, start, 2, leading);
                return Create(TokenKind.Minus, start, 1, leading);
            case '*': return Create(TokenKind.Star, start, 1, leading);
            case '/': return Create(TokenKind.Slash, start, 1, leading);
            case '!': return Match('=') ? Create(TokenKind.Neq, start, 2, leading) : Create(TokenKind.Bang, start, 1, leading);
            case '&': return Match('&') ? Create(TokenKind.AndAnd, start, 2, leading) : Create(TokenKind.Amp, start, 1, leading);
            case '|': if (Match('|')) return Create(TokenKind.OrOr, start, 2, leading); break;
            case '=': return Match('=') ? Create(TokenKind.Eq, start, 2, leading) : Create(TokenKind.Assign, start, 1, leading);
            case '<': return Match('=') ? Create(TokenKind.Le, start, 2, leading) : Create(TokenKind.Lt, start, 1, leading);
            case '>': return Match('=') ? Create(TokenKind.Ge, start, 2, leading) : Create(TokenKind.Gt, start, 1, leading);
            case '"':
                while (Peek() != '"' && Peek() != '\0') Next(); // (you can enhance later)
                if (Peek() == '\0') throw new Exception("Unterminated string");
                Next();
                return Create(TokenKind.String, start, _i - start, leading);
        }
        if (char.IsDigit(c) || c == '.')
        {
            return ScanNumber(start, c == '.', leading);
        }
        if (char.IsLetter(c) || c == '_')
        {
            while (char.IsLetterOrDigit(Peek()) || Peek() == '_') Next();
            string id = Src.Substring(start, _i - start);

            var kw = id switch
            {
                "extern" => TokenKind.Extern,
                "return" => TokenKind.Return,
                "if" => TokenKind.If,
                "else" => TokenKind.Else,
                "while" => TokenKind.While,
                "for" => TokenKind.For,
                "break" => TokenKind.Break,
                "continue" => TokenKind.Continue,
                "NULL" => TokenKind.Null,
                "typedef" => TokenKind.Typedef,
                "struct" => TokenKind.Struct,
                "const" => TokenKind.Const,
                "volatile" => TokenKind.Volatile,
                "restrict" => TokenKind.Restrict,
                "unsigned" => TokenKind.Unsigned,
                "__attribute__" => TokenKind.Attribute,
                _ => TokenKind.Identifier,
            };

            var ppKw = id switch
            {
                "define" => PpTokenKind.Define,
                "undef" => PpTokenKind.Undef,
                "include" => PpTokenKind.Include,
                "if" => PpTokenKind.If,
                "ifdef" => PpTokenKind.Ifdef,
                "ifndef" => PpTokenKind.Ifndef,
                "elif" => PpTokenKind.Elif,
                "else" => PpTokenKind.Else,
                "endif" => PpTokenKind.Endif,
                _ => PpTokenKind.Other,
            };
            return Create(kw, start, id.Length, leading, ppKw);
        }

        throw new Exception($"Unexpected character '{c}' at {start}");
    }
}