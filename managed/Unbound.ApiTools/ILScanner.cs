using System;
using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;

namespace Unbound.ApiTools;

/// <summary>Walks IL method bodies and reports the metadata tokens their instructions use.</summary>
static class ILScanner
{
    static readonly OpCode?[] OneByte = new OpCode?[256];
    static readonly OpCode?[] TwoByte = new OpCode?[256];

    static ILScanner()
    {
        foreach (var field in typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static))
        {
            if (field.GetValue(null) is not OpCode op)
                continue;
            var value = (ushort)op.Value;
            if (op.Size == 1)
                OneByte[value] = op;
            else
                TwoByte[value & 0xFF] = op;
        }
    }

    /// <summary>Calls <paramref name="onToken"/> for every token operand (method, field, type and
    /// <c>ldtoken</c> operands). Stops at an invalid opcode: obfuscated plugins may carry junk.</summary>
    public static void Tokens(BlobReader il, Action<int> onToken)
    {
        while (il.RemainingBytes > 0)
        {
            var first = il.ReadByte();
            var op = first == 0xFE ? (il.RemainingBytes > 0 ? TwoByte[il.ReadByte()] : null) : OneByte[first];
            if (op == null)
                return;
            switch (op.Value.OperandType)
            {
                case OperandType.InlineNone:
                    break;
                case OperandType.ShortInlineBrTarget:
                case OperandType.ShortInlineI:
                case OperandType.ShortInlineVar:
                    il.Offset += 1;
                    break;
                case OperandType.InlineVar:
                    il.Offset += 2;
                    break;
                case OperandType.InlineBrTarget:
                case OperandType.InlineI:
                case OperandType.ShortInlineR:
                case OperandType.InlineString:
                case OperandType.InlineSig:
                    il.Offset += 4;
                    break;
                case OperandType.InlineI8:
                case OperandType.InlineR:
                    il.Offset += 8;
                    break;
                case OperandType.InlineSwitch:
                    var count = il.ReadInt32();
                    il.Offset += 4 * count;
                    break;
                case OperandType.InlineField:
                case OperandType.InlineMethod:
                case OperandType.InlineTok:
                case OperandType.InlineType:
                    onToken(il.ReadInt32());
                    break;
                default:
                    return;
            }
        }
    }
}
