namespace MiniC
{
    // --- Lexing ---

    public enum TokenKind
    {
        EOF, Identifier, Integer,
        Int, Char, Return, If, Else, While,
        LParen, RParen, LBrace, RBrace, Comma, Semicolon,
        Plus, Minus, Star, Slash, Bang,
        Amp, // '&'
        Assign, // '='
        Eq, Neq, Lt, Gt, Le, Ge,
        AndAnd, OrOr, For,
        PlusPlus, MinusMinus,
        Break, Continue,
    }

    public readonly record struct Token(TokenKind Kind, string Lexeme, int Pos);

    public sealed class Lexer
    {
        private readonly string _src;
        private int _i;

        public Lexer(string src) { _src = src; }
        private char Peek(int k = 0) => _i + k < _src.Length ? _src[_i + k] : '\0';
        private char Next() => _i < _src.Length ? _src[_i++] : '\0';
        private bool Match(char c) { if (Peek() == c) { _i++; return true; } return false; }

        public Token NextToken()
        {
            // skip whitespace & // comments (simple)
            for (;;)
            {
                while (char.IsWhiteSpace(Peek())) _i++;
                if (Peek() == '/' && Peek(1) == '/')
                { while (Peek() != '\n' && Peek() != '\0') _i++; continue; }
                break;
            }
            int start = _i;
            char c = Next();
            switch (c)
            {
                case '\0': return new(TokenKind.EOF, "", start);
                case '(': return new(TokenKind.LParen, "(", start);
                case ')': return new(TokenKind.RParen, ")", start);
                case '{': return new(TokenKind.LBrace, "{", start);
                case '}': return new(TokenKind.RBrace, "}", start);
                case ',': return new(TokenKind.Comma, ",", start);
                case ';': return new(TokenKind.Semicolon, ";", start);
                case '+':
                    if (Match('+')) return new(TokenKind.PlusPlus, "++", start);
                        return new(TokenKind.Plus, "+", start);
                case '-':
                    if (Match('-')) return new(TokenKind.MinusMinus, "--", start);
                        return new(TokenKind.Minus, "-", start);
                case '*': return new(TokenKind.Star, "*", start);
                case '/': return new(TokenKind.Slash, "/", start);
                case '!': return Match('=') ? new(TokenKind.Neq, "!=", start) : new(TokenKind.Bang, "!", start);
                case '&': return Match('&') ? new(TokenKind.AndAnd, "&&", start) : new(TokenKind.Amp, "&", start);
                case '|': if (Match('|')) return new(TokenKind.OrOr, "||", start); break;
                case '=': return Match('=') ? new(TokenKind.Eq, "==", start) : new(TokenKind.Assign, "=", start);
                case '<': return Match('=') ? new(TokenKind.Le, "<=", start) : new(TokenKind.Lt, "<", start);
                case '>': return Match('=') ? new(TokenKind.Ge, ">=", start) : new(TokenKind.Gt, ">", start);
                
            }
            if (char.IsDigit(c))
            {
                while (char.IsDigit(Peek())) Next();
                string lex = _src.Substring(start, _i - start);
                return new(TokenKind.Integer, lex, start);
            }
            if (char.IsLetter(c) || c == '_')
            {
                while (char.IsLetterOrDigit(Peek()) || Peek() == '_') Next();
                string id = _src.Substring(start, _i - start);
                return id switch
                {
                    "int" => new(TokenKind.Int, id, start),
                    "char" => new(TokenKind.Char, id, start),
                    "return" => new(TokenKind.Return, id, start),
                    "if" => new(TokenKind.If, id, start),
                    "else" => new(TokenKind.Else, id, start),
                    "while" => new(TokenKind.While, id, start),
                    "for" => new(TokenKind.For, id, start),
                    "break" => new(TokenKind.Break, id, start),     // <—
                    "continue" => new(TokenKind.Continue, id, start),  // <—
                    _ => new(TokenKind.Identifier, id, start)
                };

            }
            throw new Exception($"Unexpected character '{c}' at {start}");
        }
    }

