using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Unbound.Runtime
{
    /// <summary>A node of a settings class's type tree, as the Editor's SerializedObjects iterate it
    /// (<c>Editor/Data/settings-types-&lt;family&gt;.json</c>, from tests/projects/settings).</summary>
    internal sealed class TypeTreeNode
    {
        internal string Name;
        internal string TypeName;
        internal SerializedPropertyType Type;
        internal bool Hidden;
        internal bool ReadOnly;
        /// <summary>A new element's value (scalars).</summary>
        internal JsonElement? Default;
        internal TypeTreeNode[] Children = Array.Empty<TypeTreeNode>();

        internal TypeTreeNode Child(string name)
        {
            foreach (var c in Children)
                if (c.Name == name) return c;
            return null;
        }

        internal static TypeTreeNode Read(JsonElement e)
        {
            var node = new TypeTreeNode
            {
                Name = e.GetProperty("n").GetString(),
                TypeName = e.GetProperty("t").GetString(),
                Type = (SerializedPropertyType)e.GetProperty("p").GetInt32(),
                Hidden = e.TryGetProperty("h", out var h) && h.GetBoolean(),
                ReadOnly = e.TryGetProperty("r", out var r) && r.GetBoolean(),
            };
            if (e.TryGetProperty("d", out var d)) node.Default = d.Clone();
            if (e.TryGetProperty("c", out var c)) node.Children = c.EnumerateArray().Select(Read).ToArray();
            return node;
        }
    }

    /// <summary>A file of ProjectSettings/: its object (as AssetDatabase loads it), its text as
    /// read and as edited, and whether it was edited since saved.</summary>
    internal sealed class SettingsFile
    {
        internal string Name;
        internal string Guid;
        internal string ClassName;
        internal string ObjectName;
        internal TypeTreeNode[] Tree;
        internal List<YTextDocument> Documents;
        internal Object Object;
        internal bool Dirty;

        internal string AssetPath => "ProjectSettings/" + Name + ".asset";
        internal string FilePath => Path.Combine(ProjectInfo.ProjectPath ?? "", "ProjectSettings", Name + ".asset");
        internal YText Body => Documents != null && Documents.Count > 0 ? Documents[0].Body : null;
    }

    /// <summary>
    /// The project's settings assets as editor code loads them (AssetDatabase at
    /// <c>ProjectSettings/*.asset</c>, <c>Unsupported.GetSerializedAssetInterfaceSingleton</c>):
    /// objects of the Editor's classes (a plain <c>Object</c> for those without a managed class),
    /// with the Editor's GUIDs and names, whose SerializedObjects iterate their type trees. Applied
    /// edits take effect at once (input axes, tags and layers, the product name, physics and time);
    /// AssetDatabase.SaveAssets writes edited files as the Editor does, in Edit mode only (measured:
    /// edits made in Play mode are not saved).
    /// </summary>
    internal static class SettingsAssets
    {
        static Dictionary<string, SettingsFile> s_Files;
        static readonly Dictionary<Object, SettingsFile> s_ByObject = new Dictionary<Object, SettingsFile>();

        static Dictionary<string, SettingsFile> Files
        {
            get
            {
                if (s_Files != null) return s_Files;
                var files = new Dictionary<string, SettingsFile>(StringComparer.OrdinalIgnoreCase);
                var family = "6000.0";
                var asm = typeof(SettingsAssets).Assembly;
                using var stream = asm.GetManifestResourceStream($"Unbound.settings-types-{family}.json");
                if (stream != null)
                {
                    using var doc = JsonDocument.Parse(stream);
                    foreach (var p in doc.RootElement.EnumerateObject())
                    {
                        files[p.Name] = new SettingsFile
                        {
                            Name = p.Name,
                            Guid = p.Value.GetProperty("guid").GetString(),
                            ClassName = p.Value.GetProperty("class").GetString(),
                            ObjectName = p.Value.GetProperty("name").GetString(),
                            Tree = p.Value.GetProperty("tree").EnumerateArray().Select(TypeTreeNode.Read).ToArray(),
                        };
                    }
                }
                return s_Files = files;
            }
        }

        static string FileName(string assetPath)
        {
            if (assetPath == null) return null;
            var p = assetPath.Replace('\\', '/');
            if (!p.StartsWith("ProjectSettings/", StringComparison.Ordinal) || !p.EndsWith(".asset", StringComparison.Ordinal)) return null;
            var name = p.Substring("ProjectSettings/".Length, p.Length - "ProjectSettings/".Length - ".asset".Length);
            return name.IndexOf('/') >= 0 ? null : name;
        }

        /// <summary>The file at a path when it is a settings asset the project has.</summary>
        internal static SettingsFile AtPath(string assetPath)
        {
            var name = FileName(assetPath);
            if (name == null || !Files.TryGetValue(name, out var file)) return null;
            return Load(file) ? file : null;
        }

        internal static SettingsFile ByGuid(string guid)
        {
            if (string.IsNullOrEmpty(guid)) return null;
            foreach (var f in Files.Values)
                if (f.Guid == guid) return Load(f) ? f : null;
            return null;
        }

        /// <summary>Forgets the loaded files (another project; tests).</summary>
        internal static void Reset()
        {
            s_Files = null;
            s_ByObject.Clear();
        }

        internal static SettingsFile Of(Object o) => o is null ? null : s_ByObject.TryGetValue(o, out var f) ? f : null;

        internal static bool IsSettings(Object o) => Of(o) != null;

        /// <summary>The object of a settings class by its class or object name (Unsupported's
        /// singletons: "PlayerSettings", "PhysicsManager", "NavMeshProjectSettings", ...).</summary>
        internal static Object Singleton(string name)
        {
            foreach (var f in Files.Values)
            {
                var shortName = f.ClassName.Substring(f.ClassName.LastIndexOf('.') + 1);
                if ((shortName == name && f.ClassName != "UnityEngine.Object") || f.ObjectName == name)
                    return Load(f) ? f.Object : null;
            }
            return null;
        }

        static bool Load(SettingsFile file)
        {
            if (file.Object != null) return true;
            if (string.IsNullOrEmpty(ProjectInfo.ProjectPath) || !File.Exists(file.FilePath)) return false;
            var text = File.ReadAllText(file.FilePath);
            if (!text.StartsWith("%YAML", StringComparison.Ordinal)) return false;
            file.Documents = UnityYamlText.Parse(text);
            var type = FindType(file.ClassName) ?? typeof(Object);
            Object obj;
            using (ObjectRegistry.AsAssets())
            {
                try { obj = (Object)Activator.CreateInstance(type, nonPublic: true); }
                catch (Exception) { obj = (Object)RuntimeHelpers.GetUninitializedObject(type); }
            }
            obj.m_Name = file.ObjectName;
            obj.IsPersistentAsset = true;
            file.Object = obj;
            s_ByObject[obj] = file;
            return true;
        }

        static Type FindType(string fullName)
        {
            if (fullName == "UnityEngine.Object") return typeof(Object);
            var t = typeof(Object).Assembly.GetType(fullName);
            return t != null && typeof(Object).IsAssignableFrom(t) && !t.IsAbstract ? t : null;
        }

        // ---------------------------------------------------------------- edits

        /// <summary>A SerializedObject applied changes to a settings object: the engine takes them up.</summary>
        internal static void Applied(SettingsFile file)
        {
            file.Dirty = true;
            if (file.Object != null) EditorUtility.SetDirty(file.Object);
            var body = file.Body?.ToNode();
            if (body == null) return;
            try
            {
                switch (file.Name)
                {
                    case "InputManager": ApplyInput(body); break;
                    case "TagManager": ApplyTags(body); break;
                    case "ProjectSettings": ApplyPlayer(body); break;
                    case "DynamicsManager": Physics.EnsureSettings(); Physics.ReadSettings(body); break;
                    case "Physics2DSettings": Physics2D.EnsureSettings(); Physics2D.ReadSettings(body); break;
                    case "TimeManager": ApplyTime(body); break;
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"Unbound: applying {file.AssetPath}: {e.Message}");
            }
        }

        static void ApplyInput(YNode body)
        {
            var axes = new List<Input.InputAxis>();
            foreach (var a in body.Get("m_Axes")?.Seq() ?? Array.Empty<YNode>())
            {
                axes.Add(new Input.InputAxis
                {
                    Name = a.Str("m_Name"), Negative = a.Str("negativeButton"), Positive = a.Str("positiveButton"),
                    AltNegative = a.Str("altNegativeButton"), AltPositive = a.Str("altPositiveButton"),
                    Gravity = a.Float("gravity"), Dead = a.Float("dead"), Sensitivity = a.Float("sensitivity"),
                    Snap = a.Bool("snap"), Invert = a.Bool("invert"), Type = a.Int("type"), Axis = a.Int("axis"), JoyNum = a.Int("joyNum"),
                });
            }
            Input.ConfigureAxes(axes);
        }

        static void ApplyTags(YNode body)
        {
            TagManager.SetProjectTags(body.Get("tags")?.Seq().Select(t => t.AsString()) ?? Array.Empty<string>(), replace: true);
            int i = 0;
            foreach (var layer in body.Get("layers")?.Seq() ?? Array.Empty<YNode>())
            {
                if (i >= 32) break;
                TagManager.Layers[i++] = layer.AsString();
            }
            var sorting = body.Get("m_SortingLayers")?.Seq().ToList();
            if (sorting != null && sorting.Count > 0)
            {
                SortingLayer.s_Layers.Clear();
                foreach (var l in sorting) SortingLayer.s_Layers.Add((l.Str("name"), unchecked((int)(uint)l.Long("uniqueID"))));
            }
            var rendering = body.Get("m_RenderingLayers")?.Seq().Select(t => t.AsString()).ToArray();
            if (rendering != null && rendering.Length > 0) RenderingLayerMask.s_Names = rendering;
        }

        static void ApplyPlayer(YNode body)
        {
            ProjectInfo.ProductName = body.Str("productName", ProjectInfo.ProductName);
            ProjectInfo.CompanyName = body.Str("companyName", ProjectInfo.CompanyName);
            ProjectSettingsText.Invalidate();
        }

        static void ApplyTime(YNode body)
        {
            var step = body.Get("Fixed Timestep");
            if (step != null && step.Kind == YKind.Map)
            {
                // A rational: m_Count ticks at m_Rate (numerator / denominator) ticks a second.
                var rate = step.Get("m_Rate");
                double ticks = rate == null ? 0 : rate.Long("m_Numerator") / (double)Math.Max(1, rate.Long("m_Denominator"));
                if (ticks > 0) Time.s_FixedDeltaTime = (float)(step.Long("m_Count") / ticks);
            }
            else if (step != null) Time.s_FixedDeltaTime = step.AsFloat(Time.s_FixedDeltaTime);
            Time.s_MaximumDeltaTime = body.Float("Maximum Allowed Timestep", Time.s_MaximumDeltaTime);
            Time.s_MaximumParticleDeltaTime = body.Float("Maximum Particle Timestep", Time.s_MaximumParticleDeltaTime);
            Time.s_TimeScale = body.Float("m_TimeScale", Time.s_TimeScale);
        }

        /// <summary>AssetDatabase.SaveAssets: edited settings files written as the Editor writes them
        /// — in Edit mode only (in Play mode the Editor keeps them unsaved).</summary>
        internal static void Save(SettingsFile only = null)
        {
            if (EditorApplication.isPlaying) return;
            foreach (var f in s_Files?.Values ?? Enumerable.Empty<SettingsFile>())
            {
                if (!f.Dirty || f.Documents == null || (only != null && f != only)) continue;
                try
                {
                    File.WriteAllText(f.FilePath, UnityYamlText.Write(f.Documents));
                    f.Dirty = false;
                    if (f.Object != null) EditorUtility.ClearDirty(f.Object);
                }
                catch (IOException e)
                {
                    Debug.LogError($"Unbound: saving {f.AssetPath}: {e.Message}");
                }
            }
        }
    }

    /// <summary>
    /// SerializedObjects over settings objects: the property tree built from the class's type tree
    /// and the file's YAML (iterated as the Editor iterates native objects: a string's characters as
    /// an array, an array's <c>Array</c> node at its own depth), and applied changes written back into
    /// the YAML.
    /// </summary>
    internal static class TypeTreeSerialized
    {
        static readonly Dictionary<string, Shape> s_Scalars = new Dictionary<string, Shape>
        {
            ["int"] = Shape.Int, ["SInt32"] = Shape.Int, ["unsigned int"] = Shape.UInt, ["uint"] = Shape.UInt, ["UInt32"] = Shape.UInt,
            ["long"] = Shape.Long, ["SInt64"] = Shape.Long, ["long long"] = Shape.Long, ["UInt64"] = Shape.ULong, ["ulong"] = Shape.ULong,
            ["unsigned long long"] = Shape.ULong, ["short"] = Shape.Short, ["SInt16"] = Shape.Short, ["UInt16"] = Shape.UShort,
            ["unsigned short"] = Shape.UShort, ["ushort"] = Shape.UShort, ["char"] = Shape.SByte, ["SInt8"] = Shape.SByte, ["sbyte"] = Shape.SByte,
            ["UInt8"] = Shape.Byte, ["byte"] = Shape.Byte, ["bool"] = Shape.Bool, ["float"] = Shape.Float, ["double"] = Shape.Double,
            ["Enum"] = Shape.Int,
        };

        static readonly Dictionary<string, (Shape Shape, Type Type)> s_Math = new Dictionary<string, (Shape, Type)>
        {
            ["Vector2"] = (Shape.Vector2, typeof(Vector2)), ["Vector3"] = (Shape.Vector3, typeof(Vector3)), ["Vector4"] = (Shape.Vector4, typeof(Vector4)),
            ["Quaternion"] = (Shape.Quaternion, typeof(Quaternion)), ["Color"] = (Shape.Color, typeof(Color)), ["Rect"] = (Shape.Rect, typeof(Rect)),
            ["Bounds"] = (Shape.Bounds, typeof(Bounds)), ["Vector2Int"] = (Shape.Vector2Int, typeof(Vector2Int)), ["Vector3Int"] = (Shape.Vector3Int, typeof(Vector3Int)),
            ["RectInt"] = (Shape.RectInt, typeof(RectInt)), ["BoundsInt"] = (Shape.BoundsInt, typeof(BoundsInt)),
        };

        static bool IsArray(TypeTreeNode s) => s.Type != SerializedPropertyType.String && s.Child("Array") != null;

        static bool IsMap(TypeTreeNode s) => s.TypeName == "map";

        // ---------------------------------------------------------------- reading

        internal static SerializedNode BuildRoot(SettingsFile file)
        {
            var root = new SerializedNode
            {
                Kind = SerializedNodeKind.Root, Name = "Base", Path = "", Depth = -1, TypeName = file.Body == null ? "Object" : file.Documents[0].ClassName,
                PropertyType = SerializedPropertyType.Generic, ClrType = file.Object?.GetType() ?? typeof(Object), TypeTree = true,
            };
            var body = file.Body ?? YText.Map();
            foreach (var s in file.Tree)
                Add(root, Build(root, s, body.Get(s.Name), -1));
            return root;
        }

        static void Add(SerializedNode parent, SerializedNode child)
        {
            child.Parent = parent;
            child.SiblingIndex = parent.Children.Count;
            parent.Children.Add(child);
        }

        static string PathOf(SerializedNode parent, string name, int index) =>
            index >= 0 ? parent.Path + ".data[" + index.ToString(CultureInfo.InvariantCulture) + "]"
            : parent.Kind == SerializedNodeKind.Root ? name : parent.Path + "." + name;

        static SerializedNode Node(SerializedNode parent, TypeTreeNode s, int index, YText y)
        {
            var node = new SerializedNode
            {
                Parent = parent, Name = s.Name, Index = index, Depth = parent.Depth + 1, TypeName = s.TypeName, PropertyType = s.Type,
                Hidden = s.Hidden, ReadOnly = s.ReadOnly, TypeTree = true, Schema = s, Extra = y,
            };
            node.Path = PathOf(parent, s.Name, index);
            return node;
        }

        internal static SerializedNode Build(SerializedNode parent, TypeTreeNode s, YText y, int index)
        {
            var node = Node(parent, s, index, y);
            if (s.Type == SerializedPropertyType.String)
            {
                node.Kind = SerializedNodeKind.Leaf;
                node.Shape = Shape.String;
                node.ClrType = typeof(string);
                node.Value = y != null && y.Kind == YKind.Scalar ? y.Text : DefaultString(s);
                Chars(node);
                return node;
            }
            if (s.TypeName.StartsWith("PPtr<", StringComparison.Ordinal))
            {
                node.Kind = SerializedNodeKind.Leaf;
                node.Shape = Shape.UnityObject;
                node.ClrType = typeof(Object);
                node.PropertyType = SerializedPropertyType.ObjectReference;
                node.Value = Resolve(y);
                foreach (var c in s.Children) Add(node, PPtrPart(node, c));
                return node;
            }
            if (s.Type == SerializedPropertyType.LayerMask)
            {
                // A layer mask: its bits, as one value (the Editor reads it as an int).
                node.Kind = SerializedNodeKind.Leaf;
                node.Shape = Shape.UInt;
                node.ClrType = typeof(uint);
                node.Value = (uint)(ulong)ReadScalar(Shape.ULong, y != null && y.Kind == YKind.Map ? y.Get("m_Bits") : null, s.Child("m_Bits") ?? s);
                foreach (var c in s.Children)
                {
                    Add(node, new SerializedNode
                    {
                        Parent = node, Kind = SerializedNodeKind.Component, Name = c.Name, TypeName = c.TypeName, Depth = node.Depth + 1,
                        PropertyType = SerializedPropertyType.Integer, Shape = Shape.UInt, ClrType = typeof(uint), Hidden = c.Hidden, TypeTree = true,
                        Path = node.Path + "." + c.Name, Schema = c,
                        Component = new ComponentDef { Name = c.Name, Type = typeof(uint), Shape = Shape.UInt, Get = whole => whole, With = (_, part) => part },
                    });
                }
                return node;
            }
            if (s.Type == SerializedPropertyType.Color && s.Child("rgba") != null)
            {
                // A colour packed into one uint (r in the low byte), as some settings keep theirs.
                node.Kind = SerializedNodeKind.Leaf;
                node.Shape = Shape.Color;
                node.ClrType = typeof(Color);
                var packed = (uint)(ulong)ReadScalar(Shape.ULong, y != null && y.Kind == YKind.Map ? y.Get("rgba") : null, s.Child("rgba"));
                node.Value = (Color)new Color32((byte)packed, (byte)(packed >> 8), (byte)(packed >> 16), (byte)(packed >> 24));
                var rgbaS = s.Child("rgba");
                Add(node, new SerializedNode
                {
                    Parent = node, Kind = SerializedNodeKind.Component, Name = "rgba", TypeName = rgbaS.TypeName, Depth = node.Depth + 1,
                    PropertyType = SerializedPropertyType.Integer, Shape = Shape.UInt, ClrType = typeof(uint), Hidden = rgbaS.Hidden, TypeTree = true,
                    Path = node.Path + ".rgba", Schema = rgbaS,
                    Component = new ComponentDef
                    {
                        Name = "rgba", Type = typeof(uint), Shape = Shape.UInt,
                        Get = whole => Pack((Color)whole),
                        With = (_, part) => { uint p = Convert.ToUInt32(part, CultureInfo.InvariantCulture); return (Color)new Color32((byte)p, (byte)(p >> 8), (byte)(p >> 16), (byte)(p >> 24)); },
                    },
                });
                return node;
            }
            if (s_Math.TryGetValue(s.TypeName, out var math) && s.Type != SerializedPropertyType.Generic)
            {
                node.Kind = SerializedNodeKind.Leaf;
                node.Shape = math.Shape;
                node.ClrType = math.Type;
                node.Value = ReadMath(math.Shape, y);
                SerializedTree.AddComponents(node);
                Retype(node, s);
                return node;
            }
            if (IsArray(s))
            {
                node.Kind = SerializedNodeKind.Array;
                node.PropertyType = SerializedPropertyType.Generic;
                node.Shape = Shape.List;
                var arrayS = s.Child("Array");
                var container = Node(node, arrayS, -1, null);
                container.Kind = SerializedNodeKind.ArrayContainer;
                container.Depth = node.Depth;
                container.Hidden = true;
                Add(node, container);
                var sizeS = arrayS.Child("size");
                var size = Node(container, sizeS, -1, null);
                size.Kind = SerializedNodeKind.ArraySize;
                size.Shape = Shape.Int;
                size.ClrType = typeof(int);
                size.PropertyType = SerializedPropertyType.ArraySize;
                Add(container, size);
                var elementS = arrayS.Child("data");
                int i = 0;
                // Measured: the Editor's QualitySettings shows its per-platform defaults empty.
                if (!(s.Name == "m_PerPlatformDefaultQuality" && parent.Kind == SerializedNodeKind.Root && parent.TypeName == "QualitySettings"))
                    foreach (var item in Items(s, y))
                        Add(container, Build(container, elementS, item, i++));
                return node;
            }
            if (s.Children.Length == 0)
            {
                node.Kind = SerializedNodeKind.Leaf;
                node.Shape = s_Scalars.TryGetValue(s.TypeName, out var shape) ? shape : Shape.Int;
                node.ClrType = ClrOf(node.Shape);
                node.Value = ReadScalar(node.Shape, y, s);
                return node;
            }
            if (s.TypeName == "GUID")
            {
                // Four uints in the tree, 32 hex digits in the file.
                node.Kind = SerializedNodeKind.Composite;
                node.Shape = Shape.Composite;
                var parts = GuidParts(y?.Text ?? "");
                var partS = s.Children[0];
                for (int k = 0; k < 4; k++)
                {
                    var part = Node(node, new TypeTreeNode { Name = "data[" + k + "]", TypeName = partS.TypeName, Type = partS.Type, Hidden = partS.Hidden }, -1, null);
                    part.Kind = SerializedNodeKind.Leaf;
                    part.Shape = Shape.UInt;
                    part.ClrType = typeof(uint);
                    part.Value = k < parts.Length ? parts[k] : 0u;
                    Add(node, part);
                }
                return node;
            }
            node.Kind = SerializedNodeKind.Composite;
            node.Shape = Shape.Composite;
            foreach (var c in s.Children)
                Add(node, Build(node, c, y != null && y.Kind == YKind.Map ? y.Get(c.Name) : null, -1));
            return node;
        }

        /// <summary>An array's elements in the file: a sequence's items; a map's entries as pairs.</summary>
        static IEnumerable<YText> Items(TypeTreeNode s, YText y)
        {
            if (y == null) yield break;
            if (y.Kind == YKind.Scalar)
            {
                // An array of numbers the Editor writes as one hex string (little-endian elements).
                var element = s.Child("Array")?.Child("data");
                int size = element == null ? 0 : HexSize(element.TypeName);
                var hex = y.Text;
                if (size == 0) yield break;
                for (int at = 0; at + size * 2 <= hex.Length; at += size * 2)
                {
                    ulong v = 0;
                    for (int b = size - 1; b >= 0; b--)
                        v = (v << 8) | Convert.ToByte(hex.Substring(at + b * 2, 2), 16);
                    yield return YText.Scalar(HexValue(element.TypeName, v));
                }
                yield break;
            }
            if (y.Kind == YKind.Seq)
            {
                foreach (var item in y.Items) yield return item;
                yield break;
            }
            if (y.Kind == YKind.Map && IsMap(s))
            {
                for (int i = 0; i < y.Keys.Count; i++)
                {
                    var pair = YText.Map();
                    pair.Add("first", YText.Scalar(y.Keys[i]));
                    pair.Add("second", y.Values[i]);
                    yield return pair;
                }
            }
        }

        static int HexSize(string typeName)
        {
            switch (typeName)
            {
                case "UInt8": case "SInt8": case "char": case "bool": return 1;
                case "UInt16": case "SInt16": case "short": case "unsigned short": return 2;
                case "int": case "unsigned int": case "uint": case "UInt32": case "SInt32": case "float": return 4;
                case "UInt64": case "SInt64": case "long": case "double": return 8;
                default: return 0;
            }
        }

        static string HexValue(string typeName, ulong v)
        {
            var inv = CultureInfo.InvariantCulture;
            switch (typeName)
            {
                case "float": return BitConverter.Int32BitsToSingle(unchecked((int)v)).ToString("R", inv);
                case "double": return BitConverter.Int64BitsToDouble(unchecked((long)v)).ToString("R", inv);
                case "int": case "SInt32": return unchecked((int)v).ToString(inv);
                case "SInt16": case "short": return unchecked((short)v).ToString(inv);
                case "SInt8": case "char": return unchecked((sbyte)v).ToString(inv);
                case "SInt64": case "long": return unchecked((long)v).ToString(inv);
                default: return v.ToString(inv);
            }
        }

        static string Hex(string typeName, object value)
        {
            int size = HexSize(typeName);
            ulong v;
            switch (value)
            {
                case float f: v = unchecked((uint)BitConverter.SingleToInt32Bits(f)); break;
                case double d: v = unchecked((ulong)BitConverter.DoubleToInt64Bits(d)); break;
                case bool b: v = b ? 1ul : 0ul; break;
                default: v = unchecked((ulong)Convert.ToInt64(value, CultureInfo.InvariantCulture)); break;
            }
            var sb = new StringBuilder(size * 2);
            for (int b = 0; b < size; b++) sb.Append(((byte)(v >> (8 * b))).ToString("x2", CultureInfo.InvariantCulture));
            return sb.ToString();
        }

        static uint Pack(Color c)
        {
            Color32 c32 = c;
            return c32.r | (uint)c32.g << 8 | (uint)c32.b << 16 | (uint)c32.a << 24;
        }

        /// <summary>A math value's parts named and typed as the native type tree has them.</summary>
        static void Retype(SerializedNode node, TypeTreeNode s)
        {
            node.TypeTree = true;
            node.Schema = s;
            foreach (var child in node.Children)
            {
                var cs = s.Child(child.Name);
                if (cs == null) continue;
                child.TypeName = cs.TypeName;
                child.Hidden = cs.Hidden;
                Retype(child, cs);
            }
        }

        static string DefaultString(TypeTreeNode s) => s.Default is JsonElement d && d.ValueKind == JsonValueKind.String ? d.GetString() : "";

        /// <summary>A string's characters, as the Editor iterates a native string: an array of chars.</summary>
        internal static void Chars(SerializedNode node)
        {
            node.Children.Clear();
            var text = (string)node.Value ?? "";
            var container = new SerializedNode
            {
                Parent = node, Kind = SerializedNodeKind.ArrayContainer, Name = "Array", TypeName = "Array", Depth = node.Depth,
                PropertyType = SerializedPropertyType.Generic, Hidden = true, TypeTree = true, Path = node.Path + ".Array",
            };
            Add(node, container);
            var size = new SerializedNode
            {
                Parent = container, Kind = SerializedNodeKind.ArraySize, Name = "size", TypeName = "ArraySize", Depth = node.Depth + 1,
                PropertyType = SerializedPropertyType.ArraySize, Shape = Shape.Int, ClrType = typeof(int), TypeTree = true, ReadOnly = true,
                Hidden = node.Hidden, Path = container.Path + ".size",
            };
            Add(container, size);
            var bytes = Encoding.UTF8.GetBytes(text);
            for (int i = 0; i < bytes.Length; i++)
            {
                Add(container, new SerializedNode
                {
                    Parent = container, Kind = SerializedNodeKind.Leaf, Name = "data", Index = i, TypeName = "char", Depth = node.Depth + 1,
                    PropertyType = SerializedPropertyType.Integer, Shape = Shape.SByte, ClrType = typeof(sbyte), Value = unchecked((sbyte)bytes[i]),
                    TypeTree = true, ReadOnly = true, Hidden = node.Hidden, Path = container.Path + ".data[" + i.ToString(CultureInfo.InvariantCulture) + "]",
                });
            }
        }

        static SerializedNode PPtrPart(SerializedNode pptr, TypeTreeNode s)
        {
            bool file = s.Name == "m_FileID";
            var part = new SerializedNode
            {
                Parent = pptr, Kind = SerializedNodeKind.Component, Name = s.Name, TypeName = s.TypeName, Depth = pptr.Depth + 1,
                PropertyType = SerializedPropertyType.Integer, Shape = file ? Shape.Int : Shape.Long, ClrType = file ? typeof(int) : typeof(long),
                Hidden = s.Hidden, ReadOnly = true, TypeTree = true, Path = pptr.Path + "." + s.Name,
                Component = new ComponentDef
                {
                    Name = s.Name, Type = file ? typeof(int) : typeof(long), Shape = file ? Shape.Int : Shape.Long,
                    Get = whole => file ? (object)FileIdOf(pptr, whole as Object) : PathIdOf(pptr, whole as Object),
                    With = (whole, _) => whole,
                },
            };
            return part;
        }

        /// <summary>A loaded object's reference, as the Editor shows it in memory: its instance ID,
        /// path ID 0 (measured).</summary>
        static int FileIdOf(SerializedNode pptr, Object o) => o == null ? 0 : o.GetInstanceID();

        static long PathIdOf(SerializedNode pptr, Object o) => 0;

        static ObjectRef RefOf(SerializedNode pptr, Object o)
        {
            if (pptr.Extra is YText y && ReferenceEquals(Resolve(y), o)) return ToRef(y);
            if (AssetDatabaseRuntime.TryGetLocation(o, out var guid, out var fileId)) return new ObjectRef(fileId, guid, 2);
            return default;
        }

        static ObjectRef ToRef(YText y)
        {
            if (y == null || y.Kind != YKind.Map) return default;
            long.TryParse(y.Get("fileID")?.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var fileId);
            var guid = y.Get("guid")?.Text;
            int.TryParse(y.Get("type")?.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var type);
            return new ObjectRef(fileId, string.IsNullOrEmpty(guid) ? null : guid, type);
        }

        static Object Resolve(YText y)
        {
            var r = ToRef(y);
            if (r.IsNull || r.Guid == null) return null;
            try { return AssetDatabaseRuntime.Load(r.Guid, r.FileId, typeof(Object)); }
            catch (Exception) { return null; }
        }

        static Type ClrOf(Shape shape)
        {
            switch (shape)
            {
                case Shape.Bool: return typeof(bool);
                case Shape.SByte: return typeof(sbyte);
                case Shape.Byte: return typeof(byte);
                case Shape.Short: return typeof(short);
                case Shape.UShort: return typeof(ushort);
                case Shape.UInt: return typeof(uint);
                case Shape.Long: return typeof(long);
                case Shape.ULong: return typeof(ulong);
                case Shape.Float: return typeof(float);
                case Shape.Double: return typeof(double);
                default: return typeof(int);
            }
        }

        static object ReadScalar(Shape shape, YText y, TypeTreeNode s)
        {
            string text = y != null && y.Kind == YKind.Scalar ? y.Text : null;
            if (text == null && s.Default is JsonElement d)
            {
                switch (d.ValueKind)
                {
                    case JsonValueKind.True: text = "1"; break;
                    case JsonValueKind.False: text = "0"; break;
                    case JsonValueKind.Number: text = d.GetRawText(); break;
                    case JsonValueKind.String: text = d.GetString(); break;
                }
            }
            text ??= "0";
            var inv = CultureInfo.InvariantCulture;
            switch (shape)
            {
                case Shape.Bool: return text != "0" && text.Length > 0 && !text.Equals("false", StringComparison.OrdinalIgnoreCase);
                case Shape.Float: return float.TryParse(text, NumberStyles.Float, inv, out var f) ? f : 0f;
                case Shape.Double: return double.TryParse(text, NumberStyles.Float, inv, out var dbl) ? dbl : 0d;
                case Shape.ULong: return ulong.TryParse(text, NumberStyles.Integer, inv, out var ul) ? ul : 0ul;
            }
            long.TryParse(text, NumberStyles.Integer, inv, out var l);
            switch (shape)
            {
                case Shape.SByte: return unchecked((sbyte)l);
                case Shape.Byte: return unchecked((byte)l);
                case Shape.Short: return unchecked((short)l);
                case Shape.UShort: return unchecked((ushort)l);
                case Shape.UInt: return unchecked((uint)l);
                case Shape.Long: return l;
                default: return unchecked((int)l);
            }
        }

        static float F(YText y, string key) =>
            y != null && y.Kind == YKind.Map && float.TryParse(y.Get(key)?.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : 0f;

        static int I(YText y, string key) =>
            y != null && y.Kind == YKind.Map && int.TryParse(y.Get(key)?.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : 0;

        static object ReadMath(Shape shape, YText y)
        {
            switch (shape)
            {
                case Shape.Vector2: return new Vector2(F(y, "x"), F(y, "y"));
                case Shape.Vector3: return new Vector3(F(y, "x"), F(y, "y"), F(y, "z"));
                case Shape.Vector4: return new Vector4(F(y, "x"), F(y, "y"), F(y, "z"), F(y, "w"));
                case Shape.Quaternion: return new Quaternion(F(y, "x"), F(y, "y"), F(y, "z"), F(y, "w"));
                case Shape.Color: return new Color(F(y, "r"), F(y, "g"), F(y, "b"), F(y, "a"));
                case Shape.Rect: return new Rect(F(y, "x"), F(y, "y"), F(y, "width"), F(y, "height"));
                case Shape.Vector2Int: return new Vector2Int(I(y, "x"), I(y, "y"));
                case Shape.Vector3Int: return new Vector3Int(I(y, "x"), I(y, "y"), I(y, "z"));
                case Shape.RectInt: return new RectInt(I(y, "x"), I(y, "y"), I(y, "width"), I(y, "height"));
                case Shape.Bounds:
                {
                    var c = y?.Get("m_Center");
                    var e = y?.Get("m_Extent");
                    return new Bounds(new Vector3(F(c, "x"), F(c, "y"), F(c, "z")), 2f * new Vector3(F(e, "x"), F(e, "y"), F(e, "z")));
                }
                case Shape.BoundsInt:
                {
                    var p = y?.Get("m_Position");
                    var s = y?.Get("m_Size");
                    return new BoundsInt(new Vector3Int(I(p, "x"), I(p, "y"), I(p, "z")), new Vector3Int(I(s, "x"), I(s, "y"), I(s, "z")));
                }
                default: return null;
            }
        }

        /// <summary>A GUID's four uints from its 32 hex digits (each uint's digits low nibble first,
        /// as the Editor writes them).</summary>
        static uint[] GuidParts(string hex)
        {
            var parts = new uint[4];
            if (hex.Length != 32) return parts;
            for (int i = 0; i < 4; i++)
            {
                uint v = 0;
                for (int j = 7; j >= 0; j--)
                    v = (v << 4) | (uint)Convert.ToInt32(hex[i * 8 + j].ToString(), 16);
                parts[i] = v;
            }
            return parts;
        }

        static string GuidText(uint[] parts)
        {
            var sb = new StringBuilder(32);
            foreach (var p in parts)
                for (int j = 0; j < 8; j++) sb.Append("0123456789abcdef"[(int)((p >> (j * 4)) & 0xF)]);
            return sb.ToString();
        }

        /// <summary>A new element of an empty array: the element's tree with a new element's values.</summary>
        internal static SerializedNode NewElement(SerializedNode array, int index)
        {
            var elementS = array.Schema.Child("Array").Child("data");
            return Build(array.ArrayContainer, elementS, null, index);
        }

        // ---------------------------------------------------------------- writing back

        internal static void Apply(SerializedNode root, SettingsFile file)
        {
            var body = file.Body;
            if (body == null) return;
            foreach (var child in root.Children)
            {
                if (!child.Dirty || child.Schema == null) continue;
                body.Set(child.Name, Write(child, body.Get(child.Name), 2 + child.Name.Length + 2, 2));
            }
            SettingsAssets.Applied(file);
        }

        /// <summary>A node's YAML: its old YAML where it did not change, else written as the Editor
        /// writes it. <paramref name="column"/> is where a scalar starts, <paramref name="indent"/> its
        /// key's indentation.</summary>
        static YText Write(SerializedNode node, YText old, int column, int indent)
        {
            old ??= node.Extra as YText;
            switch (node.Kind)
            {
                case SerializedNodeKind.Leaf:
                    return WriteLeaf(node, old, column, indent);
                case SerializedNodeKind.Array:
                    return WriteArray(node, old, indent);
                case SerializedNodeKind.Composite:
                {
                    if (node.TypeName == "GUID")
                    {
                        var parts = node.Children.Select(c => Convert.ToUInt32(c.Value, CultureInfo.InvariantCulture)).ToArray();
                        var text = GuidText(parts);
                        return old != null && old.Kind == YKind.Scalar && old.Text == text ? old : YText.Scalar(text);
                    }
                    var map = old != null && old.Kind == YKind.Map ? old.Clone() : NewMap(node);
                    if (map.Flow) map.Raw = null;
                    foreach (var child in node.Children)
                    {
                        if (child.Schema == null) continue;
                        var before = map.Get(child.Name);
                        map.Set(child.Name, Write(child, before, indent + 2 + child.Name.Length + 2, indent + 2));
                    }
                    return map;
                }
                default:
                    return old ?? YText.Scalar("");
            }
        }

        /// <summary>A map for a composite without YAML yet (a new element): the keys the class's other
        /// elements carry outside its tree (<c>serializedVersion</c>) first, as the Editor writes them.</summary>
        static YText NewMap(SerializedNode node)
        {
            var map = YText.Map();
            if (node.IsElement && node.Parent != null)
            {
                foreach (var sibling in node.Parent.Children)
                {
                    if (!(sibling.Extra is YText y) || y.Kind != YKind.Map) continue;
                    for (int i = 0; i < y.Keys.Count; i++)
                        if (node.Schema?.Child(y.Keys[i]) == null) map.Add(y.Keys[i], y.Values[i].Clone());
                    break;
                }
            }
            return map;
        }

        static YText WriteArray(SerializedNode node, YText old, int indent)
        {
            int count = node.ArrayCount;
            if (old != null && old.Kind == YKind.Scalar && old.Text.Length > 0)
            {
                var elementS = node.Schema.Child("Array")?.Child("data");
                if (elementS != null && HexSize(elementS.TypeName) > 0)
                {
                    var sb = new StringBuilder();
                    for (int i = 0; i < count; i++) sb.Append(Hex(elementS.TypeName, node.Element(i).Value));
                    return sb.ToString() == old.Text ? old : YText.Scalar(sb.ToString());
                }
            }
            bool map = IsMap(node.Schema) && (old == null || old.Kind == YKind.Map) && KeysAreScalar(node);
            if (count == 0) return map ? YText.Map(flow: true) : YText.Seq(flow: true);
            if (map)
            {
                var result = YText.Map();
                for (int i = 0; i < count; i++)
                {
                    var pair = node.Element(i);
                    var first = pair.Children.FirstOrDefault(c => c.Name == "first");
                    var second = pair.Children.FirstOrDefault(c => c.Name == "second");
                    var key = first == null ? "" : ScalarText(first, null, 0, 0).Raw;
                    var oldValue = (pair.Extra as YText)?.Get("second");
                    result.Add(key, second == null ? YText.Scalar("") : Write(second, oldValue, indent + 2 + key.Length + 2, indent + 2));
                }
                return result;
            }
            var seq = YText.Seq();
            for (int i = 0; i < count; i++)
            {
                var element = node.Element(i);
                seq.Items.Add(Write(element, element.Extra as YText, indent + 2, indent + 2));
            }
            return seq;
        }

        static bool KeysAreScalar(SerializedNode node)
        {
            var first = node.Schema.Child("Array")?.Child("data")?.Child("first");
            return first != null && first.Children.Length == 0 || first?.Type == SerializedPropertyType.String;
        }

        static YText WriteLeaf(SerializedNode node, YText old, int column, int indent)
        {
            if (node.Shape == Shape.Color && node.Schema?.Child("rgba") != null)
            {
                var rgba = Pack((Color)node.Value).ToString(CultureInfo.InvariantCulture);
                var map = old != null && old.Kind == YKind.Map ? old.Clone() : YText.Map();
                if (map.Get("rgba")?.Text == rgba) return old;
                map.Set("rgba", YText.Scalar(rgba));
                return map;
            }
            if (node.PropertyType == SerializedPropertyType.LayerMask)
            {
                var bits = Number(node.Value);
                var map = old != null && old.Kind == YKind.Map ? old.Clone() : YText.Map();
                if (map.Get("m_Bits")?.Text == bits) return old;
                map.Set("m_Bits", YText.Scalar(bits));
                return map;
            }
            switch (node.Shape)
            {
                case Shape.UnityObject:
                {
                    var o = node.Value as Object;
                    if (old != null && old.Kind == YKind.Map && ReferenceEquals(Resolve(old), o) && (o != null || ToRef(old).IsNull)) return old;
                    var map = YText.Map(flow: true);
                    map.Raw = null;
                    if (o == null) { map.Add("fileID", YText.Scalar("0")); return map; }
                    var r = RefOf(node, o);
                    map.Add("fileID", YText.Scalar(r.FileId.ToString(CultureInfo.InvariantCulture)));
                    if (r.Guid != null)
                    {
                        map.Add("guid", YText.Scalar(r.Guid));
                        map.Add("type", YText.Scalar(r.Type.ToString(CultureInfo.InvariantCulture)));
                    }
                    return map;
                }
                case Shape.Vector2: case Shape.Vector3: case Shape.Vector4: case Shape.Quaternion: case Shape.Color: case Shape.Rect:
                case Shape.Vector2Int: case Shape.Vector3Int: case Shape.RectInt:
                {
                    if (old != null && Equals(ReadMath(node.Shape, old), node.Value)) return old;
                    var map = YText.Map(flow: true);
                    map.Raw = null;
                    foreach (var part in node.Children)
                        map.Add(part.Name, YText.Scalar(Number(SerializedTree.GetValue(part))));
                    return map;
                }
                case Shape.Bounds: case Shape.BoundsInt:
                {
                    if (old != null && Equals(ReadMath(node.Shape, old), node.Value)) return old;
                    var map = YText.Map();
                    foreach (var part in node.Children)
                    {
                        var inner = YText.Map(flow: true);
                        inner.Raw = null;
                        foreach (var c in part.Children) inner.Add(c.Name, YText.Scalar(Number(SerializedTree.GetValue(c))));
                        map.Add(part.Name, inner);
                    }
                    return map;
                }
                default:
                    return ScalarText(node, old, column, indent);
            }
        }

        static YText ScalarText(SerializedNode node, YText old, int column, int indent)
        {
            if (old != null && old.Kind == YKind.Scalar && Equals(ReadScalarLike(node, old), node.Value)) return old;
            if (node.Shape == Shape.String)
            {
                var s = (string)node.Value ?? "";
                var raw = UnityYamlWriter.Scalar(s);
                if (raw.Length > 0 && raw[0] != '\'' && raw[0] != '"') raw = UnityYamlText.WrapPlain(raw, column, indent + 2);
                return YText.Scalar(raw);
            }
            if (node.Shape == Shape.Bool) return YText.Scalar((bool)node.Value ? "1" : "0");
            return YText.Scalar(Number(node.Value));
        }

        static object ReadScalarLike(SerializedNode node, YText old) =>
            node.Shape == Shape.String ? old.Text : ReadScalar(node.Shape, old, node.Schema ?? new TypeTreeNode());

        /// <summary>A number as the Editor writes it: integers plainly, floats in their shortest
        /// round-trip form without an exponent.</summary>
        internal static string Number(object value)
        {
            var inv = CultureInfo.InvariantCulture;
            switch (value)
            {
                case float f:
                {
                    var s = f.ToString("R", inv);
                    if (s.IndexOf('E') >= 0) s = ((decimal)f).ToString(inv);
                    return s;
                }
                case double d:
                {
                    var s = d.ToString("R", inv);
                    if (s.IndexOf('E') >= 0) s = d.ToString("0.###################################", inv);
                    return s;
                }
                case bool b: return b ? "1" : "0";
                case null: return "0";
                default: return Convert.ToString(value, inv);
            }
        }
    }
}

namespace UnityEditorInternal
{
    public partial class InternalEditorUtility
    {
        /// <summary>The tags: the built-in ones, then the project's (TagManager.asset).</summary>
        public static string[] tags => Unbound.Runtime.TagManager.All;

        /// <summary>The named layers, in order.</summary>
        public static string[] layers => System.Linq.Enumerable.ToArray(System.Linq.Enumerable.Where(Unbound.Runtime.TagManager.Layers, l => !string.IsNullOrEmpty(l)));
    }
}

namespace UnityEditor
{
    public static partial class Unsupported
    {
        /// <summary>The settings object of a class (or its object's name): "PlayerSettings",
        /// "TagManager", "PhysicsManager", ...; null for a name no settings asset has.</summary>
        public static UnityEngine.Object GetSerializedAssetInterfaceSingleton(string className) =>
            Unbound.Runtime.SettingsAssets.Singleton(className);
    }
}
