
using System.Runtime.CompilerServices;

namespace MiniC;


// --- AST ---
public enum Attribute{ None, Import, Export }
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
public record VarDecl(TypeRefBase Type, string Name, Expr? Init, int P) : Decl(P);
public record ParamDecl(TypeRefBase Type, string Name, int P) : Decl(P);
public record FuncDef(Attribute Attribute, bool Extern, TypeRefBase RetType, string Name, List<ParamDecl> Params, CompoundStmt? Body, int P) : Decl(P);
public record TranslationUnit(List<Decl> Decls, int P) : Node(P);
public record TypedefDecl(TypeRefBase Type, string Name, int P) : Decl(P);
public record StructField(TypeRefBase Type, string Name, int P) : Node(P);

public record StructDecl(Attribute Attribute, bool Extern, string Name, string? Name2, List<StructField>? Fields, int P) : Decl(P)
{
    public override string ToString() => $"struct {Name}";
}

public abstract record TypeRefBase;

public sealed record TypeRef(bool Struct, string Name, int PointerDepth = 0) : TypeRefBase
{
    public override string ToString() => (Struct ? "struct " : "") + Name + new string('*', PointerDepth);
}

public sealed record FuncPtrTypeRef(TypeRef ReturnType, List<ParamDecl> Params, int PointerDepthToFunc = 1) : TypeRefBase
{
    public override string ToString()
        => $"{ReturnType} (*)({string.Join(", ", Params.Select(p => p.Type + " " + p.Name))})";
}

public sealed class Parser
{
    private readonly LexerReader _reader;
    private readonly List<Token> _buf = new(); // tokens we’ve fetched
    private int _idx = 0;                      // index of "current" token in _buf
    private readonly HashSet<string> _typedefs =
        new HashSet<string>(StringComparer.Ordinal) {
            "int", "char", "float", "double", "long", "void", "unsigned int", "unsigned char", "unsigned short",
            "khronos_int8_t", "khronos_uint8_t", "khronos_int16_t", "khronos_int16_t", "khronos_int32_t", "khronos_float_t",
            "khronos_int32_t", "khronos_intptr_t", "khronos_ssize_t", "khronos_int64_t", "khronos_uint64_t", "khronos_uint16_t"
        };

    private readonly HashSet<string> _structTags = new(StringComparer.Ordinal);

    public Parser(LexerReader reader) { _reader = reader; }

    // LookAhead: get token k steps ahead without consuming (LA(0) == current)
    private Token LA(int k = 0)
    {
        while (_idx + k >= _buf.Count)
            _buf.Add(_reader.NextToken());
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
            throw new Exception($"Expected {k} but found {LA(0)}");
        return Consume();
    }

    // Optional: current token (if you still want a property)
    private Token T => LA(0);

    // ---- backtracking support ----
    private int Mark() => _idx;
    private void Reset(int mark) => _idx = mark;
    
    private void SkipConst()
    {
        while (Match(TokenKind.Const)) { /* ignore for now */ }
    }

    private ParamDecl ParseParamDecl()
    {
        SkipConst();
        var t = ParseTypeRef();
        string name = "";
        if (Check(TokenKind.Identifier))
            name = Eat(TokenKind.Identifier).Lexeme;
        return new ParamDecl(t!, name, 0);
    }
    
    public TranslationUnit ParseTranslationUnit()
    {
        var decls = new List<Decl>();
        while (!Check(TokenKind.EOF))
        {
            decls.Add(ParseExternalDeclaration());
        }
        return new TranslationUnit(decls, 0);
    }

    private TypeRef? ParseTypeRef()
    {
        int mark = Mark();
        string name;
        bool isStruct = false;
        if (Match(TokenKind.Struct))
        {
            name = Eat(TokenKind.Identifier).Lexeme;
            isStruct = true;
        }
        else
        {
            bool unsigned = Match(TokenKind.Unsigned);
            if (Check(TokenKind.Identifier))
            {
                name = (unsigned ? "unsigned " : "") + Consume().Lexeme;
                if (!(_typedefs.Contains(name) || _structTags.Contains(name)))
                {
                    Reset(mark);
                    return null;
                }
            }
            else
            {
                Reset(mark);
                return null;
            }
        }

        int stars = 0;
        while (Match(TokenKind.Star)) stars++;

        return new TypeRef(isStruct, name, stars);
    }

    private TypedefDecl ParseTypedefDecl()
    {
        Eat(TokenKind.Typedef);

        SkipConst();
        var retType = ParseTypeRef()!;

        // Detect function-pointer declarator:  ( * Name ) ( params )
        if (Check(TokenKind.LParen) && LA(1).Kind == TokenKind.Star)
        {
            Eat(TokenKind.LParen);
            int ptrToFunc = 0;
            while (Match(TokenKind.Star)) ptrToFunc++;       // usually 1

            string typedefName = Eat(TokenKind.Identifier).Lexeme;
            Eat(TokenKind.RParen);

            Eat(TokenKind.LParen);
            var ps = new List<ParamDecl>();
            if (!Check(TokenKind.RParen))
            {
                do
                {
                    ps.Add(ParseParamDecl());
                } while (Match(TokenKind.Comma));
            }
            Eat(TokenKind.RParen);
            Eat(TokenKind.Semicolon);

            var fptr = new FuncPtrTypeRef(retType, ps, ptrToFunc);
            return new TypedefDecl(fptr, typedefName, 0);
        }

        string newName = Eat(TokenKind.Identifier).Lexeme;
        Eat(TokenKind.Semicolon);
        _typedefs.Add(newName);
        return new TypedefDecl(retType, newName, 0);
    }

