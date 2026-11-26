using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Runtime.CompilerServices;

namespace Unbound.Runtime
{
    /// <summary>
    /// An assembly's portable debug information (its .pdb beside it or embedded): the statements
    /// (sequence points) of its methods. Read once per assembly, kept while the assembly lives.
    /// </summary>
    internal sealed class PortablePdb
    {
        // The assembly itself, kept only where its method bodies are read (CallSite).
        readonly PEReader m_Image;
        readonly MetadataReader m_Metadata;
        // Owns the memory m_Pdb reads.
        readonly MetadataReaderProvider m_Provider;
        readonly MetadataReader m_Pdb;
        // Applied hot-reload edits, oldest first (their providers own the readers' memory), and
        // the edited methods' newest debug information by MethodDef row.
        readonly List<MetadataReaderProvider> m_Deltas = new();
        readonly Dictionary<int, (MetadataReader Reader, MethodDebugInformationHandle Handle)> m_Edited = new();

        PortablePdb(PEReader image, MetadataReaderProvider provider)
        {
            m_Image = image;
            m_Metadata = image?.GetMetadataReader();
            m_Provider = provider;
            m_Pdb = provider.GetMetadataReader();
        }

        static readonly ConditionalWeakTable<Assembly, PortablePdb> s_Loaded = new();
        static readonly Dictionary<string, PortablePdb> s_Files = new(StringComparer.Ordinal);

        /// <summary>The .pdb of a loaded assembly (null without one).</summary>
        internal static PortablePdb Of(Assembly assembly)
        {
            if (assembly.IsDynamic || string.IsNullOrEmpty(assembly.Location)) return null;
            if (s_Loaded.TryGetValue(assembly, out var known)) return known;
            lock (s_Loaded)
            {
                if (!s_Loaded.TryGetValue(assembly, out var pdb))
                {
                    pdb = Open(assembly.Location, keepImage: false);
                    s_Loaded.Add(assembly, pdb);
                }
                return pdb;
            }
        }

        /// <summary>Adds a hot-reload edit's .pdb delta to a loaded assembly's: the methods it
        /// edits are read from it from then on.</summary>
        internal static void ApplyDelta(Assembly assembly, byte[] delta)
        {
            var pdb = Of(assembly);
            if (pdb == null || delta == null || delta.Length == 0) return;
            lock (pdb) pdb.Add(delta);
            Edited = true;
        }

        /// <summary>Whether a hot-reload edit was applied: until then the runtime's own lines of
        /// frames are the .pdb's.</summary>
        internal static volatile bool Edited;

        internal void Add(byte[] delta)
        {
            var provider = MetadataReaderProvider.FromPortablePdbImage(ImmutableArray.Create(delta));
            var reader = provider.GetMetadataReader();
            m_Deltas.Add(provider);
            // The delta's method rows stand, in order, for the methods its map lists (EncMap). Its
            // statements name documents of its own Document table: the compiler's numbering, which
            // the loaded .pdb need not keep (an adapted assembly's is rewritten).
            int methods = 0;
            foreach (var entry in reader.GetEditAndContinueMapEntries())
                if (entry.Kind == HandleKind.MethodDebugInformation)
                    m_Edited[MetadataTokens.GetRowNumber(entry)] = (reader, MetadataTokens.MethodDebugInformationHandle(++methods));
        }

        /// <summary>The .pdb of an assembly file that is not loaded (null without one).</summary>
        internal static PortablePdb OfFile(string path)
        {
            lock (s_Files)
            {
                if (!s_Files.TryGetValue(path, out var pdb))
                {
                    pdb = File.Exists(path) ? Open(path, keepImage: true) : null;
                    s_Files.Add(path, pdb);
                }
                return pdb;
            }
        }

        static PortablePdb Open(string path, bool keepImage)
        {
            try
            {
                // Read into memory: no file stays open (adapted assemblies are replaced on reload).
                var image = new PEReader(ImmutableArray.Create(File.ReadAllBytes(path)));
                bool found = image.TryOpenAssociatedPortablePdb(path,
                    pdb => File.Exists(pdb) ? new MemoryStream(File.ReadAllBytes(pdb), false) : null,
                    out var provider, out _) && provider != null;
                if (!keepImage || !found) image.Dispose();
                return found ? new PortablePdb(keepImage ? image : null, provider) : null;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or BadImageFormatException)
            {
                return null;
            }
        }

