// Prints C# enum declarations for the given full type names, read from the metadata of every
// DLL in a directory (a Unity release's Editor/Data/Managed/UnityEngine). Usage: <dir> <names...>
using System; using System.Collections.Generic; using System.IO; using System.Linq; using System.Reflection.Metadata; using System.Reflection.PortableExecutable; using System.Text;
// usage: dir names... ; prints C# enum declarations (namespace-grouped) for the given full names found in dir/*.dll
var dir = args[0]; var wanted = new HashSet<string>(args.Skip(1));
var found = new Dictionary<string,string>();
foreach (var file in Directory.GetFiles(dir, "*.dll")) {
  using var pe = new PEReader(File.OpenRead(file)); if (!pe.HasMetadata) continue; var md = pe.GetMetadataReader();
  foreach (var th in md.TypeDefinitions) {
    var td = md.GetTypeDefinition(th);
    string FullName(TypeDefinition t) { var n = md.GetString(t.Name); if (t.IsNested) return FullName(md.GetTypeDefinition(t.GetDeclaringType())) + "." + n; var ns = md.GetString(t.Namespace); return ns.Length>0? ns+"."+n : n; }
    var full = FullName(td);
    if (!wanted.Contains(full) || found.ContainsKey(full)) continue;
    if (td.BaseType.Kind != HandleKind.TypeReference) continue;
    var bt = md.GetTypeReference((TypeReferenceHandle)td.BaseType); if (md.GetString(bt.Name) != "Enum") continue;
    var sb = new StringBuilder();
    bool flags = td.GetCustomAttributes().Any(h => { var a = md.GetCustomAttribute(h); if (a.Constructor.Kind != HandleKind.MemberReference) return false; var mr = md.GetMemberReference((MemberReferenceHandle)a.Constructor); return mr.Parent.Kind==HandleKind.TypeReference && md.GetString(md.GetTypeReference((TypeReferenceHandle)mr.Parent).Name)=="FlagsAttribute"; });
    string underlying = "int";
    var members = new List<string>();
    foreach (var fh in td.GetFields()) { var f = md.GetFieldDefinition(fh); var name = md.GetString(f.Name);
      if (name == "value__") { var sig = md.GetBlobReader(f.Signature); sig.ReadByte(); var code = sig.ReadSignatureTypeCode(); underlying = code switch { SignatureTypeCode.Byte=>"byte", SignatureTypeCode.SByte=>"sbyte", SignatureTypeCode.Int16=>"short", SignatureTypeCode.UInt16=>"ushort", SignatureTypeCode.UInt32=>"uint", SignatureTypeCode.Int64=>"long", SignatureTypeCode.UInt64=>"ulong", _=>"int" }; continue; }
      var ch = f.GetDefaultValue(); if (ch.IsNil) continue; var c = md.GetConstant(ch); var br = md.GetBlobReader(c.Value);
      object v = c.TypeCode switch { ConstantTypeCode.Byte=>br.ReadByte(), ConstantTypeCode.SByte=>br.ReadSByte(), ConstantTypeCode.Int16=>br.ReadInt16(), ConstantTypeCode.UInt16=>br.ReadUInt16(), ConstantTypeCode.Int32=>br.ReadInt32(), ConstantTypeCode.UInt32=>br.ReadUInt32(), ConstantTypeCode.Int64=>br.ReadInt64(), ConstantTypeCode.UInt64=>br.ReadUInt64(), _=>0 };
      bool obsolete = f.GetCustomAttributes().Any(h => { var a = md.GetCustomAttribute(h); if (a.Constructor.Kind != HandleKind.MemberReference) return false; var mr = md.GetMemberReference((MemberReferenceHandle)a.Constructor); return mr.Parent.Kind==HandleKind.TypeReference && md.GetString(md.GetTypeReference((TypeReferenceHandle)mr.Parent).Name)=="ObsoleteAttribute"; });
      members.Add($"        {(obsolete ? "[Obsolete] " : "")}{name} = {v},"); }
    var simple = md.GetString(td.Name);
    sb.AppendLine((flags ? "    [Flags]\n" : "") + $"    public enum {simple}" + (underlying=="int"?"":$" : {underlying}"));
    sb.AppendLine("    {"); foreach (var m in members) sb.AppendLine(m); sb.AppendLine("    }");
    found[full] = sb.ToString();
  }
}
foreach (var g in found.OrderBy(k=>k.Key).GroupBy(k => k.Key.Contains('.') ? k.Key.Substring(0, k.Key.LastIndexOf('.')) : "")) {
  Console.WriteLine($"namespace {g.Key}\n{{"); foreach (var kv in g) Console.WriteLine(kv.Value); Console.WriteLine("}\n"); }
foreach (var w in wanted) if (!found.ContainsKey(w)) Console.Error.WriteLine("missing: " + w);
