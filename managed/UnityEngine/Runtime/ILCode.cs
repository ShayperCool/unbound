using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;

namespace Unbound.Runtime
{
    /// <summary>Decoding of method body IL (ECMA-335 III): its instructions and their operands.</summary>
    internal static class ILCode
    {
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

        static readonly Dictionary<int, OperandType> s_Operands = typeof(OpCodes)
            .GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
            .Select(f => (OpCode)f.GetValue(null)!)
            .ToDictionary(o => (int)(ushort)o.Value, o => o.OperandType);

        internal static OperandType OperandType(int opcode) => s_Operands[opcode];

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
}