        /// <summary>The statement holding the instruction at <paramref name="ilOffset"/> of the method
        /// with metadata token <paramref name="methodToken"/>: where it starts, its document and its
        /// first line. Hidden statements belong to the visible one before them.</summary>
        internal bool Statement(int methodToken, int ilOffset, out int start, out string document, out int line)
        {
            start = 0;
            document = null;
            line = 0;
            if ((methodToken >> 24) != 0x06) return false;
            int row = methodToken & 0xFFFFFF;
            MethodDebugInformation information;
            MetadataReader reader = m_Pdb;
            lock (this)
            {
                if (m_Edited.TryGetValue(row, out var edited))
                    information = (reader = edited.Reader).GetMethodDebugInformation(edited.Handle);
                else if (row <= m_Pdb.GetTableRowCount(TableIndex.MethodDebugInformation))
                    information = m_Pdb.GetMethodDebugInformation(MetadataTokens.MethodDebugInformationHandle(row));
                else return false;
                bool found = false;
                foreach (var point in information.GetSequencePoints())
                {
                    if (point.Offset > ilOffset) break;
                    if (point.IsHidden) continue;
                    start = point.Offset;
                    document = DocumentName(reader, point.Document);
                    line = point.StartLine;
                    found = true;
                }
                return found;
            }
        }

        static string DocumentName(MetadataReader reader, DocumentHandle handle) =>
            MetadataTokens.GetRowNumber(handle) <= reader.GetTableRowCount(TableIndex.Document)
                ? reader.GetString(reader.GetDocument(handle).Name)
                : null;

        /// <summary>Where the method <paramref name="typeName"/>.<paramref name="methodName"/> with
        /// <paramref name="parameterCount"/> parameters calls a method named
        /// <paramref name="callee"/> (its first such call): the statement's document and line.</summary>
        internal bool CallSite(string typeName, string methodName, int parameterCount, string callee, out string document, out int line) =>
            CallSite(typeName, methodName, parameterCount, null, callee, null, -1, out _, out document, out line);

        /// <summary>Where the method <paramref name="typeName"/>.<paramref name="methodName"/> with
        /// <paramref name="parameterCount"/> parameters calls a method named <paramref name="callee"/>:
        /// its first such call, or, given the called method's declaring type (a generic one by its
        /// definition, <c>Namespace.Name`1</c>) and parameter count, the first call of that method
        /// (the first by name when none is). The method is the one whose parameter types are
        /// <paramref name="parameterTypes"/> (<see cref="ParameterTypeName"/>), else the first with
        /// as many parameters. The statement's start, document and line.</summary>
        internal bool CallSite(string typeName, string methodName, int parameterCount, string[] parameterTypes, string callee, string calleeOwner,
            int calleeParameters, out int start, out string document, out int line)
        {
            start = 0;
            document = null;
            line = 0;
            if (m_Image == null) return false;
            MethodDefinitionHandle found = default;
            foreach (var typeHandle in m_Metadata.TypeDefinitions)
            {
                var type = m_Metadata.GetTypeDefinition(typeHandle);
                if (!type.GetDeclaringType().IsNil || FullName(type) != typeName) continue;
                foreach (var methodHandle in type.GetMethods())
                {
                    var method = m_Metadata.GetMethodDefinition(methodHandle);
                    if (m_Metadata.GetString(method.Name) != methodName || method.RelativeVirtualAddress == 0) continue;
                    if (ParameterCount(method.Signature) != parameterCount) continue;
                    if (parameterTypes != null && !SameTypes(ParameterTypes(method.Signature), parameterTypes))
                    {
                        if (found.IsNil) found = methodHandle;
                        continue;
                    }
                    found = methodHandle;
                    break;
                }
                if (!found.IsNil) break;
            }
            if (found.IsNil) return false;
            var il = m_Image.GetMethodBody(m_Metadata.GetMethodDefinition(found).RelativeVirtualAddress).GetILBytes();
            int first = -1, chosen = -1;
            foreach (var (opcode, at, operand) in ILCode.Instructions(il, 0, il.Length))
            {
                if (opcode is not (0x28 or 0x6F)) continue; // call, callvirt
                int token = BitConverter.ToInt32(il, operand);
                if (TargetName(token) != callee) continue;
                if (first < 0) first = at;
                if (calleeOwner == null) break;
                if (TargetOwner(token) == calleeOwner && TargetParameterCount(token) == calleeParameters) { chosen = at; break; }
            }
            if (first < 0) return false;
            return Statement(MetadataTokens.GetToken(found), chosen >= 0 ? chosen : first, out start, out document, out line);
        }

