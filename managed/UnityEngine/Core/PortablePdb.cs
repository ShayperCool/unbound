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
        readonly PEReader m_Image;
        readonly MetadataReader m_Metadata;
        readonly MetadataReader m_Pdb;

        PortablePdb(PEReader image, MetadataReader pdb)
        {
            m_Image = image;
            m_Metadata = image.GetMetadataReader();
            m_Pdb = pdb;
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
                    pdb = Open(assembly.Location);
                    s_Loaded.Add(assembly, pdb);
                }
                return pdb;
            }
        }

        /// <summary>The .pdb of an assembly file that is not loaded (null without one).</summary>
        internal static PortablePdb OfFile(string path)
        {
            lock (s_Files)
            {
                if (!s_Files.TryGetValue(path, out var pdb))
                {
                    pdb = File.Exists(path) ? Open(path) : null;
                    s_Files.Add(path, pdb);
                }
                return pdb;
            }
        }

        static PortablePdb Open(string path)
        {
            try
            {
                // Read into memory: no file stays open (adapted assemblies are replaced on reload).
                var image = new PEReader(ImmutableArray.Create(File.ReadAllBytes(path)));
                if (!image.TryOpenAssociatedPortablePdb(path,
                        pdb => File.Exists(pdb) ? new MemoryStream(File.ReadAllBytes(pdb), false) : null,
                        out var provider, out _) || provider == null)
                {
                    image.Dispose();
                    return null;
                }
                return new PortablePdb(image, provider.GetMetadataReader());
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
            var method = MetadataTokens.MethodDefinitionHandle(methodToken & 0xFFFFFF);
            if (MetadataTokens.GetRowNumber(method) > m_Pdb.GetTableRowCount(TableIndex.MethodDebugInformation)) return false;
            bool found = false;
            foreach (var point in m_Pdb.GetMethodDebugInformation(method).GetSequencePoints())
            {
                if (point.Offset > ilOffset) break;
                if (point.IsHidden) continue;
                start = point.Offset;
                document = m_Pdb.GetString(m_Pdb.GetDocument(point.Document).Name);
                line = point.StartLine;
                found = true;
            }
            return found;
        }

        /// <summary>Where the method <paramref name="typeName"/>.<paramref name="methodName"/> with
        /// <paramref name="parameterCount"/> parameters calls a method named
        /// <paramref name="callee"/> (its first such call): the statement's document and line.</summary>
        internal bool CallSite(string typeName, string methodName, int parameterCount, string callee, out string document, out int line)
        {
            document = null;
            line = 0;
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
