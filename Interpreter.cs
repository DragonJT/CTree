
namespace MiniC
{
    // -------- Runtime value --------
    // Simple int/char-only runtime for now (C-like: 0=false, nonzero=true)
    public readonly record struct Value(int Int)
    {
        public static implicit operator Value(int i) => new(i);
        public static implicit operator int(Value v) => v.Int;
        public override string ToString() => Int.ToString();
    }

    // -------- Return signaling (to unwind to function boundary) --------
    public sealed class ReturnSignal : Exception
    {
        public readonly Value? Value;
        public ReturnSignal(Value? v) { Value = v; }
    }

    // -------- Environment / call stack --------
    public sealed class Env
    {
        // One function-level frame = stack of nested lexical scopes
        private sealed class Frame
        {
            public readonly Stack<Dictionary<string, Value>> Scopes = new();
            public Frame() { Scopes.Push(new()); } // function-local root scope
        }

        private readonly Stack<Frame> _frames = new();

        public void PushFrame() => _frames.Push(new Frame());
        public void PopFrame() => _frames.Pop();

        public void PushScope()
        {
            if (_frames.Count == 0) throw new InvalidOperationException("No frame");
            _frames.Peek().Scopes.Push(new Dictionary<string, Value>());
        }

        public void PopScope()
        {
            var f = _frames.Peek();
            f.Scopes.Pop();
        }

        public void Define(string name, Value v)
        {
            var f = _frames.Peek();
            f.Scopes.Peek()[name] = v;
        }

        public bool TrySet(string name, Value v)
        {
            foreach (var scope in _frames.Peek().Scopes)
            {
                if (scope.ContainsKey(name)) { scope[name] = v; return true; }
            }
            return false;
        }

        public bool TryGet(string name, out Value v)
        {
            foreach (var scope in _frames.Peek().Scopes)
            {
                if (scope.TryGetValue(name, out v)) return true;
            }
            v = default;
            return false;
        }
    }

    // -------- Function table --------
    public sealed class FunctionTable
    {
        private readonly Dictionary<string, FuncDef> _fns = new();
        public void Add(FuncDef f)
        {
            _fns[f.Name] = f;
        }
        public FuncDef Get(string name)
        {
            if (!_fns.TryGetValue(name, out var f))
                throw new Exception($"Undefined function '{name}'");
            return f;
        }
        public bool TryGet(string name, out FuncDef f) => _fns.TryGetValue(name, out f!);
    }

    // -------- Interpreter --------
    public sealed class Interpreter
    {
        private readonly FunctionTable _fns = new();
        private readonly Env _env = new();
        sealed class BreakSignal    : Exception { }
        sealed class ContinueSignal : Exception { }
        // Optional: quick built-ins
        private readonly Dictionary<string, Func<List<Value>, Value>> _builtins =
            new(StringComparer.Ordinal)
            {
                // print(x) → prints integer and returns x
                ["print"] = args =>
                {
                    foreach (var a in args) Console.WriteLine(a.Int);
                    return args.Count > 0 ? args[^1] : new Value(0);
                }
            };

        public Value Run(TranslationUnit tu, string entry = "main", params int[] argv)
        {
            // Load functions (ignore global vars for now; easy to add later)
            foreach (var d in tu.Decls)
            {
                if (d is FuncDef f) _fns.Add(f);
                // global VarDecls would go here if you want globals
            }

            // Call main(int argc, int argv0, ...)
            var args = new List<Value> { new Value(argv.Length) };
            foreach (var a in argv) args.Add(new Value(a));

            return Call(entry, args);
        }

        private Value Call(string name, List<Value> args)
        {
            // Built-in?
            if (_builtins.TryGetValue(name, out var builtin))
                return builtin(args);

            var f = _fns.Get(name);

            if (args.Count != f.Params.Count)
                throw new Exception($"Function '{name}' expects {f.Params.Count} args, got {args.Count}");

            _env.PushFrame();
            try
            {
                // Bind parameters
                for (int i = 0; i < f.Params.Count; i++)
                {
                    var p = f.Params[i];
                    _env.Define(p.Name, args[i]);
                }

                // Execute body
                ExecCompound(f.Body);
            }
            catch (ReturnSignal r)
            {
                _env.PopFrame();
                return r.Value ?? new Value(0);
            }

            _env.PopFrame();
            return new Value(0); // default if no explicit return
        }

        // ----- Statements -----

        private void ExecFor(ForStmt f)
        {
            _env.PushScope();
            try
            {
                // init
                if (f.InitDecl is VarDecl vd)
                {
                    var init = vd.Init is null ? new Value(0) : Eval(vd.Init);
                    _env.Define(vd.Name, init);
                }
                else if (f.InitExpr is not null)
                {
                    _ = Eval(f.InitExpr);
                }

                // loop
                while (true)
                {
                    int cond = 1;
                    if (f.Cond is not null) cond = Eval(f.Cond).Int;
                    if (cond == 0) break;

                    try
                    {
                        ExecStmt(f.Body);
                    }
                    catch (ContinueSignal)
                    {
                        // jump directly to post
                    }
                    catch (BreakSignal)
                    {
                        break;
                    }
                    // bubble returns out of the loop to caller
                    // (no catch here for ReturnSignal)
                    // post
                    if (f.Post is not null) _ = Eval(f.Post);
                    continue;

                    // If we continued from body, still run post then continue
                }
            }
            finally
            {
                _env.PopScope();
            }
        }

        private void ExecIf(IfStmt i)
        {
            var c = Eval(i.Cond).Int;            // 0 == false, nonzero == true
            if (c != 0)
            {
                ExecStmt(i.Then);
            }
            else if (i.Else is not null)
            {
                ExecStmt(i.Else);
            }
        }

