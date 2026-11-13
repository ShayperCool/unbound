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

    /// <summary>The offsets in a delta's IL of the bodies of the methods it adds or replaces.</summary>
    internal IEnumerable<(MethodDefinition Method, int Offset)> Bodies()
    {
        foreach (var handle in Delta.MethodDefinitions)
        {
            var method = Delta.GetMethodDefinition(handle);
            if (method.RelativeVirtualAddress != 0) yield return (method, method.RelativeVirtualAddress);
        }
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
