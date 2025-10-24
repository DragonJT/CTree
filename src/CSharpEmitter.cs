
using System.Runtime.InteropServices;
using System.Text;

using MiniC;

// ===== Emitter config & helpers =====

public sealed class EmitterConfig
{
    // Output organization
    public string? Namespace { get; init; } = "Generated";
    public string ContainerTypeName { get; init; } = "Native"; // static class for funcs/globals

    // Interop knobs
    public bool UseUnsafe { get; init; } = true;
    public bool PreferIntPtrOverPointers { get; init; } = false;
    public string? DllName { get; init; } = null; // used when Attribute.Import && no per-func dll known
    public CallingConvention CallingConvention { get; init; } = CallingConvention.Cdecl;
    public CharSet CharSet { get; init; } = CharSet.Ansi;

    // C layout assumptions (affects 'long' and size_t mapping)
    public bool TargetIsMsvcLLP64 { get; init; } = true; // Windows/MSVC: long=32
    public bool UseNUIntForSizeT { get; init; } = true;

    // Formatting
    public bool AddHeader { get; init; } = true;
}

internal sealed class IndentedWriter
{
    private readonly StringBuilder _sb = new();
    private int _indent;

    public IDisposable Indent()
    {
        _indent++;
        return new Scope(() => _indent--);
    }

    public void WriteLine(string s = "")
    {
        if (_indent > 0 && s.Length > 0)
            _sb.Append(new string(' ', _indent * 4));
        _sb.AppendLine(s);
    }

    public override string ToString() => _sb.ToString();

    private sealed class Scope : IDisposable
    {
        private readonly Action _onDispose;
        public Scope(Action onDispose) => _onDispose = onDispose;
        public void Dispose() => _onDispose();
    }
}

// ===== Type mapper wired to your TypeRef/FuncPtrTypeRef =====

internal sealed class CsEmitter
{
    private readonly EmitterConfig _cfg;
    private readonly IndentedWriter _w = new();
    private readonly Dictionary<string, TypeRefBase> _typedefs = new(StringComparer.Ordinal);
    private readonly HashSet<string> _structsWithLayout = new(StringComparer.Ordinal);

    public CsEmitter(EmitterConfig cfg) => _cfg = cfg;

    public string ToCsType(TypeRefBase t, bool asFieldOrLocal = false)
    {
        switch (t)
        {
            case TypeRef tr:
                return MapTypeRef(tr, asFieldOrLocal);

            case FuncPtrTypeRef fptr:
            {
                var ret = ToCsType(fptr.ReturnType, false);
                var ps = string.Join(", ",
                    fptr.Params.Select(p => ToCsType(p.Type, true)));
                    var star = new string('*', Math.Max(1, fptr.PointerDepthToFunc));
                // delegate* unmanaged<...> requires unsafe
                return $"delegate{star} unmanaged<{ps}{(ps.Length > 0 ? ", " : "")}{ret}>";
            }

            default:
                throw new NotSupportedException($"Unknown TypeRefBase: {t?.GetType().Name}");
        }
    }

    private string MapTypeRef(TypeRef tr, bool asFieldOrLocal)
    {
        // typedef name resolution first
        if (!tr.Struct && _typedefs.TryGetValue(tr.Name, out var aliased))
        {
            // Keep pointer depth on top of the alias
            var core = ToCsType(aliased, asFieldOrLocal);
            if (tr.PointerDepth <= 0) return core;
            return _cfg.PreferIntPtrOverPointers ? "IntPtr" : core + new string('*', tr.PointerDepth);
        }
        else
        {
            string core;
            if (tr.Struct)
            {
                if (_structsWithLayout.Contains(tr.Name))
                {
                    core = tr.Name;
                }
                else
                {
                    core = "nint";
                }
            }
            else
            {
                core = MapBuiltin(tr.Name, tr.PointerDepth > 0);
            }
            if (tr.PointerDepth > 0)
                return _cfg.PreferIntPtrOverPointers ? "IntPtr" : core + new string('*', tr.PointerDepth);

            return core;
        }
    }

