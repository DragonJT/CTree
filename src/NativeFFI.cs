
using System;
using System.Collections.Concurrent;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.InteropServices;
using System.Linq.Expressions;

namespace MiniC;

static class UnmanagedDelegateFactory
{
    private static readonly AssemblyBuilder asm =
        AssemblyBuilder.DefineDynamicAssembly(new AssemblyName("DynFFI"), AssemblyBuilderAccess.Run);
    private static readonly ModuleBuilder mod = asm.DefineDynamicModule("DynFFIMod");

    private static readonly ConcurrentDictionary<(Type ret, string cc, Type[] args), Type> Cache = new();

    public static Type Create(Type retType, CallingConvention cc, params Type[] argTypes)
    {
        var key = (retType, cc.ToString(), argTypes);
        return Cache.GetOrAdd(key, _ => Build(retType, cc, argTypes));
    }

    private static Type Build(Type retType, CallingConvention cc, Type[] argTypes)
    {
        // public sealed delegate Ret D(Arg0, Arg1, ...);
        string name = $"UD_{cc}_{retType.Name}__{string.Join("_", Array.ConvertAll(argTypes, t => t.Name))}_{Guid.NewGuid():N}";
        var tb = mod.DefineType(name,
            TypeAttributes.Sealed | TypeAttributes.Public | TypeAttributes.AutoClass,
            typeof(MulticastDelegate));

        // [UnmanagedFunctionPointer(cc)]
        var attrCtor = typeof(UnmanagedFunctionPointerAttribute).GetConstructor(new[] { typeof(CallingConvention) })!;
        var cab = new CustomAttributeBuilder(attrCtor, new object[] { cc });
        tb.SetCustomAttribute(cab);

        // .ctor(object, IntPtr)
        var ctor = tb.DefineConstructor(
            MethodAttributes.RTSpecialName | MethodAttributes.HideBySig | MethodAttributes.Public,
            CallingConventions.Standard,
            new[] { typeof(object), typeof(IntPtr) });
        ctor.SetImplementationFlags(MethodImplAttributes.Runtime | MethodImplAttributes.Managed);

        // Ret Invoke(Arg0, Arg1, ...)
        var invoke = tb.DefineMethod("Invoke",
            MethodAttributes.Public | MethodAttributes.HideBySig | MethodAttributes.NewSlot | MethodAttributes.Virtual,
            retType,
            argTypes);
        invoke.SetImplementationFlags(MethodImplAttributes.Runtime | MethodImplAttributes.Managed);

        return tb.CreateType()!;
    }
}

static class Native
{
    [DllImport("kernel32")] static extern IntPtr LoadLibrary(string path);
    [DllImport("kernel32")] static extern IntPtr GetProcAddress(IntPtr h, string name);
    [DllImport("kernel32")] static extern bool FreeLibrary(IntPtr h);
}
    
public sealed class NativeFunction
{
    public readonly Delegate Delegate;
    public NativeFunction(Delegate d) => Delegate = d;
}

public sealed class FfiBinder
{
    private static (Type ret, Type[] args) MapSignature(TypeRef retType, List<ParamDecl> ps)
    {
        static Type Map(TypeRef type)
        {
            Type MapName(string t) => t switch
            {
                "long" => typeof(long),
                "int" => typeof(int),
                "float" => typeof(float),
                "double" => typeof(double),
                "char" => typeof(char),
                "void" => typeof(void),
                _ => typeof(IntPtr),
            };
            var csType = MapName(type.Name);
            if (csType == typeof(char) && type.PointerDepth == 1)
            {
                csType = typeof(string);
            }
            return csType;
        }

        return (Map(retType), ps.Select(p => Map(p.Type)).ToArray());
    }

    public static NativeFunction Bind(ExternFuncDecl d)
    {
        // Load the DLL (cross-platform)
        IntPtr h = NativeLibrary.Load(d.DllName);
        string entry = d.EntryPoint ?? d.Func.Name;

        // Get function pointer
        IntPtr p = NativeLibrary.GetExport(h, entry);

        (Type ret, Type[] args) = MapSignature(d.Func.RetType, d.Func.Params);

        var cc = d.CallConv?.ToLowerInvariant() switch
        {
            "stdcall" => CallingConvention.StdCall,
            _         => CallingConvention.Cdecl, // default
        };

        var delegateType = UnmanagedDelegateFactory.Create(ret, cc, args);

        // This overload now accepts the emitted, non-generic delegate type:
        Delegate del = Marshal.GetDelegateForFunctionPointer(p, delegateType);
        return new NativeFunction(del);
    }
}