    // --- AST ---

    public abstract record Node(int Pos);

    public abstract record Expr(int Pos) : Node(Pos);
    public record IntegerExpr(int Value, int P) : Expr(P);
    public record IdentExpr(string Name, int P) : Expr(P);
    public record UnaryExpr(string Op, Expr Expr, int P) : Expr(P);
    public record BinaryExpr(string Op, Expr Left, Expr Right, int P) : Expr(P);
    public record AssignExpr(Expr Left, Expr Right, int P) : Expr(P);
    public record CallExpr(Expr Callee, List<Expr> Args, int P) : Expr(P);

    public abstract record Stmt(int Pos) : Node(Pos);
    public record ExprStmt(Expr? Expr, int P) : Stmt(P);
    public record ReturnStmt(Expr? Expr, int P) : Stmt(P);
    public record CompoundStmt(List<Node> Items, int P) : Stmt(P); // Items: mix of Decl and Stmt
    public record ForStmt(Decl? InitDecl, Expr? InitExpr, Expr? Cond, Expr? Post, Stmt Body, int P) : Stmt(P);
    public record IfStmt(Expr Cond, Stmt Then, Stmt? Else, int P) : Stmt(P);
    public record BreakStmt(int P)    : Stmt(P);
    public record ContinueStmt(int P) : Stmt(P);

    public abstract record Decl(int Pos) : Node(Pos);
    public record VarDecl(string TypeName, string Name, Expr? Init, int P) : Decl(P);
    public record ParamDecl(string TypeName, string Name, int P) : Decl(P);
    public record FuncDef(string RetType, string Name, List<ParamDecl> Params, CompoundStmt Body, int P) : Decl(P);
    public record TranslationUnit(List<Decl> Decls, int P) : Node(P);

    // --- Parser (recursive descent + Pratt for expressions) ---

    public sealed class Parser
    {
        private readonly Lexer _lx;
        private Token _t;

        public Parser(Lexer lx) { _lx = lx; _t = _lx.NextToken(); }

        private Token Eat(TokenKind k)
        {
            if (_t.Kind != k) throw new Exception($"Expected {k} but found {_t.Kind} at {_t.Pos}");
            var cur = _t; _t = _lx.NextToken(); return cur;
        }
        private bool Check(TokenKind k) => _t.Kind == k;
        private bool Match(TokenKind k) { if (Check(k)) { _t = _lx.NextToken(); return true; } return false; }

        public TranslationUnit ParseTranslationUnit()
        {
            var decls = new List<Decl>();
            while (!Check(TokenKind.EOF))
                decls.Add(ParseExternalDeclaration());
            return new TranslationUnit(decls, 0);
        }

        private Decl ParseExternalDeclaration()
        {
            string type = ParseTypeSpecifier();
            string name = Eat(TokenKind.Identifier).Lexeme;

            if (Match(TokenKind.LParen))
            {
                // function
                var ps = new List<ParamDecl>();
                if (!Check(TokenKind.RParen))
                {
                    do
                    {
                        string pt = ParseTypeSpecifier();
                        string pn = Eat(TokenKind.Identifier).Lexeme;
                        ps.Add(new ParamDecl(pt, pn, 0));
                    } while (Match(TokenKind.Comma));
                }
                Eat(TokenKind.RParen);
                var body = ParseCompoundStmt();
                return new FuncDef(type, name, ps, body, 0);
            }
            else
            {
                // global var decl (possibly with init); allow comma list
                var decls = new List<VarDecl>();
                Expr? init = null;
                if (Match(TokenKind.Assign))
                    init = ParseAssignment();
                decls.Add(new VarDecl(type, name, init, 0));
                while (Match(TokenKind.Comma))
                {
                    string n = Eat(TokenKind.Identifier).Lexeme;
                    Expr? i = null;
                    if (Match(TokenKind.Assign)) i = ParseAssignment();
                    decls.Add(new VarDecl(type, n, i, 0));
                }
                Eat(TokenKind.Semicolon);
                // For simplicity return just the first; in practice, split into separate Decl entries.
                return decls[0];
            }
        }

