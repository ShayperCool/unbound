using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata;
using Microsoft.CodeAnalysis;

namespace Unbound.ApiTools;

sealed partial class StubBuilder
{
    // These declarations use metadata flags/tables, so GetAttributes() does not return them.
    static string Marshal(MetadataReader reader, BlobHandle blob) => blob.IsNil ? "absent" : Convert.ToHexString(reader.GetBlobBytes(blob));

    void CheckStorageMetadata()
    {
        var reference = new Dictionary<string, (MetadataReader Reader, TypeDefinition Type)>(StringComparer.Ordinal);
        foreach (var (_, _, reader) in _modules)
            foreach (var handle in reader.TypeDefinitions)
                reference.TryAdd(MetadataNames.TypeDefinition(reader, handle).Replace('/', '+'), (reader, reader.GetTypeDefinition(handle)));
        foreach (var handle in _implMetadata.TypeDefinitions)
        {
            var name = MetadataNames.TypeDefinition(_implMetadata, handle).Replace('/', '+');
            var owner = _implAssembly.GetTypeByMetadataName(name);
            if (owner == null || FindUnity(owner) == null || !reference.TryGetValue(name, out var unity)) continue;
            var impl = _implMetadata.GetTypeDefinition(handle);
            static string Layout(TypeDefinition type)
            {
                var layout = type.GetLayout();
                return $"{type.Attributes & System.Reflection.TypeAttributes.LayoutMask},pack={layout.PackingSize},size={layout.Size}";
            }
            if (Layout(impl) != Layout(unity.Type))
                Error("layout", owner, $"storage layout is {Layout(impl)}, Unity's is {Layout(unity.Type)}");
            var fields = unity.Type.GetFields().Select(h => unity.Reader.GetFieldDefinition(h))
                .ToDictionary(f => unity.Reader.GetString(f.Name), StringComparer.Ordinal);
            foreach (var fieldHandle in impl.GetFields())
            {
                var field = _implMetadata.GetFieldDefinition(fieldHandle);
                var fieldName = _implMetadata.GetString(field.Name);
                if (!fields.TryGetValue(fieldName, out var other)) continue;
                // Compare all fields shared with the reference, including private serialized state.
                const FieldAttributes NotSerialized = (FieldAttributes)0x80;
                if ((field.Attributes & NotSerialized) != (other.Attributes & NotSerialized))
                    Error("attributes", owner, $"field {fieldName} NonSerialized is {(field.Attributes & NotSerialized) != 0}, Unity's is {(other.Attributes & NotSerialized) != 0}");
                if (Marshal(_implMetadata, field.GetMarshallingDescriptor()) != Marshal(unity.Reader, other.GetMarshallingDescriptor()))
                    Error("attributes", owner, $"field {fieldName} marshal is {Marshal(_implMetadata, field.GetMarshallingDescriptor())}, Unity's is {Marshal(unity.Reader, other.GetMarshallingDescriptor())}");
                if (field.GetOffset() != other.GetOffset())
                    Error("layout", owner, $"field {fieldName} offset is {field.GetOffset()}, Unity's is {other.GetOffset()}");
            }
            var methods = unity.Type.GetMethods().ToDictionary(h => unity.Reader.GetString(unity.Reader.GetMethodDefinition(h).Name)
                + ":" + CanonicalSignatures.MethodOf(unity.Reader, h));
            foreach (var methodHandle in impl.GetMethods())
            {
                var method = _implMetadata.GetMethodDefinition(methodHandle);
                var key = _implMetadata.GetString(method.Name) + ":" + CanonicalSignatures.MethodOf(_implMetadata, methodHandle);
                if (!methods.TryGetValue(key, out var otherHandle)) continue;
                var parameters = unity.Reader.GetMethodDefinition(otherHandle).GetParameters()
                    .Select(h => unity.Reader.GetParameter(h)).ToDictionary(p => p.SequenceNumber);
                var mine = method.GetParameters().Select(h => _implMetadata.GetParameter(h)).ToDictionary(p => p.SequenceNumber);
                foreach (var sequence in mine.Keys.Union(parameters.Keys))
                {
                    var hasMine = mine.TryGetValue(sequence, out var parameter);
                    var hasOther = parameters.TryGetValue(sequence, out var other);
                    const ParameterAttributes flags = ParameterAttributes.In | ParameterAttributes.Out;
                    var myFlags = hasMine ? parameter.Attributes & flags : 0;
                    var otherFlags = hasOther ? other.Attributes & flags : 0;
                    if (myFlags != otherFlags)
                        Error("parameters", owner, $"{key} metadata parameter[{sequence}] in/out flags are {myFlags}, Unity's are {otherFlags}");
                    var myMarshal = hasMine ? Marshal(_implMetadata, parameter.GetMarshallingDescriptor()) : "absent";
                    var otherMarshal = hasOther ? Marshal(unity.Reader, other.GetMarshallingDescriptor()) : "absent";
                    if (myMarshal != otherMarshal)
                        Error("attributes", owner, $"{key} metadata parameter[{sequence}] marshal is {myMarshal}, Unity's is {otherMarshal}");
                }
            }
        }
    }
}
