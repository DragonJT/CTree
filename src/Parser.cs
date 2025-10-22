
namespace MiniC
{
    // --- AST ---

    public abstract record Node(int Pos);

    public abstract record Expr(int Pos) : Node(Pos);
    public record IntegerExpr(long Value, int P) : Expr(P);
    public record IdentExpr(string Name, int P) : Expr(P);
    public record UnaryExpr(string Op, Expr Expr, int P) : Expr(P);
    public record BinaryExpr(string Op, Expr Left, Expr Right, int P) : Expr(P);
    public record AssignExpr(Expr Left, Expr Right, int P) : Expr(P);
    public record CallExpr(Expr Callee, List<Expr> Args, int P) : Expr(P);
    public record FloatExpr(double Value, int P) : Expr(P);
    public record StringExpr(string Value, int P) : Expr(P);
    public record NullExpr(int P) : Expr(P);

    public abstract record Stmt(int Pos) : Node(Pos);
    public record ExprStmt(Expr? Expr, int P) : Stmt(P);
    public record ReturnStmt(Expr? Expr, int P) : Stmt(P);
    public record CompoundStmt(List<Node> Items, int P) : Stmt(P); // Items: mix of Decl and Stmt
    public record ForStmt(Decl? InitDecl, Expr? InitExpr, Expr? Cond, Expr? Post, Stmt Body, int P) : Stmt(P);
    public record IfStmt(Expr Cond, Stmt Then, Stmt? Else, int P) : Stmt(P);
    public record BreakStmt(int P)    : Stmt(P);
    public record ContinueStmt(int P) : Stmt(P);
    public record WhileStmt(Expr Cond, Stmt Body, int P) : Stmt(P);

    public abstract record Decl(int Pos) : Node(Pos);
    public record VarDecl(TypeRef Type, string Name, Expr? Init, int P) : Decl(P);
    public record ParamDecl(TypeRef Type, string Name, int P) : Decl(P);
    public record FuncDef(TypeRef RetType, string Name, List<ParamDecl> Params, CompoundStmt Body, int P) : Decl(P);
    public record TranslationUnit(List<Decl> Decls, int P) : Node(P);
    public record TypedefDecl(TypeRef Type, string Name, int P) : Decl(P);
    public record OpaqueStructDecl(string Tag, int P) : Decl(P);

    public record ExternFuncDecl(string DllName, TypeRef RetType, string Name,
        List<ParamDecl> Params, string? EntryPoint, string? CallConv, int P) : Decl(P);

    public sealed record TypeRef(bool Struct, string Name, int PointerDepth = 0)
    {
        public override string ToString() => Struct?"struct ":"" + Name + new string('*', PointerDepth);
    }

    public sealed class Parser
    {
        private readonly Lexer _lx;
        private readonly List<Token> _buf = new(); // tokens we’ve fetched
        private int _idx = 0;                      // index of "current" token in _buf
        private readonly HashSet<string> _typedefs =
            new HashSet<string>(StringComparer.Ordinal) { "int", "char", "float", "double", "long", "void" };

        private readonly HashSet<string> _structTags = new(StringComparer.Ordinal);

        // LookAhead: get token k steps ahead without consuming (LA(0) == current)
        private Token LA(int k = 0)
        {
            // ensure buffer has at least up to _idx + k
            while (_idx + k >= _buf.Count)
                _buf.Add(_lx.NextToken());
            return _buf[_idx + k];
        }

        // Consume: return current token and advance
        private Token Consume()
        {
            var t = LA(0);
            _idx++;
            return t;
        }

        // Check: is current token kind == k ?
        private bool Check(TokenKind k) => LA(0).Kind == k;

        // Match: if current token kind == k, consume and return true; else false
        private bool Match(TokenKind k)
        {
            if (Check(k)) { Consume(); return true; }
            return false;
        }

        // Eat: require token kind == k, else throw; returns consumed token
        private Token Eat(TokenKind k)
        {
            if (!Check(k))
                throw new Exception($"Expected {k} but found {LA(0).Kind} at {LA(0).Start}");
            return Consume();
        }

        // Optional: current token (if you still want a property)
        private Token T => LA(0);

        // ---- backtracking support ----
        private int Mark() => _idx;
        private void Reset(int mark) => _idx = mark;

        public Parser(Lexer lx) { _lx = lx; LA(0); }

