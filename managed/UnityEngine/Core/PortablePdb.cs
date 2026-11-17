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
        // Applied hot-reload edits, oldest first: the readers (kept with their providers), the
        // edited methods' newest debug information by MethodDef row, and the documents they add.
        readonly List<MetadataReaderProvider> m_Deltas = new();
        readonly Dictionary<int, (MetadataReader Reader, MethodDebugInformationHandle Handle)> m_Edited = new();
        readonly Dictionary<int, string> m_AddedDocuments = new();

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
        }

        internal void Add(byte[] delta)
        {
            var provider = MetadataReaderProvider.FromPortablePdbImage(ImmutableArray.Create(delta));
            var reader = provider.GetMetadataReader();
            m_Deltas.Add(provider);
            // A delta's rows of a table are listed, in order, by the rows they stand for (EncMap).
            int methods = 0, documents = 0;
            foreach (var entry in reader.GetEditAndContinueMapEntries())
            {
                int row = MetadataTokens.GetRowNumber(entry);
                if (entry.Kind == HandleKind.MethodDebugInformation)
                    m_Edited[row] = (reader, MetadataTokens.MethodDebugInformationHandle(++methods));
                else if (entry.Kind == HandleKind.Document)
                    m_AddedDocuments[row] = reader.GetString(reader.GetDocument(MetadataTokens.DocumentHandle(++documents)).Name);
            }
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
            lock (this)
            {
                if (m_Edited.TryGetValue(row, out var edited))
                    information = edited.Reader.GetMethodDebugInformation(edited.Handle);
                else if (row <= m_Pdb.GetTableRowCount(TableIndex.MethodDebugInformation))
                    information = m_Pdb.GetMethodDebugInformation(MetadataTokens.MethodDebugInformationHandle(row));
                else return false;
                bool found = false;
                foreach (var point in information.GetSequencePoints())
                {
                    if (point.Offset > ilOffset) break;
                    if (point.IsHidden) continue;
                    start = point.Offset;
                    document = DocumentName(point.Document);
                    line = point.StartLine;
                    found = true;
                }
                return found;
            }
        }

        string DocumentName(DocumentHandle handle)
        {
            int row = MetadataTokens.GetRowNumber(handle);
            if (m_AddedDocuments.TryGetValue(row, out var added)) return added;
            return row <= m_Pdb.GetTableRowCount(TableIndex.Document) ? m_Pdb.GetString(m_Pdb.GetDocument(handle).Name) : null;
        }

        /// <summary>Where the method <paramref name="typeName"/>.<paramref name="methodName"/> with
        /// <paramref name="parameterCount"/> parameters calls a method named
        /// <paramref name="callee"/> (its first such call): the statement's document and line.</summary>
        internal bool CallSite(string typeName, string methodName, int parameterCount, string callee, out string document, out int line)
        {
            document = null;
            line = 0;
            if (m_Image == null) return false;
            foreach (var typeHandle in m_Metadata.TypeDefinitions)
            {
                var type = m_Metadata.GetTypeDefinition(typeHandle);
                if (!type.GetDeclaringType().IsNil || FullName(type) != typeName) continue;
                foreach (var methodHandle in type.GetMethods())
                {
                    var method = m_Metadata.GetMethodDefinition(methodHandle);
                    if (m_Metadata.GetString(method.Name) != methodName || method.RelativeVirtualAddress == 0) continue;
                    if (method.GetParameters().Count < parameterCount) continue;
                    var il = m_Image.GetMethodBody(method.RelativeVirtualAddress).GetILBytes();
                    foreach (var (opcode, at, operand) in ILCode.Instructions(il, 0, il.Length))
                    {
                        if (opcode is not (0x28 or 0x6F)) continue; // call, callvirt
                        if (TargetName(BitConverter.ToInt32(il, operand)) != callee) continue;
                        return Statement(MetadataTokens.GetToken(methodHandle), at, out _, out document, out line);
                    }
                }
            }
            return false;
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
