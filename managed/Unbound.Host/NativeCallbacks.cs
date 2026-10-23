using System;
using System.IO;
using System.Linq;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Unbound.Host;

/// <summary>
/// Under Mono a native callback made from a delegate to a static method stays valid after the
/// delegate is collected (the thunk belongs to the method), and plugins rely on it: Firebase passes
/// <c>SetLogFunction(LogMessageFromCallback)</c> a temporary delegate. .NET frees the thunk with the
/// delegate, so every P/Invoke taking a delegate gets a managed wrapper that roots the delegates
/// it passes before calling the native entry point (kept as <c>&lt;name&gt;__native</c>).
/// </summary>
static class NativeCallbacks
{
    internal static byte[] KeepAlive(byte[] image)
    {
        if (!HasPInvokes(image)) return image;
        using var input = new MemoryStream(image);
        using var assembly = Mono.Cecil.AssemblyDefinition.ReadAssembly(input, new ReaderParameters { AssemblyResolver = AccessChecks.Resolver });
        var module = assembly.MainModule;
        MethodReference? keep = null;
        bool changed = false;
        foreach (var type in module.GetTypes().ToList())
        {
            foreach (var method in type.Methods.ToList())
            {
                if (!method.IsPInvokeImpl || !method.Parameters.Any(p => IsDelegate(p.ParameterType))) continue;
                keep ??= module.ImportReference(typeof(Unbound.NativeCallbackRoots).GetMethod(nameof(Unbound.NativeCallbackRoots.Keep))!);
                Wrap(type, method, keep);
                changed = true;
            }
        }
        if (!changed) return image;
        using var output = new MemoryStream();
        assembly.Write(output);
        var dump = Environment.GetEnvironmentVariable("UNBOUND_DUMP_REWRITTEN");
        if (!string.IsNullOrEmpty(dump)) File.WriteAllBytes(Path.Combine(dump, assembly.Name.Name + ".dll"), output.ToArray());
        return output.ToArray();
    }

    static bool HasPInvokes(byte[] image)
    {
        using var pe = new PEReader(new MemoryStream(image));
        if (!pe.HasMetadata) return false;
        return pe.GetMetadataReader().GetTableRowCount(TableIndex.ImplMap) > 0;
    }

    static bool IsDelegate(Mono.Cecil.TypeReference type)
    {
        try
        {
            var definition = type.Resolve();
            return definition?.BaseType?.FullName == "System.MulticastDelegate";
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>Moves the P/Invoke to a new method and makes the original a managed wrapper.</summary>
    static void Wrap(Mono.Cecil.TypeDefinition type, Mono.Cecil.MethodDefinition method, MethodReference keep)
    {
        var native = new Mono.Cecil.MethodDefinition(method.Name + "__native", method.Attributes, method.ReturnType)
        {
            ImplAttributes = method.ImplAttributes,
            PInvokeInfo = method.PInvokeInfo,
            IsPInvokeImpl = true,
        };
        native.MethodReturnType.MarshalInfo = method.MethodReturnType.MarshalInfo;
        foreach (var p in method.Parameters)
        {
            var copy = new ParameterDefinition(p.Name, p.Attributes, p.ParameterType);
            if (p.HasMarshalInfo) copy.MarshalInfo = p.MarshalInfo;
            native.Parameters.Add(copy);
        }
        native.IsPrivate = true;
        type.Methods.Add(native);

        // Cecil's PInvokeInfo setter sets the flag again: clear the info first.
        method.PInvokeInfo = null;
        method.IsPInvokeImpl = false;
        method.ImplAttributes = Mono.Cecil.MethodImplAttributes.IL | Mono.Cecil.MethodImplAttributes.Managed;
        method.MethodReturnType.MarshalInfo = null;
        foreach (var p in method.Parameters)
        {
            p.MarshalInfo = null;
            p.HasFieldMarshal = false;
        }
        method.Body = new Mono.Cecil.Cil.MethodBody(method);
        var il = method.Body.GetILProcessor();
        foreach (var p in method.Parameters)
        {
            if (!IsDelegate(p.ParameterType)) continue;
            il.Emit(OpCodes.Ldarg, p);
            il.Emit(OpCodes.Call, keep);
        }
        foreach (var p in method.Parameters) il.Emit(OpCodes.Ldarg, p);
        il.Emit(OpCodes.Call, native);
        il.Emit(OpCodes.Ret);
    }
}