        private string ParseTypeSpecifier()
        {
            if (Match(TokenKind.Int)) return "int";
            if (Match(TokenKind.Char)) return "char";
            throw new Exception($"Type specifier expected at {_t.Pos}");
        }

        private IfStmt ParseIfStatement()
        {
            Eat(TokenKind.LParen);
            var cond = ParseExpression();
            Eat(TokenKind.RParen);

            var thenStmt = ParseStatement();

            Stmt? elseStmt = null;
            if (Match(TokenKind.Else))
                elseStmt = ParseStatement();

            return new IfStmt(cond, thenStmt, elseStmt, 0);
        }

        private ForStmt ParseForStatement()
        {
            Eat(TokenKind.LParen);

            // init: declaration | expression | empty
            Decl? initDecl = null;
            Expr? initExpr = null;

            if (!Check(TokenKind.Semicolon))
            {
                if (Check(TokenKind.Int) || Check(TokenKind.Char))
                {
                    // parse a simple 'type ident (= expr)? ;' declaration as init
                    string t = ParseTypeSpecifier();
                    string n = Eat(TokenKind.Identifier).Lexeme;
                    Expr? init = null;
                    if (Match(TokenKind.Assign)) init = ParseAssignment();
                    initDecl = new VarDecl(t, n, init, 0);
                    Eat(TokenKind.Semicolon);
                }
                else
                {
                    initExpr = ParseExpression();
                    Eat(TokenKind.Semicolon);
                }
            }
            else
            {
                Eat(TokenKind.Semicolon); // empty init
            }

            // cond: expression? ;
            Expr? cond = null;
            if (!Check(TokenKind.Semicolon)) cond = ParseExpression();
            Eat(TokenKind.Semicolon);

            // post: expression? )
            Expr? post = null;
            if (!Check(TokenKind.RParen)) post = ParseExpression();
            Eat(TokenKind.RParen);

            // body: statement (either single or compound)
            var body = ParseStatement();

            return new ForStmt(initDecl, initExpr, cond, post, body, 0);
        }

        private CompoundStmt ParseCompoundStmt()
        {
            Eat(TokenKind.LBrace);
            var items = new List<Node>();
            while (!Check(TokenKind.RBrace))
            {
                // Lookahead: type → declaration; else statement
                if (Check(TokenKind.Int) || Check(TokenKind.Char))
                {
                    string t = ParseTypeSpecifier();
                    string n = Eat(TokenKind.Identifier).Lexeme;
                    Expr? init = null;
                    if (Match(TokenKind.Assign)) init = ParseAssignment();
                    Eat(TokenKind.Semicolon);
                    items.Add(new VarDecl(t, n, init, 0));
                }
                else
                {
                    items.Add(ParseStatement());
                }
            }
            Eat(TokenKind.RBrace);
            return new CompoundStmt(items, 0);
        }

        private Stmt ParseStatement()
        {
            if (Match(TokenKind.Return))
            {
                Expr? e = null;
                if (!Check(TokenKind.Semicolon)) e = ParseExpression();
                Eat(TokenKind.Semicolon);
                return new ReturnStmt(e, 0);
            }
            if (Match(TokenKind.If))
                return ParseIfStatement();
            if (Match(TokenKind.For))
                return ParseForStatement();
            if (Match(TokenKind.Break))
            {
                Eat(TokenKind.Semicolon);
                return new BreakStmt(0);
            }

            if (Match(TokenKind.Continue))
            {
                Eat(TokenKind.Semicolon);
                return new ContinueStmt(0);
            }

            if (Check(TokenKind.LBrace)) return ParseCompoundStmt();
            // extend with if/while later
            // default: expr stmt
            Expr? expr = null;
            if (!Check(TokenKind.Semicolon)) expr = ParseExpression();
            Eat(TokenKind.Semicolon);
            return new ExprStmt(expr, 0);
        }