        public TranslationUnit ParseTranslationUnit()
        {
            var decls = new List<Decl>();
            while (!Check(TokenKind.EOF))
                decls.Add(ParseExternalDeclaration());
            return new TranslationUnit(decls, 0);
        }

        private TypeRef? ParseTypeRef()
        {
            string baseName;
            bool hasStruct = false;
            if (Match(TokenKind.Struct))
            {
                // struct <Identifier>
                baseName = Eat(TokenKind.Identifier).Lexeme(_lx);
                hasStruct = true;
            }
            else
            {
                if (Check(TokenKind.Identifier) && (_typedefs.Contains(LA(0).Lexeme(_lx)) || _structTags.Contains(LA(0).Lexeme(_lx))))
                {
                    baseName = Consume().Lexeme(_lx);
                }
                else
                {
                    return null;
                }
            }

            int stars = 0;
            while (Match(TokenKind.Star)) stars++;

            return new TypeRef(hasStruct, baseName, stars);
        }

        private TypedefDecl ParseTypedefDecl()
        {
            Eat(TokenKind.Typedef);
            var type = ParseTypeRef();
            string newName = Eat(TokenKind.Identifier).Lexeme(_lx);
            Eat(TokenKind.Semicolon);

            _typedefs.Add(newName);

            return new TypedefDecl(type!, newName, 0);
        }

