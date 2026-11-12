using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Text.Json;

namespace Unbound.ApiTools;

public sealed class ConstructorStoreReport
{
    public int Equal { get; set; }
    public int Unknown { get; set; }
    public int UnmatchedFields { get; set; }
    public int UnmatchedConstructors { get; set; }
    public int BranchingConstructors { get; set; }
    public List<ConstructorStoreDifference> Differences { get; set; } = new();
}

public sealed class ConstructorStoreDifference
{
    public string Constructor { get; set; } = "";
    public string Field { get; set; } = "";
    public string Expected { get; set; } = "";
    public string Actual { get; set; } = "";
}

/// <summary>Compares literal instance stores in straight-line managed constructors. This is a store comparison,
/// not an inference about state after arbitrary calls, inherited constructors or native initialization.</summary>
public static class ConstructorStores
{
    static readonly Dictionary<ushort, OpCode> Opcodes = typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static)
        .Select(f => (OpCode)f.GetValue(null)!).ToDictionary(o => unchecked((ushort)o.Value));
    readonly record struct Instruction(OpCode Op, object? Operand);
    sealed class AssemblyStores
    {
        public Dictionary<string, Dictionary<string, string>> Constructors = new(StringComparer.Ordinal);
        public int Branching;
    }

    public static ConstructorStoreReport Compare(IEnumerable<string> referenceAssemblies, string implementation)
    {
        var reference = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
        var report = new ConstructorStoreReport();
        foreach (var path in referenceAssemblies)
        {
            var stores = Read(path);
            report.BranchingConstructors += stores.Branching;
            foreach (var pair in stores.Constructors) reference.TryAdd(pair.Key, pair.Value);
        }
        var actual = Read(implementation);
        report.BranchingConstructors += actual.Branching;
        foreach (var constructor in actual.Constructors.OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            if (!reference.TryGetValue(constructor.Key, out var expected)) { report.UnmatchedConstructors++; continue; }
            foreach (var field in expected.Keys.Union(constructor.Value.Keys).OrderBy(k => k, StringComparer.Ordinal))
            {
                if (!expected.TryGetValue(field, out var wanted) || !constructor.Value.TryGetValue(field, out var found))
                { report.UnmatchedFields++; continue; }
                if (wanted == "unknown" || found == "unknown") { report.Unknown++; continue; }
                if (wanted == found) { report.Equal++; continue; }
                report.Differences.Add(new ConstructorStoreDifference { Constructor = constructor.Key, Field = field,
                    Expected = wanted, Actual = found });
            }
        }
        return report;
    }

    static AssemblyStores Read(string path)
    {
        using var pe = new PEReader(File.OpenRead(path));
        var metadata = pe.GetMetadataReader();
        var result = new AssemblyStores();
        foreach (var typeHandle in metadata.TypeDefinitions)
        foreach (var methodHandle in metadata.GetTypeDefinition(typeHandle).GetMethods())
        {
            var method = metadata.GetMethodDefinition(methodHandle);
            if (metadata.GetString(method.Name) != ".ctor" || method.RelativeVirtualAddress == 0) continue;
            var body = pe.GetMethodBody(method.RelativeVirtualAddress);
            var il = body.GetILReader();
            var instructions = new List<Instruction>();
            while (il.RemainingBytes > 0)
            {
                ushort code = il.ReadByte();
                if (code == 0xfe) code = (ushort)(0xfe00 | il.ReadByte());
                if (!Opcodes.TryGetValue(code, out var op)) throw new BadImageFormatException($"Unknown IL opcode in {path}");
                object? operand = op.OperandType switch
                {
                    OperandType.InlineNone => null,
                    OperandType.ShortInlineI => il.ReadSByte(),
                    OperandType.InlineI => il.ReadInt32(),
                    OperandType.InlineI8 => il.ReadInt64(),
                    OperandType.ShortInlineR => il.ReadSingle(),
                    OperandType.InlineR => il.ReadDouble(),
                    OperandType.ShortInlineVar or OperandType.ShortInlineBrTarget => il.ReadByte(),
                    OperandType.InlineVar => il.ReadUInt16(),
                    OperandType.InlineString => metadata.GetUserString(MetadataTokens.UserStringHandle(il.ReadInt32() & 0xffffff)),
                    OperandType.InlineSwitch => SkipSwitch(ref il),
                    _ => il.ReadInt32()
                };
                if (op != OpCodes.Nop) instructions.Add(new Instruction(op, operand));
            }
            if (body.ExceptionRegions.Length != 0 || instructions.Any(i => i.Op.FlowControl is FlowControl.Branch or FlowControl.Cond_Branch))
            { result.Branching++; continue; }
            var stores = new Dictionary<string, string>(StringComparer.Ordinal);
            for (int i = 0; i < instructions.Count; i++)
            {
                if (instructions[i].Op != OpCodes.Stfld) continue;
                var handle = MetadataTokens.EntityHandle((int)instructions[i].Operand!);
                // Include owner and type so hidden/retyped storage is never paired accidentally.
                var key = FieldKey(metadata, handle);
                stores[key] = i >= 2 && instructions[i - 2].Op == OpCodes.Ldarg_0 ? Literal(instructions[i - 1]) : "unknown";
            }
            result.Constructors[MetadataNames.TypeDefinition(metadata, typeHandle) + "/" + CanonicalSignatures.MethodOf(metadata, methodHandle)] = stores;
        }
        return result;
    }

    static string FieldKey(MetadataReader metadata, EntityHandle handle)
    {
        if (handle.Kind == HandleKind.FieldDefinition)
        {
            var field = metadata.GetFieldDefinition((FieldDefinitionHandle)handle);
            return metadata.GetString(field.Name) + ":" + CanonicalSignatures.FieldOf(metadata, (FieldDefinitionHandle)handle)
                + "@" + MetadataNames.TypeDefinition(metadata, field.GetDeclaringType());
        }
        if (handle.Kind != HandleKind.MemberReference) throw new BadImageFormatException("Invalid instance field store");
        var reference = metadata.GetMemberReference((MemberReferenceHandle)handle);
        var owner = reference.Parent.Kind switch
        {
            HandleKind.TypeDefinition => MetadataNames.TypeDefinition(metadata, (TypeDefinitionHandle)reference.Parent),
            HandleKind.TypeReference => MetadataNames.TypeReference(metadata, (TypeReferenceHandle)reference.Parent),
            HandleKind.TypeSpecification => metadata.GetTypeSpecification((TypeSpecificationHandle)reference.Parent)
                .DecodeSignature(CanonicalSignatures.Instance, null),
            _ => throw new BadImageFormatException("Invalid field owner")
        };
        return metadata.GetString(reference.Name) + ":" + reference.DecodeFieldSignature(CanonicalSignatures.Instance, null) + "@" + owner;
    }

    static object? SkipSwitch(ref BlobReader il)
    {
        var count = il.ReadInt32();
        il.Offset = checked(il.Offset + checked(count * 4));
        return null;
    }

    static string Literal(Instruction instruction)
    {
        var op = instruction.Op;
        if (op == OpCodes.Ldnull) return "null";
        if (op == OpCodes.Ldc_I4_M1) return "i4:-1";
        if (op.Value >= OpCodes.Ldc_I4_0.Value && op.Value <= OpCodes.Ldc_I4_8.Value) return "i4:" + (op.Value - OpCodes.Ldc_I4_0.Value);
        if (op == OpCodes.Ldc_I4 || op == OpCodes.Ldc_I4_S) return "i4:" + instruction.Operand;
        if (op == OpCodes.Ldstr) return "string:" + JsonSerializer.Serialize(instruction.Operand);
        if (op == OpCodes.Ldc_I8) return "i8:" + instruction.Operand;
        if (op == OpCodes.Ldc_R4) return "r4:" + ((float)instruction.Operand!).ToString("R", CultureInfo.InvariantCulture);
        if (op == OpCodes.Ldc_R8) return "r8:" + ((double)instruction.Operand!).ToString("R", CultureInfo.InvariantCulture);
        return "unknown";
    }
}
