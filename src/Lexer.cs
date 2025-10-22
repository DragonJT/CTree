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
    Typedef, Struct, DirectiveHash,
}

public enum TriviaKind { Space, Newline, LineComment, BlockComment }

public readonly struct Trivia
{
    public TriviaKind Kind { get; }
    public int Start { get; }     // offset into the source
    public int Length { get; }    // zero-copy slice, no string allocation
    public Trivia(TriviaKind k, int start, int length) { Kind = k; Start = start; Length = length; }
}

public readonly struct Token
{
    public TokenKind Kind { get; }
    public int Start { get; }
    public int Length { get; }
    public ReadOnlyMemory<Trivia> Leading { get; }
    public string Lexeme(Lexer lexer) => lexer._src[Start..(Start + Length)];

    public Token(TokenKind kind, int start, int length, ReadOnlyMemory<Trivia> leading)
    { Kind = kind; Start = start; Length = length; Leading = leading; }
}

public sealed class Lexer
{
    public readonly string _src;
    private int _i;
    private bool _atBol = true; //beginning of line

    public Lexer(string src) { _src = src; }
    private char Peek(int k = 0) => _i + k < _src.Length ? _src[_i + k] : '\0';
    private char Next() => _i < _src.Length ? _src[_i++] : '\0';
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
            return new(TokenKind.Dot, start, 1, leading.ToArray());
        }

        // classify
        if (hasDot || hasExp || hasSuffix || startedWithDot)
            return new(TokenKind.FloatLiteral, start, _i - start, leading.ToArray());

        return new(TokenKind.IntLiteral, start, _i-start, leading.ToArray());
    }

    public Token NextToken()
    {
        var leading = new List<Trivia>(capacity: 2);
        CollectLeadingTrivia(leading);
        int start = _i;

        char c = Next();
        if (c == '\0') return new(TokenKind.EOF, start, 0, leading.ToArray());

        // Detect directives at BOL:  #include, #define, ...
        if (_atBol)
        {
            // Skip spaces in leading trivia ⇒ next real char determines if directive
            // Because we already consumed trivia, `c` is the first non-trivia char.
            if (c == '#')
            {
                _atBol = false;
                return new(TokenKind.DirectiveHash, start, 1, leading.ToArray());
            }
        }

        _atBol = false;

        switch (c)
        {
            case '(': return new(TokenKind.LParen, start, 1, leading.ToArray());
            case ')': return new(TokenKind.RParen, start, 1, leading.ToArray());
            case '{': return new(TokenKind.LBrace, start, 1, leading.ToArray());
            case '}': return new(TokenKind.RBrace, start, 1, leading.ToArray());
            case ',': return new(TokenKind.Comma, start, 1, leading.ToArray());
            case ';': return new(TokenKind.Semicolon, start, 1, leading.ToArray());
            case '+':
                if (Match('+')) return new(TokenKind.PlusPlus, start, 2, leading.ToArray());
                return new(TokenKind.Plus, start, 1, leading.ToArray());
            case '-':
                if (Match('-')) return new(TokenKind.MinusMinus, start, 2, leading.ToArray());
                return new(TokenKind.Minus, start, 1, leading.ToArray());
            case '*': return new(TokenKind.Star, start, 1, leading.ToArray());
            case '/': return new(TokenKind.Slash, start, 1, leading.ToArray());
            case '!': return Match('=') ? new(TokenKind.Neq, start, 2, leading.ToArray()) : new(TokenKind.Bang, start, 1, leading.ToArray());
            case '&': return Match('&') ? new(TokenKind.AndAnd, start, 2, leading.ToArray()) : new(TokenKind.Amp, start, 1, leading.ToArray());
            case '|': if (Match('|')) return new(TokenKind.OrOr, start, 2, leading.ToArray()); break;
            case '=': return Match('=') ? new(TokenKind.Eq, start, 2, leading.ToArray()) : new(TokenKind.Assign, start, 1, leading.ToArray());
            case '<': return Match('=') ? new(TokenKind.Le, start, 2, leading.ToArray()) : new(TokenKind.Lt, start, 1, leading.ToArray());
            case '>': return Match('=') ? new(TokenKind.Ge, start, 2, leading.ToArray()) : new(TokenKind.Gt, start, 1, leading.ToArray());
            case '"':
                while (Peek() != '"' && Peek() != '\0') Next(); // (you can enhance later)
                if (Peek() == '\0') throw new Exception("Unterminated string");
                Next();
                return new(TokenKind.String, start, _i - start, leading.ToArray());
        }
        if (char.IsDigit(c) || c == '.')
        {
            return ScanNumber(start, c == '.', leading);
        }
        if (char.IsLetter(c) || c == '_')
        {
            while (char.IsLetterOrDigit(Peek()) || Peek() == '_') Next();
            string id = _src.Substring(start, _i - start);
            return id switch
            {
                "extern" => new(TokenKind.Extern, start, id.Length, leading.ToArray()),
                "return" => new(TokenKind.Return, start, id.Length, leading.ToArray()),
                "if" => new(TokenKind.If, start, id.Length, leading.ToArray()),
                "else" => new(TokenKind.Else, start, id.Length, leading.ToArray()),
                "while" => new(TokenKind.While, start, id.Length, leading.ToArray()),
                "for" => new(TokenKind.For, start, id.Length, leading.ToArray()),
                "break" => new(TokenKind.Break, start, id.Length, leading.ToArray()),
                "continue" => new(TokenKind.Continue, start, id.Length, leading.ToArray()),
                "NULL" => new(TokenKind.Null, start, id.Length, leading.ToArray()),
                "typedef" => new(TokenKind.Typedef, start, id.Length, leading.ToArray()),
                "struct" => new(TokenKind.Struct, start, id.Length, leading.ToArray()),
                _ => new(TokenKind.Identifier, start, id.Length, leading.ToArray()),
            };

        }

        throw new Exception($"Unexpected character '{c}' at {start}");
    }
}