        static bool SameTypes(string[] a, string[] b)
        {
            if (a == null || a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++)
                if (a[i] != b[i]) return false;
            return true;
        }

        /// <summary>The parameter types of a method signature blob (<see cref="ParameterTypeName"/>),
        /// null for a form not read here.</summary>
        string[] ParameterTypes(BlobHandle signature)
        {
            try
            {
                var reader = m_Metadata.GetBlobReader(signature);
                var header = reader.ReadSignatureHeader();
                if (header.IsGeneric) reader.ReadCompressedInteger();
                int count = reader.ReadCompressedInteger();
                SignatureTypeName(ref reader); // return type
                var names = new string[count];
                for (int i = 0; i < count; i++) names[i] = SignatureTypeName(ref reader);
                return names;
            }
            catch (BadImageFormatException) { return null; }
        }

        /// <summary>A type of a signature as <see cref="ParameterTypeName"/> names it.</summary>
        string SignatureTypeName(ref BlobReader reader)
        {
            var code = reader.ReadSignatureTypeCode();
            switch (code)
            {
                case SignatureTypeCode.Void: return "System.Void";
                case SignatureTypeCode.Boolean: return "System.Boolean";
                case SignatureTypeCode.Char: return "System.Char";
                case SignatureTypeCode.SByte: return "System.SByte";
                case SignatureTypeCode.Byte: return "System.Byte";
                case SignatureTypeCode.Int16: return "System.Int16";
                case SignatureTypeCode.UInt16: return "System.UInt16";
                case SignatureTypeCode.Int32: return "System.Int32";
                case SignatureTypeCode.UInt32: return "System.UInt32";
                case SignatureTypeCode.Int64: return "System.Int64";
                case SignatureTypeCode.UInt64: return "System.UInt64";
                case SignatureTypeCode.Single: return "System.Single";
                case SignatureTypeCode.Double: return "System.Double";
                case SignatureTypeCode.String: return "System.String";
                case SignatureTypeCode.Object: return "System.Object";
                case SignatureTypeCode.IntPtr: return "System.IntPtr";
                case SignatureTypeCode.UIntPtr: return "System.UIntPtr";
                case SignatureTypeCode.TypedReference: return "System.TypedReference";
                case SignatureTypeCode.SZArray: return SignatureTypeName(ref reader) + "[]";
                case SignatureTypeCode.Pointer: return SignatureTypeName(ref reader) + "*";
                case SignatureTypeCode.ByReference: return SignatureTypeName(ref reader) + "&";
                case SignatureTypeCode.GenericTypeParameter: return "!" + reader.ReadCompressedInteger();
                case SignatureTypeCode.GenericMethodParameter: return "!!" + reader.ReadCompressedInteger();
                case SignatureTypeCode.TypeHandle: return TypeName(reader.ReadTypeHandle());
                case SignatureTypeCode.GenericTypeInstance:
                {
                    reader.ReadSignatureTypeCode();
                    string name = TypeName(reader.ReadTypeHandle());
                    int arguments = reader.ReadCompressedInteger();
                    for (int i = 0; i < arguments; i++) SignatureTypeName(ref reader);
                    return name;
                }
                default: throw new BadImageFormatException("a signature form not read here: " + code);
            }
        }

        /// <summary>How a parameter type is compared with a signature's: <c>Namespace.Name</c>, a
        /// constructed generic type by its definition's, arrays <c>[]</c>, pointers <c>*</c>,
        /// references <c>&amp;</c>, a type's generic parameter <c>!n</c>, a method's <c>!!n</c>.</summary>
        internal static string ParameterTypeName(Type type)
        {
            if (type.IsByRef) return ParameterTypeName(type.GetElementType()) + "&";
            if (type.IsPointer) return ParameterTypeName(type.GetElementType()) + "*";
            if (type.IsSZArray) return ParameterTypeName(type.GetElementType()) + "[]";
            if (type.IsGenericParameter) return (type.DeclaringMethod != null ? "!!" : "!") + type.GenericParameterPosition;
            if (type.IsConstructedGenericType) type = type.GetGenericTypeDefinition();
            return string.IsNullOrEmpty(type.Namespace) ? type.Name : type.Namespace + "." + type.Name;
        }

