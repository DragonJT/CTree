
namespace MiniC
{
    public readonly struct Value
    {
        public enum Kind { Int, Float, Double, Long, Ptr, Str, Null }
        public readonly Kind Type;
        public readonly long I;
        public readonly double F;
        public readonly IntPtr P;
        public readonly string? S;

        public Value(){ Type = Kind.Null;  I = 0; F = 0; P = IntPtr.Zero; S = null; }
        public Value(long i) { Type = Kind.Int; I = i; F = i; P = IntPtr.Zero; S = null; }
        public Value(double f) { Type = Kind.Float;F = f;    I = (int)f;P = IntPtr.Zero; S = null; }
        public Value(IntPtr p) { Type = Kind.Ptr;  P = p;    I = 0;    F = 0;            S = null; }
        public Value(string s) { Type = Kind.Str;  S = s;    I = 0;    F = 0;            P = IntPtr.Zero; }

        public bool IsTruthy => Type switch
        {
            Kind.Int => I != 0,
            Kind.Long => I != 0,
            Kind.Float => F != 0.0,
            Kind.Double => F != 0.0,
            Kind.Ptr => P != IntPtr.Zero,
            Kind.Str => false,
            Kind.Null => false,
            _ => throw new Exception($"Unknown type {Type}"),
        };

        public bool IsFloat => Type == Kind.Float || Type == Kind.Double;

        public object Box() => Type switch
        {
            Kind.Int => (int)I,
            Kind.Long => I,
            Kind.Float => (float)F,
            Kind.Double => F,
            Kind.Ptr => P,
            Kind.Str => S!,
            Kind.Null => IntPtr.Zero,
            _ => throw new Exception($"Unknown type {Type}"),
        };
        
        public double AsFloat() => IsFloat ? F : I;

        public override string ToString() => Type switch
        {
            Kind.Int => I.ToString(),
            Kind.Long => I.ToString(),
            Kind.Float => F.ToString(),
            Kind.Double => F.ToString(),
            Kind.Ptr => P==IntPtr.Zero ? "NULL" : P.ToString(),
            Kind.Str => S!,
            Kind.Null => "NULL",
            _ => throw new Exception($"Unknown type {Type}"),
        };

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
        private readonly Dictionary<string, NativeFunction> _native = new();

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
            foreach (var d in tu.Decls)
            {
                switch (d)
                {
                    case FuncDef f:
                        _fns.Add(f);
                        break;
                    case ExternFuncDecl e:
                        _native[e.Func.Name] = FfiBinder.Bind(e);
                        break;
                }
            }

            var args = new List<Value> { new Value(argv.Length) };
            foreach (var a in argv) args.Add(new Value(a));
            return Call(entry, args);
        }

        private Value InvokeNative(NativeFunction nf, List<Value> args)
        {
            var boxedArgs = args.Select(a=>a.Box()).ToArray();
            var retObj = nf.Delegate.DynamicInvoke(boxedArgs);

            if (retObj is null) return new Value(0);
            return retObj switch
            {
                int i => new Value(i),
                long l => new Value(l),
                float f => new Value((double)f),
                double d => new Value(d),
                IntPtr p => new Value(p),
                _ => throw new Exception($"FFI return type not supported: {retObj.GetType().Name}")
            };
        }

        private Value Call(string name, List<Value> args)
        {
            if (_native.TryGetValue(name, out var nf))
                return InvokeNative(nf, args);
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

        private void ExecWhile(WhileStmt w)
        {
            while (Eval(w.Cond).IsTruthy)
            {
                try
                {
                    ExecStmt(w.Body);
                }
                catch (BreakSignal)
                {
                    break;
                }
                catch (ContinueSignal)
                {
                    continue;
                }
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
                case WhileStmt w:
                    ExecWhile(w);
                    return;
                    
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
                case NullExpr:
                    return new Value();

                case StringExpr s:
                    return new Value(s.Value);

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
