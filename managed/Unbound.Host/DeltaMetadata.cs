using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

namespace Unbound.Host;

/// <summary>
/// The metadata a hot-reload delta's IL refers to: the loaded image and every delta applied to it,
/// this one last (a delta's rows and heap offsets continue the generations before it), and the
/// method bodies of the delta's IL.
/// </summary>
sealed class DeltaMetadata : IDisposable
{
    readonly PEReader m_Image;
    readonly List<MetadataReaderProvider> m_Providers;
    readonly MetadataAggregator m_Aggregator;
    readonly List<MetadataReader> m_Readers;

    internal DeltaMetadata(byte[] baseline, IReadOnlyList<byte[]> deltas)
    {
        m_Image = new PEReader(ImmutableArray.Create(baseline));
        m_Providers = deltas.Select(d => MetadataReaderProvider.FromMetadataImage(ImmutableArray.Create(d))).ToList();
        m_Readers = new List<MetadataReader> { m_Image.GetMetadataReader() };
        m_Readers.AddRange(m_Providers.Select(p => p.GetMetadataReader()));
        m_Aggregator = new MetadataAggregator(m_Readers[0], m_Readers.Skip(1).ToList());
    }

    internal MetadataReader Baseline => m_Readers[0];

    /// <summary>The newest delta's metadata.</summary>
    internal MetadataReader Delta => m_Readers[^1];

    /// <summary>The generation's reader of a handle and the handle within it.</summary>
    internal MetadataReader Reader(Handle handle, out Handle local)
    {
        local = m_Aggregator.GetGenerationHandle(handle, out int generation);
        return m_Readers[generation];
    }

    internal MetadataReader Reader(int token, out Handle local) => Reader(MetadataTokens.EntityHandle(token), out local);

    internal string String(StringHandle handle)
    {
        if (handle.IsNil) return "";
        var reader = Reader(handle, out var local);
        return reader.GetString((StringHandle)local);
    }

    /// <summary>A blob (its heap offset counts every generation's heap) and the reader it is in.</summary>
    internal BlobReader Blob(BlobHandle handle, out MetadataReader reader)
    {
        reader = Reader(handle, out var local);
        return reader.GetBlobReader((BlobHandle)local);
    }

    /// <summary>The methods a delta adds or replaces: the delta's row, the offset of its body in the
    /// delta's IL and its token in the aggregated metadata (the delta's rows follow the order of
    /// their tokens in its EnC map).</summary>
    internal IEnumerable<(MethodDefinition Method, int Offset, MethodDefinitionHandle Token)> Bodies()
    {
        var tokens = Delta.GetEditAndContinueMapEntries().Where(h => h.Kind == HandleKind.MethodDefinition).ToList();
        int row = 0;
        foreach (var handle in Delta.MethodDefinitions)
        {
            var method = Delta.GetMethodDefinition(handle);
            var token = row < tokens.Count ? (MethodDefinitionHandle)tokens[row] : default;
            row++;
            if (method.RelativeVirtualAddress != 0) yield return (method, method.RelativeVirtualAddress, token);
        }
    }

    /// <summary>A loaded method's declaring type (Cecil's full name: nested types after '/'), name and
    /// parameter count; null for a method a delta added or an unknown token.</summary>
    internal (string Type, string Name, int Parameters)? Identity(MethodDefinitionHandle token)
    {
        if (token.IsNil || MetadataTokens.GetRowNumber(token) > Baseline.MethodDefinitions.Count) return null;
        var method = Baseline.GetMethodDefinition(token);
        var blob = Baseline.GetBlobReader(method.Signature);
        var header = blob.ReadSignatureHeader();
        if (header.IsGeneric) blob.ReadCompressedInteger();
        int parameters = blob.ReadCompressedInteger();
        string TypeName(TypeDefinitionHandle handle)
        {
            var type = Baseline.GetTypeDefinition(handle);
            var name = Baseline.GetString(type.Name);
            var declaring = type.GetDeclaringType();
            if (!declaring.IsNil) return TypeName(declaring) + "/" + name;
            var ns = Baseline.GetString(type.Namespace);
            return ns.Length == 0 ? name : ns + "." + name;
        }
        return (TypeName(method.GetDeclaringType()), Baseline.GetString(method.Name), parameters);
    }

    public void Dispose()
    {
        foreach (var provider in m_Providers) provider.Dispose();
        m_Image.Dispose();
    }

    // ------------------------------------------------------------------ method bodies

    /// <summary>A method body's code range and local signature token (0: none) in an IL stream.</summary>
    internal static (int Start, int End, int Locals) Body(byte[] il, int offset)
    {
        if ((il[offset] & 0x3) == 0x2) // tiny header
            return (offset + 1, offset + 1 + (il[offset] >> 2), 0);
        int headerSize = (il[offset + 1] >> 4) * 4;
        int start = offset + headerSize;
        return (start, start + BitConverter.ToInt32(il, offset + 4), BitConverter.ToInt32(il, offset + 8));
    }

    /// <summary>The instructions of a code range: opcode (two-byte ones as 0xFExx), its offset and
    /// the offset of its operand.</summary>
    internal static IEnumerable<(int OpCode, int At, int Operand)> Instructions(byte[] il, int start, int end)
    {
        for (int at = start; at < end;)
        {
            int opcode = il[at] == 0xFE ? 0xFE00 | il[at + 1] : il[at];
            int operand = at + (opcode > 0xFF ? 2 : 1);
            yield return (opcode, at, operand);
            at = operand + OperandSize(opcode, il, operand);
        }
    }

    static readonly Dictionary<int, System.Reflection.Emit.OperandType> s_Operands = typeof(System.Reflection.Emit.OpCodes)
        .GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
        .Select(f => (System.Reflection.Emit.OpCode)f.GetValue(null)!)
        .ToDictionary(o => (int)(ushort)o.Value, o => o.OperandType);

    internal static System.Reflection.Emit.OperandType OperandType(int opcode) => s_Operands[opcode];

    /// <summary>Whether an opcode's operand is a metadata token.</summary>
    internal static bool HasToken(int opcode) => OperandType(opcode) is System.Reflection.Emit.OperandType.InlineField
        or System.Reflection.Emit.OperandType.InlineMethod or System.Reflection.Emit.OperandType.InlineType
        or System.Reflection.Emit.OperandType.InlineTok or System.Reflection.Emit.OperandType.InlineSig
        or System.Reflection.Emit.OperandType.InlineString;

    static int OperandSize(int opcode, byte[] il, int at) => OperandType(opcode) switch
    {
        System.Reflection.Emit.OperandType.InlineNone => 0,
        System.Reflection.Emit.OperandType.ShortInlineBrTarget or System.Reflection.Emit.OperandType.ShortInlineI
            or System.Reflection.Emit.OperandType.ShortInlineVar => 1,
        System.Reflection.Emit.OperandType.InlineVar => 2,
        System.Reflection.Emit.OperandType.InlineI8 or System.Reflection.Emit.OperandType.InlineR => 8,
        System.Reflection.Emit.OperandType.InlineSwitch => 4 + 4 * BitConverter.ToInt32(il, at),
        _ => 4,
    };
}
