using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using Microsoft.CodeAnalysis;

namespace Unbound.ApiTools;

sealed partial class StubBuilder
{
    bool AccessorBody(IMethodSymbol accessor, out BlobReader il, out MetadataReader metadata)
    {
        il = default; metadata = null!;
        if (!_moduleImages.TryGetValue(accessor.ContainingAssembly, out var pe) || accessor.MetadataToken == 0)
            return false;
        metadata = pe.GetMetadataReader();
        var method = metadata.GetMethodDefinition((MethodDefinitionHandle)MetadataTokens.EntityHandle(accessor.MetadataToken));
        if (method.RelativeVirtualAddress == 0) return false;
        var body = pe.GetMethodBody(method.RelativeVirtualAddress);
        if (body.ExceptionRegions.Length != 0) return false;
        il = body.GetILReader();
        return true;
    }

    static int NextCode(ref BlobReader il)
    {
        while (il.RemainingBytes > 0)
        {
            var code = il.ReadByte();
            if (code != 0) return code;
        }
        return -1;
    }

    bool EmptyAccessor(IMethodSymbol accessor) => AccessorBody(accessor, out var il, out _)
        && NextCode(ref il) == 0x2a && NextCode(ref il) == -1;

    static bool LiteralReturn(ref BlobReader il)
    {
        var code = NextCode(ref il);
        if (code != 0x2a)
        {
            // A block getter can return its literal through a temporary local.
            int local;
            if (code is >= 0x0a and <= 0x0d) local = code - 0x0a;
            else if (code == 0x13 && il.RemainingBytes >= 1) local = il.ReadByte();
            else return false;
            code = NextCode(ref il);
            if (code == 0x2b)
            {
                if (il.RemainingBytes < 1 || il.ReadSByte() != 0) return false;
                code = NextCode(ref il);
            }
            else if (code == 0x38)
            {
                if (il.RemainingBytes < 4 || il.ReadInt32() != 0) return false;
                code = NextCode(ref il);
            }
            if (code is >= 0x06 and <= 0x09)
            {
                if (code - 0x06 != local) return false;
            }
            else if (code == 0x11 && il.RemainingBytes >= 1)
            {
                if (il.ReadByte() != local) return false;
            }
            else return false;
            if (NextCode(ref il) != 0x2a) return false;
        }
        return NextCode(ref il) == -1;
    }

    // Stateless compatibility accessors retain their literal without an importer object.
    // Reject any body doing additional work, including field reads and calls.
    string? LiteralAccessor(IMethodSymbol accessor)
    {
        if (!AccessorBody(accessor, out var il, out var metadata)) return null;
        var type = accessor.ReturnType;
        var special = type.SpecialType;
        object? value;
        var code = NextCode(ref il);
        if (code == 0x14 && type.IsReferenceType) value = null;
        else if (code == 0x72 && il.RemainingBytes >= 4
            && special is SpecialType.System_String or SpecialType.System_Object)
        {
            var token = MetadataTokens.Handle(il.ReadInt32());
            if (token.Kind != HandleKind.UserString) return null;
            value = metadata.GetUserString((UserStringHandle)token);
        }
        else if (code is >= 0x15 and <= 0x20 && (type.TypeKind == TypeKind.Enum
            || special is SpecialType.System_Boolean or SpecialType.System_Char
                or SpecialType.System_SByte or SpecialType.System_Byte or SpecialType.System_Int16
                or SpecialType.System_UInt16 or SpecialType.System_Int32 or SpecialType.System_UInt32))
        {
            int number;
            if (code <= 0x1e) number = code - 0x16;
            else if (code == 0x1f && il.RemainingBytes >= 1) number = il.ReadSByte();
            else if (code == 0x20 && il.RemainingBytes >= 4) number = il.ReadInt32();
            else return null;
            value = number;
            if (special == SpecialType.System_Boolean)
            {
                if (number is not (0 or 1)) return null;
                value = number != 0;
            }
            else if (special == SpecialType.System_Char) value = unchecked((char)number);
            else if (special == SpecialType.System_UInt32) value = unchecked((uint)number);
        }
        else if (code == 0x21 && il.RemainingBytes >= 8
            && special is SpecialType.System_Int64 or SpecialType.System_UInt64)
        {
            var number = il.ReadInt64();
            value = special == SpecialType.System_UInt64 ? (object)unchecked((ulong)number) : number;
        }
        else if (code == 0x22 && il.RemainingBytes >= 4 && special == SpecialType.System_Single) value = il.ReadSingle();
        else if (code == 0x23 && il.RemainingBytes >= 8 && special == SpecialType.System_Double) value = il.ReadDouble();
        else return null;
        if (!LiteralReturn(ref il)) return null;
        return ConstantFor(type, value);
    }
}