    private Attribute ParseAttribute()
    {
        var attribute = Attribute.None;
        if (Match(TokenKind.Attribute))
        {
            Eat(TokenKind.LParen);
            Eat(TokenKind.LParen);
            if (Check(TokenKind.Identifier))
            {
                var name = Consume().Lexeme;
                if (name == "dllexport")
                {
                    attribute = Attribute.Export;
                }
                else if (name == "dllimport")
                {
                    attribute = Attribute.Import;
                }
            }
            Eat(TokenKind.RParen);
            Eat(TokenKind.RParen);
        }
        return attribute;
    }

    private FuncDef? ParseFuncDef(Attribute attribute, bool isExtern)
    {
        var type = ParseTypeRef();
        if (type == null) return null;
        if (!Check(TokenKind.Identifier)) return null;
        string name = Consume().Lexeme;

        if (Match(TokenKind.LParen))
        {
            // function
            var ps = new List<ParamDecl>();
            if (!Check(TokenKind.RParen))
            {
                if (!(LA(0).Kind == TokenKind.Identifier && LA(0).Lexeme == "void"))
                {
                    do
                    {
                        ps.Add(ParseParamDecl());
                    } while (Match(TokenKind.Comma));
                }
                else
                {
                    Consume();
                }
            }
            Eat(TokenKind.RParen);
            CompoundStmt? body = null;
            if (!Check(TokenKind.Semicolon))
            {
                body = ParseCompoundStmt();
            }
            else
            {
                Consume();
            }
            return new FuncDef(attribute, isExtern, type!, name, ps, body, 0);
        }
        return null;
    }

    private Decl ParseExternalDeclaration()
    {
        if(Check(TokenKind.Extern) && LA(1).Kind == TokenKind.String && (LA(1).Unquote()=="C" || LA(1).Unquote() == "C++"))
        {
            Consume();
            Consume();
            if (Match(TokenKind.LBrace))
            {
                while(!Match(TokenKind.RBrace))
                {
                    ParseExternalDeclaration();
                }
            }
        }

        var attribute = ParseAttribute();
        var isExtern = Match(TokenKind.Extern);

        if (Check(TokenKind.Typedef))
        {
            return ParseTypedefDecl();
        }

        // 'struct Tag;' (opaque/forward)
        if (Check(TokenKind.Struct) && LA(1).Kind == TokenKind.Identifier)
        {
            Eat(TokenKind.Struct);
            string name = Eat(TokenKind.Identifier).Lexeme;
            string? name2 = null;
            if (Check(TokenKind.Identifier))
            {
                name2 = Consume().Lexeme;
            }
            // struct Tag;
            if (Match(TokenKind.Semicolon))
            {
                _structTags.Add(name);
                return new StructDecl(attribute, isExtern, name, name2, null, 0);
            }

            // struct Tag { ... };
            if (Match(TokenKind.LBrace))
            {
                var fields = new List<StructField>();

                while (!Check(TokenKind.RBrace))
                {
                    var fieldType = ParseTypeRef();
                    string fieldName = Eat(TokenKind.Identifier).Lexeme;
                    Eat(TokenKind.Semicolon);

                    fields.Add(new StructField(fieldType!, fieldName, 0));
                }

                Eat(TokenKind.RBrace);
                Eat(TokenKind.Semicolon);

                _structTags.Add(name); // allow use of 'struct tag' later
                return new StructDecl(attribute, isExtern, name, name2, fields, 0);
            }
        }

        {
            var mark = Mark();
            var func = ParseFuncDef(attribute, isExtern);
            if (func != null)
            {
                return func;
            }
            else
            {
                Reset(mark);
                var type = ParseTypeRef();
                string name = Eat(TokenKind.Identifier).Lexeme;

                var decls = new List<VarDecl>();
                Expr? init = null;
                if (Match(TokenKind.Assign))
                    init = ParseAssignment();
                decls.Add(new VarDecl(type!, name, init, 0));
                Eat(TokenKind.Semicolon);
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
                string n = Eat(TokenKind.Identifier).Lexeme;
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
            if (type != null)
            {
                string n = Eat(TokenKind.Identifier).Lexeme;
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
            return new StringExpr(t.Unquote(), t.Start);
        }
        if (Check(TokenKind.IntLiteral))
        {
            var t = Consume();
            return new IntegerExpr(int.Parse(t.Lexeme), t.Start);
        }
        if (Check(TokenKind.FloatLiteral))
        {
            var t = Consume();

            string text = t.Lexeme.EndsWith("f", StringComparison.OrdinalIgnoreCase)
                ? t.Lexeme[..^1]
                : t.Lexeme;

            if (!double.TryParse(
                text,
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture,
                out double val))
                throw new Exception($"Invalid float literal '{t.Lexeme}' at {t.Start}");

            return new FloatExpr(val, t.Start);
        }
        if (Check(TokenKind.Identifier))
        {
            var t = Consume();
            return new IdentExpr(t.Lexeme, t.Start);
        }
        if (Match(TokenKind.LParen))
        {
            var e = ParseExpression();
            Eat(TokenKind.RParen);
            return e;
        }
        throw new Exception($"Primary expression expected at {LA(0).Start}:{LA(0).Lexeme}");
    }
}
