using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using Microsoft.CodeAnalysis;

namespace Unbound.ApiTools;

sealed partial class StubBuilder
{
    // Removed compatibility getters have no state to emulate. Read the selected release's
    // fixed exception, rather than inferring its text from the Obsolete attribute.
    string? FixedGetterThrow(IMethodSymbol getter)
    {
        if (!AccessorBody(getter, out var il, out var md)) return null;
        if (!Instruction(ref il, 0x72) || il.RemainingBytes < 4) return null; // ldstr
        var messageToken = MetadataTokens.Handle(il.ReadInt32());
        if (messageToken.Kind != HandleKind.UserString) return null;
        var message = md.GetUserString((UserStringHandle)messageToken);
        if (!Instruction(ref il, 0x73) || il.RemainingBytes < 4) return null; // newobj
        var constructorToken = MetadataTokens.EntityHandle(il.ReadInt32());
        if (constructorToken.Kind != HandleKind.MemberReference) return null;
        var constructor = md.GetMemberReference((MemberReferenceHandle)constructorToken);
        if (md.GetString(constructor.Name) != ".ctor" || constructor.Parent.Kind != HandleKind.TypeReference)
            return null;
        var exception = md.GetTypeReference((TypeReferenceHandle)constructor.Parent);
        if (md.GetString(exception.Namespace) != "System" || md.GetString(exception.Name) != "NotSupportedException")
            return null;
        // Instance constructor with exactly one string parameter and no other work in the getter.
        var signature = md.GetBlobReader(constructor.Signature);
        if (signature.ReadSignatureHeader().RawValue != 0x20 || signature.ReadCompressedInteger() != 1
            || signature.ReadSignatureTypeCode() != SignatureTypeCode.Void
            || signature.ReadSignatureTypeCode() != SignatureTypeCode.String || signature.RemainingBytes != 0
            || !Instruction(ref il, 0x7a)) return null; // throw
        while (il.RemainingBytes > 0)
            if (il.ReadByte() != 0) return null;
        return $"throw new global::System.NotSupportedException({Quote(message)});";
    }

    static bool Instruction(ref BlobReader il, byte expected)
    {
        while (il.RemainingBytes > 0)
        {
            var code = il.ReadByte();
            if (code != 0) return code == expected; // optional nop
        }
        return false;
    }
}