    private string MapBuiltin(string name, bool isPointer)
    {
        switch (name)
        {
            // C core
            case "void": return isPointer ? "nint" : "void";
            case "char": return "sbyte";   // raw 8-bit char; NOT C# char (UTF-16)
            case "unsigned char": return "byte";
            case "short": return "short";
            case "unsigned short": return "ushort";
            case "int": return "int";
            case "unsigned int": return "uint";
            case "long": return _cfg.TargetIsMsvcLLP64 ? "int" : "long";   // LLP64 vs LP64
            case "unsigned long": return _cfg.TargetIsMsvcLLP64 ? "uint" : "ulong";
            case "float": return "float";
            case "double": return "double";

            // Khronos fixed-width (treat as exact-width typedefs)
            case "khronos_int8_t": return "sbyte";
            case "khronos_uint8_t": return "byte";
            case "khronos_int16_t": return "short";
            case "khronos_uint16_t": return "ushort";
            case "khronos_int32_t": return "int";
            // If you ever see khronos_uint32_t, map to:
            // case "khronos_uint32_t":  return "uint";
            case "khronos_int64_t": return "long";
            case "khronos_uint64_t": return "ulong";
            case "khronos_float_t": return "float";

            // Pointer-sized Khronos
            case "khronos_intptr_t": return "nint";   // or "IntPtr" if you prefer a ref-type
            case "khronos_ssize_t": return "nint";   // signed size_t
            default:
                throw new Exception($"Unexpected type: {name}");
        }
    }

    public string Emit(TranslationUnit tu)
    {
        if (_cfg.AddHeader)
        {
            _w.WriteLine("// <auto-generated>");
            _w.WriteLine("//   Generated by CsEmitter from a C AST");
            _w.WriteLine("//   Do not edit by hand.");
            _w.WriteLine("// </auto-generated>");
            _w.WriteLine();
        }

        if (!string.IsNullOrEmpty(_cfg.Namespace))
        {
            _w.WriteLine($"namespace {_cfg.Namespace};");
            _w.WriteLine();
        }

        // Emit struct/typedefs first pass to collect names
        CollectDeclarations(tu);

        // Emit structs with fields
        foreach (var d in tu.Decls)
        {
            if (d is StructDecl sd && sd.Fields is not null)
                EmitStruct(sd);
        }

        // Container for functions and globals
        _w.WriteLine($"{( _cfg.UseUnsafe ? "public static unsafe" : "public static")} class {_cfg.ContainerTypeName}");
        _w.WriteLine("{");
        using (_w.Indent())
        {
            // typedefs as C# aliases (where legal) – we try with 'using' alias pattern
            foreach (var kv in _typedefs)
            {
                var cs = SafeId(kv.Key);
                var rhs = ToCsType(kv.Value);
                // Only emit for identifiers that map to a legal type name (skip function pointer typedefs as C# alias)
                if (kv.Value is TypeRef or FuncPtrTypeRef)
                    _w.WriteLine($"// typedef {kv.Key} -> {rhs}");
            }
            if (_typedefs.Count > 0) _w.WriteLine();

            // now functions, globals, and everything else
            foreach (var d in tu.Decls)
            {
                switch (d)
                {
                    case VarDecl v: EmitGlobalVar(v); break;
                    case FuncDef f: EmitFunction(f); break;
                    case TypedefDecl: /*already collected*/ break;
                    case StructDecl: /*emitted above or opaque*/ break;
                    case LinkageGroup lg:
                        foreach (var inner in lg.Decls) // simple flatten; Lang could control calling conv if you like
                        {
                            if (inner is VarDecl v2) EmitGlobalVar(v2);
                            else if (inner is FuncDef f2) EmitFunction(f2);
                        }
                        break;
                }
            }
        }
        _w.WriteLine("}");

        return _w.ToString();
    }