        private Decl ParseExternalDeclaration()
        {
            if (Match(TokenKind.Extern))
            {
                string? callConv = null;

                // Optional: extern ( stdcall )
                if (Match(TokenKind.LParen))
                {
                    if (Check(TokenKind.Identifier))
                        callConv = Eat(TokenKind.Identifier).Lexeme(_lx).ToLowerInvariant();
                    Eat(TokenKind.RParen);
                }

                // extern "mylib.dll"
                string dll = Eat(TokenKind.String).Lexeme(_lx)[1..^1];

                var ret = ParseTypeRef();
                string name = Eat(TokenKind.Identifier).Lexeme(_lx);

                Eat(TokenKind.LParen);
                var ps = new List<ParamDecl>();
                if (!Check(TokenKind.RParen))
                {
                    do
                    {
                        var pt = ParseTypeRef();
                        string pn = Eat(TokenKind.Identifier).Lexeme(_lx);
                        ps.Add(new ParamDecl(pt!, pn, 0));
                    } while (Match(TokenKind.Comma));

                }
                Eat(TokenKind.RParen);
                Eat(TokenKind.Semicolon);
                return new ExternFuncDecl(dll, ret!, name, ps, null, callConv, 0);
            }
            if (Check(TokenKind.Typedef))
            {
                return ParseTypedefDecl();
            }

            // 'struct Tag;' (opaque/forward)
            if (Check(TokenKind.Struct) && LA(1).Kind == TokenKind.Identifier && LA(2).Kind == TokenKind.Semicolon)
            {
                Eat(TokenKind.Struct);
                string tag = Eat(TokenKind.Identifier).Lexeme(_lx);
                Eat(TokenKind.Semicolon);
                _structTags.Add(tag);
                return new OpaqueStructDecl(tag, 0);
            }

            {
                var type = ParseTypeRef();
                string name = Eat(TokenKind.Identifier).Lexeme(_lx);

                if (Match(TokenKind.LParen))
                {
                    // function
                    var ps = new List<ParamDecl>();
                    if (!Check(TokenKind.RParen))
                    {
                        do
                        {
                            var pt = ParseTypeRef();
                            string pn = Eat(TokenKind.Identifier).Lexeme(_lx);
                            ps.Add(new ParamDecl(pt!, pn, 0));
                        } while (Match(TokenKind.Comma));
                    }
                    Eat(TokenKind.RParen);
                    var body = ParseCompoundStmt();
                    return new FuncDef(type!, name, ps, body, 0);
                }
                else
                {
                    // global var decl (possibly with init); allow comma list
                    var decls = new List<VarDecl>();
                    Expr? init = null;
                    if (Match(TokenKind.Assign))
                        init = ParseAssignment();
                    decls.Add(new VarDecl(type!, name, init, 0));
                    while (Match(TokenKind.Comma))
                    {
                        string n = Eat(TokenKind.Identifier).Lexeme(_lx);
                        Expr? i = null;
                        if (Match(TokenKind.Assign)) i = ParseAssignment();
                        decls.Add(new VarDecl(type!, n, i, 0));
                    }
                    Eat(TokenKind.Semicolon);
                    // For simplicity return just the first; in practice, split into separate Decl entries.
                    return decls[0];
                }
            }
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

        private Stmt ParseWhileStatement()
        {
            Eat(TokenKind.LParen);
            var cond = ParseExpression();
            Eat(TokenKind.RParen);
            var body = ParseStatement();
            return new WhileStmt(cond, body, cond.Pos);
        }

        private ForStmt ParseForStatement()
        {
            Eat(TokenKind.LParen);

            // init: declaration | expression | empty
            Decl? initDecl = null;
            Expr? initExpr = null;

            if (!Check(TokenKind.Semicolon))
            {
                int mark = Mark();
                var type = ParseTypeRef();
                if (type != null)
                {
                    string n = Eat(TokenKind.Identifier).Lexeme(_lx);
                    Expr? init = null;
                    if (Match(TokenKind.Assign)) init = ParseAssignment();
                    initDecl = new VarDecl(type, n, init, 0);
                    Eat(TokenKind.Semicolon);
                }
                else
                {
                    Reset(mark);
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
                int mark = Mark();
                var type = ParseTypeRef();
                if (type!=null)
                {
                    string n = Eat(TokenKind.Identifier).Lexeme(_lx);
                    Expr? init = null;
                    if (Match(TokenKind.Assign)) init = ParseAssignment();
                    Eat(TokenKind.Semicolon);
                    items.Add(new VarDecl(type, n, init, 0));
                    continue;
                }
                Reset(mark);
                items.Add(ParseStatement());
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
            if (Match(TokenKind.While))
                return ParseWhileStatement();
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

            while (infix.TryGetValue(LA(0).Kind, out var info) && info.bp >= minBp)
            {
                var (bp, op, rightAssoc) = info;
                var opTok = Consume();
                var nextMin = rightAssoc ? bp : bp + 1;
                var right = ParseBinary(nextMin);
                left = new BinaryExpr(op, left, right, opTok.Start);
            }
            return left;
        }

        private Expr ParseUnary()
        {
            if (Match(TokenKind.PlusPlus))
                return new UnaryExpr("++pre", ParseUnary(), 0);
            if (Match(TokenKind.MinusMinus))
                return new UnaryExpr("--pre", ParseUnary(), 0);
            if (Match(TokenKind.Plus)) return new UnaryExpr("+", ParseUnary(), 0);
            if (Match(TokenKind.Minus)) return new UnaryExpr("-", ParseUnary(), 0);
            if (Match(TokenKind.Bang)) return new UnaryExpr("!", ParseUnary(), 0);
            if (Match(TokenKind.Amp)) return new UnaryExpr("&", ParseUnary(), 0);
            if (Match(TokenKind.Star)) return new UnaryExpr("*", ParseUnary(), 0);
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
            if (Check(TokenKind.Null))
            {
                return new NullExpr(Consume().Start);
            }
            if (Check(TokenKind.String))
            {
                var t = Consume();
                return new StringExpr(t.Lexeme(_lx)[1..^1], t.Start);
            }
            if (Check(TokenKind.IntLiteral))
            {
                var t = Consume();
                return new IntegerExpr(int.Parse(t.Lexeme(_lx)), t.Start);
            }
            if (Check(TokenKind.FloatLiteral))
            {
                var t = Consume();

                string text = t.Lexeme(_lx).EndsWith("f", StringComparison.OrdinalIgnoreCase)
                    ? t.Lexeme(_lx)[..^1]
                    : t.Lexeme(_lx);

                if (!double.TryParse(
                    text,
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out double val))
                    throw new Exception($"Invalid float literal '{t.Lexeme(_lx)}' at {t.Start}");

                return new FloatExpr(val, t.Start);
            }
            if (Check(TokenKind.Identifier))
            {
                var t = Consume();
                return new IdentExpr(t.Lexeme(_lx), t.Start);
            }
            if (Match(TokenKind.LParen))
            {
                var e = ParseExpression();
                Eat(TokenKind.RParen);
                return e;
            }
            throw new Exception($"Primary expression expected at {LA(0).Start}:{LA(0).Lexeme(_lx)}");
        }
    }
}