        /// <summary>The parameter count a method signature blob declares.</summary>
        int ParameterCount(BlobHandle signature)
        {
            var reader = m_Metadata.GetBlobReader(signature);
            var header = reader.ReadSignatureHeader();
            if (header.IsGeneric) reader.ReadCompressedInteger();
            return reader.ReadCompressedInteger();
        }

        int TargetParameterCount(int token)
        {
            var handle = MetadataTokens.EntityHandle(token);
            switch (handle.Kind)
            {
                case HandleKind.MethodDefinition:
                    return ParameterCount(m_Metadata.GetMethodDefinition((MethodDefinitionHandle)handle).Signature);
                case HandleKind.MemberReference:
                    return ParameterCount(m_Metadata.GetMemberReference((MemberReferenceHandle)handle).Signature);
                case HandleKind.MethodSpecification:
                    return TargetParameterCount(MetadataTokens.GetToken(m_Metadata.GetMethodSpecification((MethodSpecificationHandle)handle).Method));
                default:
                    return -1;
            }
        }

        /// <summary>The declaring type of a called method: <c>Namespace.Name</c>, a generic
        /// instantiation by its definition's name.</summary>
        string TargetOwner(int token)
        {
            var handle = MetadataTokens.EntityHandle(token);
            switch (handle.Kind)
            {
                case HandleKind.MethodDefinition:
                    return FullName(m_Metadata.GetTypeDefinition(m_Metadata.GetMethodDefinition((MethodDefinitionHandle)handle).GetDeclaringType()));
                case HandleKind.MemberReference:
                    return TypeName(m_Metadata.GetMemberReference((MemberReferenceHandle)handle).Parent);
                case HandleKind.MethodSpecification:
                    return TargetOwner(MetadataTokens.GetToken(m_Metadata.GetMethodSpecification((MethodSpecificationHandle)handle).Method));
                default:
                    return null;
            }
        }

        string TypeName(EntityHandle handle)
        {
            switch (handle.Kind)
            {
                case HandleKind.TypeDefinition:
                    return FullName(m_Metadata.GetTypeDefinition((TypeDefinitionHandle)handle));
                case HandleKind.TypeReference:
                {
                    var reference = m_Metadata.GetTypeReference((TypeReferenceHandle)handle);
                    string ns = m_Metadata.GetString(reference.Namespace);
                    string name = m_Metadata.GetString(reference.Name);
                    return ns.Length == 0 ? name : ns + "." + name;
                }
                case HandleKind.TypeSpecification:
                {
                    var reader = m_Metadata.GetBlobReader(m_Metadata.GetTypeSpecification((TypeSpecificationHandle)handle).Signature);
                    if (reader.ReadSignatureTypeCode() != SignatureTypeCode.GenericTypeInstance) return null;
                    reader.ReadSignatureTypeCode(); // class or value type
                    return TypeName(reader.ReadTypeHandle());
                }
                default:
                    return null;
            }
        }

        string FullName(TypeDefinition type)
        {
            string ns = m_Metadata.GetString(type.Namespace);
            return ns.Length == 0 ? m_Metadata.GetString(type.Name) : ns + "." + m_Metadata.GetString(type.Name);
        }

        string TargetName(int token)
        {
            var handle = MetadataTokens.EntityHandle(token);
            switch (handle.Kind)
            {
                case HandleKind.MethodDefinition:
                    return m_Metadata.GetString(m_Metadata.GetMethodDefinition((MethodDefinitionHandle)handle).Name);
                case HandleKind.MemberReference:
                    return m_Metadata.GetString(m_Metadata.GetMemberReference((MemberReferenceHandle)handle).Name);
                case HandleKind.MethodSpecification:
                    return TargetName(MetadataTokens.GetToken(m_Metadata.GetMethodSpecification((MethodSpecificationHandle)handle).Method));
                default:
                    return null;
            }
        }
    }
}