    private void CollectDeclarations(TranslationUnit tu)
    {
        foreach (var d in tu.Decls)
        {
            switch (d)
            {
                case TypedefDecl td:
                    _typedefs[td.Name] = td.Type;
                    break;
                case StructDecl sd when sd.Fields is not null:
                    _structsWithLayout.Add(sd.Name);
                    break;
                case LinkageGroup lg:
                    foreach (var inner in lg.Decls)
                        if (inner is TypedefDecl itd) _typedefs[itd.Name] = itd.Type;
                    break;
            }
        }
    }

    // ===== Decls =====

    private void EmitStruct(StructDecl s)
    {
        _w.WriteLine("[StructLayout(LayoutKind.Sequential)]");
        _w.WriteLine($"{( _cfg.UseUnsafe ? "public unsafe" : "public")} struct {SafeId(s.Name)}");
        _w.WriteLine("{");
        using (_w.Indent())
        {
            if (s.Fields is { Count: > 0 })
            {
                foreach (var f in s.Fields!)
                {
                    var t = ToCsType(f.Type, asFieldOrLocal: true);
                    _w.WriteLine($"public {t} {SafeId(f.Name)};");
                }
            }
            else
            {
                _w.WriteLine("// Opaque/forward-declared struct");
            }
        }
        _w.WriteLine("}");
        _w.WriteLine();
    }

    private void EmitGlobalVar(VarDecl v)
    {
        var t = ToCsType(v.Type, asFieldOrLocal: true);
        var name = SafeId(v.Name);
        var init = v.Init is null ? "" : " = " + EmitExpr(v.Init) + "";
        _w.WriteLine($"public static {t} {name}{init};");
        _w.WriteLine();
    }

    private void EmitFunction(FuncDef f)
    {
        var ret = ToCsType(f.RetType);
        var pars = string.Join(", ", f.Params.Select(EmitParam));

        if (f.Attribute == MiniC.Attribute.Import && f.Body is null)
        {
            var dll = _cfg.DllName ?? "UNKNOWN_DLL";
            _w.WriteLine($"[DllImport(\"{dll}\", EntryPoint=\"{f.Name}\", CallingConvention=CallingConvention.{_cfg.CallingConvention}, CharSet=CharSet.{_cfg.CharSet}, ExactSpelling=true)]");
            _w.WriteLine($"public static extern {ret} {SafeId(f.Name)}({pars});");
            _w.WriteLine();
            return;
        }

        if(f.Body == null)
        {
            return;
        }
        // Normal method (port mode or stub)
        _w.WriteLine($"public static {ret} {SafeId(f.Name)}({pars})");
        _w.WriteLine("{");
        using (_w.Indent())
        {
            EmitCompoundBody(f.Body);
        }
        _w.WriteLine("}");
        _w.WriteLine();
    }

    private string EmitParam(ParamDecl p)
    {
        var t = ToCsType(p.Type, asFieldOrLocal: true);
        return $"{t} {SafeId(p.Name)}";
    }

    // ===== Stmts =====

    private void EmitCompoundBody(CompoundStmt body)
    {
        foreach (var item in body.Items)
        {
            switch (item)
            {
                case Stmt s: EmitStmt(s); break;
                case Decl d: EmitLocalDecl(d); break;
            }
        }
    }

    private void EmitLocalDecl(Decl d)
    {
        switch (d)
        {
            case VarDecl v:
            {
                var t = ToCsType(v.Type, asFieldOrLocal: true);
                var name = SafeId(v.Name);
                var init = v.Init is null ? "" : " = " + EmitExpr(v.Init);
                _w.WriteLine($"{t} {name}{init};");
                break;
            }
            // local typedefs/structs uncommon; ignore or extend as needed
            default:
                _w.WriteLine("// unsupported local declaration: " + d.GetType().Name);
                break;
        }
    }