        private void ExecCompound(CompoundStmt block)
        {
            _env.PushScope();
            try
            {
                foreach (var item in block.Items)
                {
                    switch (item)
                    {
                        case VarDecl v:
                            var init = v.Init is null ? new Value(0) : Eval(v.Init);
                            _env.Define(v.Name, init);
                            break;

                        case Stmt s:
                            ExecStmt(s);
                            break;

                        default:
                            throw new Exception($"Unexpected node in block: {item.GetType().Name}");
                    }
                }
            }
            finally
            {
                _env.PopScope();
            }
        }

        private void ExecStmt(Stmt s)
        {
            switch (s)
            {
                case ExprStmt es:
                    if (es.Expr is not null) _ = Eval(es.Expr);
                    return;

                case ReturnStmt r:
                    var v = r.Expr is null ? (Value?)null : Eval(r.Expr);
                    throw new ReturnSignal(v);

                case CompoundStmt b:
                    ExecCompound(b);
                    return;

                case IfStmt iff:
                    ExecIf(iff);
                    return;
    
                case BreakStmt:
                    throw new BreakSignal();

                case ContinueStmt:
                    throw new ContinueSignal();
            
                case ForStmt f:
                    ExecFor(f);
                    return;
                default:
                    throw new Exception($"Unsupported statement: {s.GetType().Name}");
            }
        }

        
        // ----- Expressions -----

        private Value Eval(Expr e)
        {
            switch (e)
            {
                case IntegerExpr i:
                    return new Value(i.Value);

                case IdentExpr id:
                    if (!_env.TryGet(id.Name, out var v))
                        throw new Exception($"Undefined identifier '{id.Name}'");
                    return v;

                case UnaryExpr u:
                    {
                        // Evaluate operand
                        switch (u.Op)
                        {
                            // ----- Prefix increment/decrement -----
                            case "++pre":
                            case "--pre":
                                {
                                    if (u.Expr is not IdentExpr preId)
                                        throw new Exception("++/-- must target an identifier");

                                    if (!_env.TryGet(preId.Name, out var preVal))
                                        throw new Exception($"Undefined variable '{preId.Name}'");

                                    var newVal = u.Op == "++pre"
                                        ? new Value(preVal.Int + 1)
                                        : new Value(preVal.Int - 1);

                                    _env.TrySet(preId.Name, newVal);
                                    return newVal; // return new value (prefix semantics)
                                }

                            // ----- Postfix increment/decrement -----
                            case "++post":
                            case "--post":
                                {
                                    if (u.Expr is not IdentExpr postId)
                                        throw new Exception("++/-- must target an identifier");

                                    if (!_env.TryGet(postId.Name, out var postVal))
                                        throw new Exception($"Undefined variable '{postId.Name}'");

                                    var newVal = u.Op == "++post"
                                        ? new Value(postVal.Int + 1)
                                        : new Value(postVal.Int - 1);

                                    _env.TrySet(postId.Name, newVal);
                                    return postVal; // return old value (postfix semantics)
                                }

                            // ----- Simple unary operators -----
                            case "+":
                                return new Value(+Eval(u.Expr).Int);
                            case "-":
                                return new Value(-Eval(u.Expr).Int);
                            case "!":
                                return new Value(Eval(u.Expr).Int == 0 ? 1 : 0);
                            case "&":
                            case "*":
                                throw new Exception("Address/deref not supported yet");
                            default:
                                throw new Exception($"Unknown unary op {u.Op}");
                        }
                    }

                case BinaryExpr b:
                    {
                        // short-circuit for && and ||
                        if (b.Op == "&&")
                        {
                            var l = Eval(b.Left).Int;
                            if (l == 0) return new Value(0);
                            var r = Eval(b.Right).Int;
                            return new Value(r != 0 ? 1 : 0);
                        }
                        if (b.Op == "||")
                        {
                            var l = Eval(b.Left).Int;
                            if (l != 0) return new Value(1);
                            var r = Eval(b.Right).Int;
                            return new Value(r != 0 ? 1 : 0);
                        }

                        var a = Eval(b.Left).Int;
                        var c = Eval(b.Right).Int;

                        return b.Op switch
                        {
                            "+" => new Value(a + c),
                            "-" => new Value(a - c),
                            "*" => new Value(a * c),
                            "/" => new Value(c == 0 ? throw new DivideByZeroException() : a / c),
                            "==" => new Value(a == c ? 1 : 0),
                            "!=" => new Value(a != c ? 1 : 0),
                            "<" => new Value(a < c ? 1 : 0),
                            ">" => new Value(a > c ? 1 : 0),
                            "<=" => new Value(a <= c ? 1 : 0),
                            ">=" => new Value(a >= c ? 1 : 0),
                            _ => throw new Exception($"Unsupported binary op {b.Op}")
                        };
                    }

                case AssignExpr a:
                    {
                        // MVP: only identifiers as lvalues
                        if (a.Left is not IdentExpr lid)
                            throw new Exception("Assignment target must be an identifier (no pointers yet).");
                        var rv = Eval(a.Right);
                        if (!_env.TrySet(lid.Name, rv))
                            throw new Exception($"Assign to undefined variable '{lid.Name}'");
                        return rv;
                    }

                case CallExpr c:
                    {
                        var calleeName = (c.Callee as IdentExpr)?.Name
                                         ?? throw new Exception("Only simple function names supported as callees.");

                        var args = new List<Value>(c.Args.Count);
                        foreach (var arg in c.Args) args.Add(Eval(arg));

                        return Call(calleeName, args);
                    }

                default:
                    throw new Exception($"Unsupported expression: {e.GetType().Name}");
            }
        }
    }
}