        public Expr ParseExpression() => ParseAssignment();

        private Expr ParseAssignment()
        {
            var lhs = ParseBinary(0);
            if (Match(TokenKind.Assign))
            {
                var rhs = ParseAssignment();
                return new AssignExpr(lhs, rhs, 0);
            }
            return lhs;
        }

        // Pratt parser for binary operators
        private static readonly Dictionary<TokenKind, (int bp, string op, bool rightAssoc)> infix = new()
        {
            { TokenKind.OrOr,  (1, "||", false) },
            { TokenKind.AndAnd,(2, "&&", false) },
            { TokenKind.Eq,    (3, "==", false) },
            { TokenKind.Neq,   (3, "!=", false) },
            { TokenKind.Lt,    (4, "<",  false) },
            { TokenKind.Gt,    (4, ">",  false) },
            { TokenKind.Le,    (4, "<=", false) },
            { TokenKind.Ge,    (4, ">=", false) },
            { TokenKind.Plus,  (5, "+",  false) },
            { TokenKind.Minus, (5, "-",  false) },
            { TokenKind.Star,  (6, "*",  false) },
            { TokenKind.Slash, (6, "/",  false) },
        };

        private Expr ParseBinary(int minBp)
        {
            var left = ParseUnary();

            while (infix.TryGetValue(_t.Kind, out var info) && info.bp >= minBp)
            {
                var (bp, op, rightAssoc) = info;
                var opTok = _t; _t = _lx.NextToken();
                var nextMin = rightAssoc ? bp : bp + 1;
                var right = ParseBinary(nextMin);
                left = new BinaryExpr(op, left, right, opTok.Pos);
            }
            return left;
        }

        private Expr ParseUnary()
        {
            if (Match(TokenKind.PlusPlus))
                return new UnaryExpr("++pre", ParseUnary(), 0);
            if (Match(TokenKind.MinusMinus))
                return new UnaryExpr("--pre", ParseUnary(), 0);
            if (Match(TokenKind.Plus))  return new UnaryExpr("+", ParseUnary(), 0);
            if (Match(TokenKind.Minus)) return new UnaryExpr("-", ParseUnary(), 0);
            if (Match(TokenKind.Bang))  return new UnaryExpr("!", ParseUnary(), 0);
            if (Match(TokenKind.Amp))   return new UnaryExpr("&", ParseUnary(), 0);
            if (Match(TokenKind.Star))  return new UnaryExpr("*", ParseUnary(), 0);
            return ParsePostfix();
        }

        private Expr ParsePostfix()
        {
            Expr expr = ParsePrimary();

            while (true)
            {
                if (Match(TokenKind.LParen))
                {
                    var args = new List<Expr>();
                    if (!Check(TokenKind.RParen))
                    {
                        do { args.Add(ParseExpression()); } while (Match(TokenKind.Comma));
                    }
                    Eat(TokenKind.RParen);
                    expr = new CallExpr(expr, args, 0);
                    continue;
                }

                if (Match(TokenKind.PlusPlus))
                {
                    expr = new UnaryExpr("++post", expr, 0);
                    continue;
                }
                if (Match(TokenKind.MinusMinus))
                {
                    expr = new UnaryExpr("--post", expr, 0);
                    continue;
                }

                break;
            }
            return expr;
        }

        private Expr ParsePrimary()
        {
            if (Check(TokenKind.Integer))
            {
                var t = _t; _t = _lx.NextToken();
                return new IntegerExpr(int.Parse(t.Lexeme), t.Pos);
            }
            if (Check(TokenKind.Identifier))
            {
                var t = _t; _t = _lx.NextToken();
                return new IdentExpr(t.Lexeme, t.Pos);
            }
            if (Match(TokenKind.LParen))
            {
                var e = ParseExpression();
                Eat(TokenKind.RParen);
                return e;
            }
            throw new Exception($"Primary expression expected at {_t.Pos}:{_t.Lexeme}");
        }
    }
}