    private void EmitStmt(Stmt s)
    {
        switch (s)
        {
            case ExprStmt es:
                if (es.Expr is null) { _w.WriteLine(";"); }
                else _w.WriteLine(EmitExpr(es.Expr) + ";");
                break;

            case ReturnStmt rs:
                _w.WriteLine(rs.Expr is null ? "return;" : "return " + EmitExpr(rs.Expr) + ";");
                break;

            case BreakStmt:
                _w.WriteLine("break;");
                break;

            case ContinueStmt:
                _w.WriteLine("continue;");
                break;

            case IfStmt iff:
                _w.WriteLine($"if ({EmitExpr(iff.Cond)})");
                EmitStmtAsBlockOrSingle(iff.Then);
                if (iff.Else is not null)
                {
                    _w.WriteLine("else");
                    EmitStmtAsBlockOrSingle(iff.Else);
                }
                break;

            case WhileStmt ws:
                _w.WriteLine($"while ({EmitExpr(ws.Cond)})");
                EmitStmtAsBlockOrSingle(ws.Body);
                break;

            case ForStmt fs:
            {
                string initPart;
                if (fs.InitDecl is VarDecl vd)
                {
                    var t = ToCsType(vd.Type, asFieldOrLocal: true);
                    initPart = $"{t} {SafeId(vd.Name)}" + (vd.Init is null ? "" : " = " + EmitExpr(vd.Init));
                }
                else if (fs.InitExpr is Expr ie)
                {
                    initPart = EmitExpr(ie);
                }
                else initPart = "";

                var condPart = fs.Cond is null ? "" : EmitExpr(fs.Cond);
                var postPart = fs.Post is null ? "" : EmitExpr(fs.Post);
                _w.WriteLine($"for ({initPart}; {condPart}; {postPart})");
                EmitStmtAsBlockOrSingle(fs.Body);
                break;
            }

            case CompoundStmt c:
                _w.WriteLine("{");
                using (_w.Indent())
                {
                    EmitCompoundBody(c);
                }
                _w.WriteLine("}");
                break;

            default:
                _w.WriteLine("// unsupported stmt: " + s.GetType().Name);
                break;
        }
    }

    private void EmitStmtAsBlockOrSingle(Stmt s)
    {
        if (s is CompoundStmt)
            EmitStmt(s);
        else
        {
            _w.WriteLine("{");
            using (_w.Indent()) EmitStmt(s);
            _w.WriteLine("}");
        }
    }

    // ===== Exprs =====

    private string EmitExpr(Expr e)
    {
        switch (e)
        {
            case IntegerExpr ie: return ie.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
            case FloatExpr fe: return fe.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
            case StringExpr se: return "@\"" + se.Value.Replace("\"", "\"\"") + "\"";
            case NullExpr: return "null";
            case IdentExpr id: return SafeId(id.Name);
            case UnaryExpr ue:
                // e.g., &, *, +, -, !, ~, ++, --
                return ue.Op switch
                {
                    "++post" => "(" + EmitExpr(ue.Expr) + "++)",
                    "--post" => "(" + EmitExpr(ue.Expr) + "--)",
                    _ => "(" + ue.Op + EmitExpr(ue.Expr) + ")"
                };
            case BinaryExpr be:
                return "(" + EmitExpr(be.Left) + " " + be.Op + " " + EmitExpr(be.Right) + ")";
            case AssignExpr ae:
                return "(" + EmitExpr(ae.Left) + " = " + EmitExpr(ae.Right) + ")";
            case CallExpr ce:
                var args = string.Join(", ", ce.Args.Select(EmitExpr));
                return EmitExpr(ce.Callee) + "(" + args + ")";
            default:
                return "/*unsupported expr " + e.GetType().Name + "*/";
        }
    }

    // ===== Utilities =====

    private static string SafeId(string name)
    {
        // very small keyword set; expand as you like
        return name is "object" or "string" or "event" or "params" or "ref" or "out" or "in" or "base"
            or "namespace" or "class" or "struct" or "checked" or "unchecked" or "operator"
            ? "@" + name
            : name;
    }
}