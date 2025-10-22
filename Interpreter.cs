
namespace MiniC
{
    public readonly struct Value
    {
        public enum Kind { Int, Float }
        public readonly Kind Type;
        public readonly long I;
        public readonly double F;

        public Value(long i)
        {
            Type = Kind.Int;
            I = i;
            F = i;
        }

        public Value(double f)
        {
            Type = Kind.Float;
            F = f;
            I = (int)f;
        }

        public bool IsFloat => Type == Kind.Float;
        public bool IsTruthy => IsFloat ? F != 0.0 : I != 0;

        public double AsFloat() => IsFloat ? F : I;

        public override string ToString() => IsFloat ? F.ToString() : I.ToString();

        public static implicit operator Value(long i) => new(i);
        public static implicit operator Value(double f) => new(f);

        // ---------- arithmetic ----------
        public static Value operator +(Value a, Value b)
        {
            if (a.IsFloat || b.IsFloat)
                return new Value(a.AsFloat() + b.AsFloat());
            return new Value(a.I + b.I);
        }

        public static Value operator -(Value a, Value b)
        {
            if (a.IsFloat || b.IsFloat)
                return new Value(a.AsFloat() - b.AsFloat());
            return new Value(a.I - b.I);
        }

        public static Value operator *(Value a, Value b)
        {
            if (a.IsFloat || b.IsFloat)
                return new Value(a.AsFloat() * b.AsFloat());
            return new Value(a.I * b.I);
        }

        public static Value operator /(Value a, Value b)
        {
            if (a.IsFloat || b.IsFloat)
                return new Value(a.AsFloat() / b.AsFloat());
            if (b.I == 0) throw new DivideByZeroException();
            return new Value(a.I / b.I);
        }

        // ---------- comparison ----------
        public static Value operator <(Value a, Value b) => new (a.AsFloat() < b.AsFloat() ? 1 : 0);
        public static Value operator >(Value a, Value b) => new (a.AsFloat() > b.AsFloat() ? 1 : 0);
        public static Value operator <=(Value a, Value b) => new (a.AsFloat() <= b.AsFloat() ? 1 : 0);
        public static Value operator >=(Value a, Value b) => new (a.AsFloat() >= b.AsFloat() ? 1 : 0);
        public static Value operator ==(Value a, Value b) => new (a.AsFloat() == b.AsFloat() ? 1 : 0);
        public static Value operator !=(Value a, Value b) => new (a.AsFloat() != b.AsFloat() ? 1 : 0);

        // aliases for && and || usage in your interpreter
        public static Value And(Value a, Value b) => new (a.IsTruthy && b.IsTruthy ? 1 : 0);
        public static Value Or(Value a, Value b) => new (a.IsTruthy || b.IsTruthy ? 1 : 0);

        // ---------- equality boilerplate ----------
        public override bool Equals(object? obj)
        {
            if (obj is not Value v) return false;
            return this.AsFloat() == v.AsFloat();
        }

        public override int GetHashCode() => HashCode.Combine(Type, I, F);

        public static Value Add(Value v)
        {
            return v.IsFloat ? +v.F : +v.I;
        }

        public static Value Sub(Value v)
        {
            return v.IsFloat ? -v.F : -v.I;
        }
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
                    foreach (var a in args) Console.WriteLine(a);
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
                    bool cond = true;
                    if (f.Cond is not null) cond = Eval(f.Cond).IsTruthy;
                    if (!cond) break;

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
            if (Eval(i.Cond).IsTruthy)
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
        private Value PrefixInc(UnaryExpr u, int delta)
        {
            if (u.Expr is not IdentExpr id)
                throw new Exception("++/-- target must be identifier");

            if (!_env.TryGet(id.Name, out var old))
                throw new Exception($"Undefined variable '{id.Name}'");

            var newVal = old + new Value(delta);
            _env.TrySet(id.Name, newVal);
            return newVal; // return updated value
        }

        private Value PostfixInc(UnaryExpr u, int delta)
        {
            if (u.Expr is not IdentExpr id)
                throw new Exception("++/-- target must be identifier");

            if (!_env.TryGet(id.Name, out var old))
                throw new Exception($"Undefined variable '{id.Name}'");

            var newVal = old + new Value(delta);
            _env.TrySet(id.Name, newVal);
            return old; // return old value (post semantics)
        }

        private Value Eval(Expr e)
        {
            switch (e)
            {
                case IntegerExpr i:
                    return new Value(i.Value);

                case FloatExpr f:
                    return new Value(f.Value);
                    
                case IdentExpr id:
                    if (!_env.TryGet(id.Name, out var v))
                        throw new Exception($"Undefined identifier '{id.Name}'");
                    return v;

                case UnaryExpr u:
                {
                    var val = Eval(u.Expr);

                    return u.Op switch
                    {
                        "+" => Value.Add(val), 
                        "-" => Value.Sub(val),
                        "!" => new Value(val.IsTruthy ? 0 : 1), // logical NOT, returns int 0/1

                        // increment/decrement cases (you already have them)
                        "++pre"  => PrefixInc(u, +1),
                        "--pre"  => PrefixInc(u, -1),
                        "++post" => PostfixInc(u, +1),
                        "--post" => PostfixInc(u, -1),

                        _ => throw new Exception($"Unsupported unary op '{u.Op}'")
                    };
                }

                case BinaryExpr b:
                    {
                        var a = Eval(b.Left);
                        var c = Eval(b.Right);

                        return b.Op switch
                        {
                            "&&" => Value.And(a, c),
                            "||" => Value.Or(a, c),
                            "+" => a + c,
                            "-" => a - c,
                            "*" => a * c,
                            "/" => a / c,
                            "==" => a == c,
                            "!=" => a != c,
                            "<" => a < c,
                            ">" => a > c,
                            "<=" => a <= c,
                            ">=" => a >= c,
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
