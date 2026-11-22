using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using Unbound.Runtime;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.UIElements.StyleSheets;
using Object = UnityEngine.Object;

namespace Unbound.UIElements
{
    /// <summary>UI Toolkit's asset kinds: .uxml (VisualTreeAsset), .uss (StyleSheet) and .tss
    /// (ThemeStyleSheet), imported from their sources under the file ids Unity's importers give them.</summary>
    internal static class UiImporters
    {
        internal const long StyleSheetFileId = 7433441132597879392;
        internal const long ThemeStyleSheetFileId = -4733365628477956816;

        internal static void Register()
        {
            AssetDatabaseRuntime.Importers["uxml"] = ImportUxml;
            AssetDatabaseRuntime.Importers["uss"] = info => ImportStyleSheet(info, false);
            AssetDatabaseRuntime.Importers["tss"] = info => ImportStyleSheet(info, true);
        }

        static LoadedFile ImportUxml(AssetDatabaseRuntime.AssetInfo info)
        {
            VisualTreeAsset asset;
            using (ObjectRegistry.AsAssets())
            {
                asset = ScriptableObject.CreateInstance<VisualTreeAsset>();
                asset.name = System.IO.Path.GetFileNameWithoutExtension(info.Path);
                asset.IsPersistentAsset = true;
            }
            var file = new LoadedFile { MainObject = asset };
            file.Objects[UxmlImporter.VisualTreeAssetFileId] = asset;
            // Published before the document's style sheets and templates load (they may refer back).
            UxmlImporter.Import(info.Path, System.IO.File.ReadAllText(info.File), asset);
            return file;
        }

        static LoadedFile ImportStyleSheet(AssetDatabaseRuntime.AssetInfo info, bool theme)
        {
            StyleSheet sheet;
            using (ObjectRegistry.AsAssets())
            {
                sheet = theme ? ScriptableObject.CreateInstance<ThemeStyleSheet>() : ScriptableObject.CreateInstance<StyleSheet>();
                sheet.name = System.IO.Path.GetFileNameWithoutExtension(info.Path);
                sheet.IsPersistentAsset = true;
            }
            var importer = new UssImporter(info.Path)
            {
                resolveAsset = AssetPaths.LoadUrl,
                resolveImport = href => AssetPaths.LoadImport(info.Path, href),
            };
            importer.Import(sheet, System.IO.File.ReadAllText(info.File));
            var file = new LoadedFile { MainObject = sheet };
            file.Objects[theme ? ThemeStyleSheetFileId : StyleSheetFileId] = sheet;
            return file;
        }
    }

    /// <summary>Asset URLs of USS and UXML (`project://database/Assets/...?fileID=..&guid=..&type=..#name`,
    /// absolute `/Assets/...` or relative to the referring asset).</summary>
    internal static class AssetPaths
    {
        internal static string Resolve(string referrer, string url, out string guid, out long fileId, out string subAsset)
        {
            guid = null;
            fileId = 0;
            subAsset = null;
            if (string.IsNullOrEmpty(url)) return null;
            int hash = url.IndexOf('#');
            if (hash >= 0)
            {
                subAsset = Uri.UnescapeDataString(url.Substring(hash + 1));
                url = url.Substring(0, hash);
            }
            int query = url.IndexOf('?');
            if (query >= 0)
            {
                foreach (var pair in url.Substring(query + 1).Split('&'))
                {
                    int eq = pair.IndexOf('=');
                    if (eq < 0) continue;
                    var key = pair.Substring(0, eq);
                    var value = pair.Substring(eq + 1);
                    if (key == "guid") guid = value;
                    else if (key == "fileID") long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out fileId);
                }
                url = url.Substring(0, query);
            }
            url = Uri.UnescapeDataString(url);
            const string project = "project://database/";
            if (url.StartsWith(project, StringComparison.Ordinal)) return url.Substring(project.Length);
            if (url.StartsWith("/", StringComparison.Ordinal)) return url.Substring(1);
            var dir = System.IO.Path.GetDirectoryName(referrer ?? "") ?? "";
            var combined = System.IO.Path.Combine(dir, url).Replace('\\', '/');
            var parts = new List<string>();
            foreach (var part in combined.Split('/'))
            {
                if (part == "..") { if (parts.Count > 0) parts.RemoveAt(parts.Count - 1); }
                else if (part != "." && part.Length > 0) parts.Add(part);
            }
            return string.Join("/", parts);
        }

        internal static Object LoadAsset(string referrer, string url, Type type)
        {
            var path = Resolve(referrer, url, out var guid, out var fileId, out var subAsset);
            if (guid != null && fileId != 0)
            {
                var byId = AssetDatabaseRuntime.Load(guid, fileId, type);
                if (byId != null) return byId;
            }
            if (path == null) return null;
            AssetDatabaseRuntime.AssetInfo info;
            try
            {
                info = AssetDatabaseRuntime.InfoByPath(path);
            }
            catch (Exception)
            {
                return null;
            }
            if (info == null || string.IsNullOrEmpty(info.Guid)) return null;
            if (!string.IsNullOrEmpty(subAsset))
            {
                foreach (var o in AssetDatabaseRuntime.LoadAll(info.Guid))
                    if (o != null && o.name == subAsset && (type == null || type.IsInstanceOfType(o))) return o;
            }
            return AssetDatabaseRuntime.LoadMain(info.Guid, type);
        }

        /// <summary>A url() of a style sheet: a texture is used as its sprite when it imports as one.</summary>
        internal static Object LoadUrl(string referrer, string url)
        {
            var obj = LoadAsset(referrer, url, null);
            if (obj is Texture2D)
            {
                var path = Resolve(referrer, url, out _, out _, out var sub);
                if (string.IsNullOrEmpty(sub))
                {
                    var sprite = LoadAsset(referrer, url, typeof(Sprite));
                    if (sprite != null) return sprite;
                }
            }
            return obj;
        }

        internal static StyleSheet LoadStyleSheet(string referrer, string url) => LoadAsset(referrer, url, typeof(StyleSheet)) as StyleSheet;

        internal static StyleSheet LoadImport(string referrer, string href)
        {
            if (href.StartsWith("unity-theme://", StringComparison.Ordinal))
                return UiResources.LoadTheme(href.Substring("unity-theme://".Length));
            return LoadStyleSheet(referrer, href);
        }
    }

    /// <summary>
    /// Resources of UI Toolkit that live in the Editor (`unity editor resources`): the default runtime
    /// theme and the images and fonts it names (`EditorGUIUtility.Load` paths). Loaded through the
    /// native side's reader of the file's type trees.
    /// </summary>
    internal static class UiResources
    {
        const string EditorGuid = "0000000000000000d000000000000000";
        static readonly Dictionary<long, Object> s_Objects = new Dictionary<long, Object>();
        static readonly Dictionary<string, long> s_Paths = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        static readonly Dictionary<string, StyleSheet> s_Themes = new Dictionary<string, StyleSheet>();

        static readonly Dictionary<string, string> s_ThemePaths = new Dictionary<string, string>
        {
            ["default"] = "StyleSheets/Generated/Default.tss.asset",
        };

        /// <summary>A theme `unity-theme://name` imports: a copy of the Editor's theme sheet whose
        /// resource paths are resolved to the resources themselves (as the theme importer copies them).</summary>
        internal static StyleSheet LoadTheme(string name)
        {
            if (s_Themes.TryGetValue(name, out var cached)) return cached;
            StyleSheet theme = null;
            if (s_ThemePaths.TryGetValue(name, out var path)) theme = EditorLoad(path) as StyleSheet;
            if (theme == null) Debug.LogError($"Unbound: the UI Toolkit theme 'unity-theme://{name}' is not available (it comes from the Unity Editor's resources).");
            else theme = ResolveResourcePaths(theme, name);
            s_Themes[name] = theme;
            return theme;
        }

        static StyleSheet ResolveResourcePaths(StyleSheet source, string name)
        {
            StyleSheet copy;
            using (ObjectRegistry.AsAssets())
            {
                copy = ScriptableObject.CreateInstance<ThemeStyleSheet>();
                copy.name = name;
                copy.IsPersistentAsset = true;
            }
            var assets = new List<Object>(source.assets ?? Array.Empty<Object>());
            var scalable = new List<ScalableImage>(source.scalableImages ?? Array.Empty<ScalableImage>());
            var assetIndex = new Dictionary<string, int>();
            var scalableIndex = new Dictionary<string, int>();
            var rules = new StyleRule[source.rules.Length];
            for (int r = 0; r < rules.Length; r++)
            {
                var rule = source.rules[r];
                var properties = new StyleProperty[rule.properties.Length];
                for (int p = 0; p < properties.Length; p++)
                {
                    var property = rule.properties[p];
                    var values = (StyleValueHandle[])property.values.Clone();
                    for (int v = 0; v < values.Length; v++)
                    {
                        if (values[v].valueType != StyleValueType.ResourcePath) continue;
                        var resourcePath = source.strings[values[v].valueIndex];
                        if (scalableIndex.TryGetValue(resourcePath, out int si))
                        {
                            values[v] = new StyleValueHandle(si, StyleValueType.ScalableImage);
                            continue;
                        }
                        if (assetIndex.TryGetValue(resourcePath, out int ai))
                        {
                            values[v] = new StyleValueHandle(ai, StyleValueType.AssetReference);
                            continue;
                        }
                        var obj = EditorLoad(resourcePath);
                        if (obj == null) continue;
                        if (obj is Texture2D texture)
                        {
                            var high = EditorLoad(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(resourcePath) ?? "",
                                System.IO.Path.GetFileNameWithoutExtension(resourcePath) + "@2x" + System.IO.Path.GetExtension(resourcePath)).Replace('\\', '/')) as Texture2D;
                            if (high != null)
                            {
                                scalable.Add(new ScalableImage { normalImage = texture, highResolutionImage = high });
                                scalableIndex[resourcePath] = scalable.Count - 1;
                                values[v] = new StyleValueHandle(scalable.Count - 1, StyleValueType.ScalableImage);
                                continue;
                            }
                        }
                        assets.Add(obj);
                        assetIndex[resourcePath] = assets.Count - 1;
                        values[v] = new StyleValueHandle(assets.Count - 1, StyleValueType.AssetReference);
                    }
                    properties[p] = new StyleProperty { name = property.name, line = property.line, values = values };
                }
                rules[r] = new StyleRule { line = rule.line, properties = properties };
            }
            copy.floats = source.floats;
            copy.dimensions = source.dimensions;
            copy.colors = source.colors;
            copy.strings = source.strings;
            copy.assets = assets.ToArray();
            copy.scalableImages = scalable.ToArray();
            copy.imports = source.imports ?? Array.Empty<StyleSheet.ImportStruct>();
            copy.contentHash = source.contentHash;
            var selectors = new StyleComplexSelector[source.complexSelectors.Length];
            for (int i = 0; i < selectors.Length; i++)
            {
                var s = source.complexSelectors[i];
                selectors[i] = new StyleComplexSelector { specificity = s.specificity, ruleIndex = s.ruleIndex, selectors = s.selectors };
            }
            copy.rules = rules;
            copy.complexSelectors = selectors;
            if (copy.imports.Length > 0) copy.FlattenImportedStyleSheetsRecursive();
            return copy;
        }

        /// <summary>A resource of the Editor by its path (EditorGUIUtility.Load).</summary>
        internal static Object EditorLoad(string path)
        {
            if (string.IsNullOrEmpty(path) || !Native.IsReady) return null;
            if (!s_Paths.TryGetValue(path, out long id))
            {
                id = 0;
                try
                {
                    var json = Native.Call("editor-resource", JsonSerializer.Serialize(new { path }));
                    using var doc = JsonDocument.Parse(json);
                    if (doc.RootElement.ValueKind == JsonValueKind.Object) id = doc.RootElement.GetProperty("pathId").GetInt64();
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"Unbound: editor resource {path}: {e.Message}");
                }
                s_Paths[path] = id;
            }
            return id == 0 ? null : LoadObject(id);
        }

        /// <summary>An object of the Editor's resources by its path id (references `{fileID, guid 0000...d000}`).</summary>
        internal static Object LoadObject(long pathId)
        {
            if (s_Objects.TryGetValue(pathId, out var cached)) return cached;
            s_Objects[pathId] = null;
            Object obj = null;
            try
            {
                var json = Native.Call("editor-resource", JsonSerializer.Serialize(new { pathId }));
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                int classId = root.GetProperty("classId").GetInt32();
                var fields = root.GetProperty("fields");
                obj = classId switch
                {
                    28 => ReadTexture(fields),
                    128 => ReadFont(fields, pathId),
                    114 => ReadMonoBehaviour(fields),
                    _ => null,
                };
                if (obj == null) Debug.LogWarning($"Unbound: editor resource {pathId} (class {classId}) is not supported.");
            }
            catch (Exception e)
            {
                Debug.LogWarning($"Unbound: editor resource {pathId}: {e.Message}");
            }
            s_Objects[pathId] = obj;
            return obj;
        }

        static string Str(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
        static int Int(JsonElement e, string name) => int.TryParse(Str(e, name), NumberStyles.Integer, CultureInfo.InvariantCulture, out var i) ? i : 0;
        static float Float(JsonElement e, string name) => float.TryParse(Str(e, name), NumberStyles.Float, CultureInfo.InvariantCulture, out var f) ? f : 0f;

        static byte[] Hex(string hex)
        {
            if (hex == null) return Array.Empty<byte>();
            var bytes = new byte[hex.Length / 2];
            for (int i = 0; i < bytes.Length; i++) bytes[i] = Convert.ToByte(hex.Substring(2 * i, 2), 16);
            return bytes;
        }

        /// <summary>The formats the Editor's own textures are stored in (uncompressed and the
        /// block formats its skins' backgrounds use), which load from their raw data.</summary>
        static readonly TextureFormat[] s_EditorTextureFormats =
            { TextureFormat.RGBA32, TextureFormat.ARGB32, TextureFormat.Alpha8, TextureFormat.RGB24, TextureFormat.DXT5 };

        static Texture2D ReadTexture(JsonElement f)
        {
            int format = Int(f, "m_TextureFormat");
            if (Array.IndexOf(s_EditorTextureFormats, (TextureFormat)format) < 0)
                throw new NotSupportedException($"texture format {format}");
            int w = Int(f, "m_Width"), h = Int(f, "m_Height");
            Texture2D texture;
            using (ObjectRegistry.AsAssets())
            {
                texture = new Texture2D(w, h, (TextureFormat)format, false, Int(f, "m_ColorSpace") == 0) { name = Str(f, "m_Name") ?? "" };
                texture.IsPersistentAsset = true;
            }
            texture.LoadRawTextureData(Hex(Str(f, "image data")));
            if (f.TryGetProperty("m_TextureSettings", out var settings))
            {
                texture.filterMode = (FilterMode)Int(settings, "m_FilterMode");
                texture.wrapModeU = (TextureWrapMode)Int(settings, "m_WrapU");
                texture.wrapModeV = (TextureWrapMode)Int(settings, "m_WrapV");
                texture.anisoLevel = Int(settings, "m_Aniso");
            }
            texture.ApplyQuietly(false, false);
            return texture;
        }

        static Font ReadFont(JsonElement f, long pathId)
        {
            var font = Font.UaFromEditorResource(pathId, Str(f, "m_Name") ?? "", Float(f, "m_FontSize"));
            // The names the font lists: its own family, then the OS fonts it stands for.
            if (f.TryGetProperty("m_FontNames", out var names) && names.ValueKind == JsonValueKind.Array && names.GetArrayLength() > 0)
            {
                var list = new List<string>();
                foreach (var name in names.EnumerateArray()) list.Add(name.GetString() ?? "");
                font.m_FontNames = list.ToArray();
            }
            return font;
        }

        static Object ReadMonoBehaviour(JsonElement f)
        {
            if (!f.TryGetProperty("m_Script", out var script)) return null;
            long fileId = script.TryGetProperty("fileID", out var fid) ? fid.GetInt64() : 0;
            var type = EngineScripts.Resolve(script.TryGetProperty("guid", out var g) ? g.GetString() : null, fileId);
            if (type == typeof(ThemeStyleSheet) || type == typeof(StyleSheet)) return ReadStyleSheet(f, type);
            if (type == typeof(VisualTreeAsset)) return ReadVisualTreeAsset(f);
            if (type == typeof(GUISkin)) return ReadGuiSkin(f);
            return null;
        }

        /// <summary>A GUI skin of the Editor's resources: its styles as serialized, its textures
        /// and fonts from the same resources.</summary>
        static GUISkin ReadGuiSkin(JsonElement f)
        {
            GUISkin skin;
            using (ObjectRegistry.AsAssets())
            {
                skin = ScriptableObject.CreateInstance<GUISkin>();
                new YamlReader(new EditorResourceReferences()).ReadScriptObject(skin, YNode.FromJson(f));
            }
            skin.name = Str(f, "m_Name") ?? "";
            skin.hideFlags = HideFlags.HideAndDontSave;
            skin.IsPersistentAsset = true;
            skin.Apply();
            return skin;
        }

        /// <summary>References of an Editor resource: objects of the Editor's resources by their
        /// path id, other built-in objects as every reference resolves them.</summary>
        sealed class EditorResourceReferences : IReferenceResolver
        {
            public Object Resolve(ObjectRef reference, Type expected)
            {
                if (reference.Guid == null || reference.FileId == 0) return null;
                if (string.Equals(reference.Guid, EditorResourcesGuid, StringComparison.OrdinalIgnoreCase))
                    return LoadObject(reference.FileId);
                return BuiltinShaders.Resolve(reference.Guid, reference.FileId);
            }
        }

        /// <summary>The GUID references into `unity editor resources` use.</summary>
        internal const string EditorResourcesGuid = "0000000000000000d000000000000000";

        /// <summary>
        /// A UXML document of the Editor's resources from its serialized form: its elements
        /// (`m_VisualElementAssets`: type, attributes in document order, parent and order among its
        /// siblings) as the tree Unbound's UXML importer makes from the document's source, the root
        /// being the document's &lt;UXML&gt; element. The elements' serialized attribute data
        /// (`m_SerializedData`) holds the same values as their attributes and is not read. Documents
        /// with what this reader does not read (templates, slots, UXML objects, style sheets or
        /// inline styles, element text) are refused with a warning naming the document and answer
        /// null.
        /// </summary>
        internal static VisualTreeAsset ReadVisualTreeAsset(JsonElement f)
        {
            string name = Str(f, "m_Name") ?? "";
            string unsupported = UnsupportedVisualTreeContent(f);
            if (unsupported != null)
            {
                Debug.LogWarning($"Unbound: the Editor's UXML document '{name}' is not read: it has {unsupported}.");
                return null;
            }
            var nodes = new Dictionary<int, (UxmlNode Node, int Parent, int Order, int Position)>();
            foreach (var e in f.GetProperty("m_VisualElementAssets").EnumerateArray())
            {
                string fullName = Str(e, "m_FullTypeName") ?? "";
                int dot = fullName.LastIndexOf('.');
                var node = new UxmlNode
                {
                    Namespace = dot < 0 ? "" : fullName.Substring(0, dot),
                    Type = dot < 0 ? fullName : fullName.Substring(dot + 1),
                };
                if (e.TryGetProperty("m_Properties", out var properties))
                {
                    var list = new List<string>();
                    foreach (var p in properties.EnumerateArray()) list.Add(p.GetString());
                    for (int i = 0; i + 1 < list.Count; i += 2) node.Attributes.Add(new KeyValuePair<string, string>(list[i], list[i + 1]));
                }
                nodes[Int(e, "m_Id")] = (node, Int(e, "m_ParentId"), Int(e, "m_OrderInDocument"), nodes.Count);
            }
            UxmlNode root = null;
            var children = new List<(int Order, int Position, int Parent, UxmlNode Node)>();
            foreach (var (node, parent, order, position) in nodes.Values)
            {
                if (parent == 0)
                {
                    if (root != null || node.Type != "UXML")
                    {
                        Debug.LogWarning($"Unbound: the Editor's UXML document '{name}' is not read: it has elements outside its <UXML> element.");
                        return null;
                    }
                    root = node;
                }
                else children.Add((order, position, parent, node));
            }
            if (root == null)
            {
                Debug.LogWarning($"Unbound: the Editor's UXML document '{name}' is not read: it has no <UXML> element.");
                return null;
            }
            // Siblings in document order (an order the asset lists twice keeps the listing's order).
            children.Sort((a, b) => a.Order != b.Order ? a.Order.CompareTo(b.Order) : a.Position.CompareTo(b.Position));
            foreach (var (order, _, parent, node) in children)
            {
                if (!nodes.TryGetValue(parent, out var p))
                {
                    Debug.LogWarning($"Unbound: the Editor's UXML document '{name}' is not read: an element's parent {parent} is not in it.");
                    return null;
                }
                node.Index = order;
                p.Node.Children.Add(node);
            }
            VisualTreeAsset asset;
            using (ObjectRegistry.AsAssets())
            {
                asset = ScriptableObject.CreateInstance<VisualTreeAsset>();
                asset.name = name;
                asset.IsPersistentAsset = true;
            }
            asset.m_UaRoot = root;
            asset.contentHash = Int(f, "m_ContentHash");
            asset.importedWithErrors = Str(f, "m_ImportedWithErrors") == "1";
            asset.importedWithWarnings = Str(f, "m_ImportedWithWarnings") == "1";
            return asset;
        }

        /// <summary>What a serialized UXML document holds that <see cref="ReadVisualTreeAsset"/> does
        /// not read, or null.</summary>
        static string UnsupportedVisualTreeContent(JsonElement f)
        {
            static bool NonEmpty(JsonElement e, string field) =>
                e.TryGetProperty(field, out var v) && v.ValueKind == JsonValueKind.Array && v.GetArrayLength() > 0;
            if (!f.TryGetProperty("m_VisualElementAssets", out var elements) || elements.ValueKind != JsonValueKind.Array) return "no elements";
            if (NonEmpty(f, "m_TemplateAssets")) return "template instances";
            if (NonEmpty(f, "m_Slots")) return "slots";
            if (NonEmpty(f, "m_UxmlObjectEntries")) return "UXML objects";
            foreach (var e in elements.EnumerateArray())
            {
                // An element without inline styles has no rule (-1).
                if (e.TryGetProperty("m_RuleIndex", out _) && Int(e, "m_RuleIndex") >= 0) return "inline styles";
                if (NonEmpty(e, "m_StylesheetPaths") || NonEmpty(e, "m_Stylesheets")) return "style sheets";
                if (!string.IsNullOrEmpty(Str(e, "m_Text"))) return "element text";
            }
            return null;
        }

        static StyleSheet ReadStyleSheet(JsonElement f, Type type)
        {
            StyleSheet sheet;
            using (ObjectRegistry.AsAssets())
            {
                sheet = (StyleSheet)ScriptableObject.CreateInstance(type);
                sheet.name = Str(f, "m_Name") ?? "";
                sheet.IsPersistentAsset = true;
            }
            var floats = new List<float>();
            foreach (var v in f.GetProperty("floats").EnumerateArray()) floats.Add(float.Parse(v.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture));
            var dimensions = new List<Dimension>();
            foreach (var v in f.GetProperty("dimensions").EnumerateArray())
                dimensions.Add(new Dimension(Float(v, "value"), (Dimension.Unit)Int(v, "unit")));
            var colors = new List<Color>();
            foreach (var v in f.GetProperty("colors").EnumerateArray()) colors.Add(new Color(Float(v, "r"), Float(v, "g"), Float(v, "b"), Float(v, "a")));
            var strings = new List<string>();
            foreach (var v in f.GetProperty("strings").EnumerateArray()) strings.Add(v.GetString());
            var assets = new List<Object>();
            foreach (var v in f.GetProperty("assets").EnumerateArray()) assets.Add(ResolveReference(v));
            var rules = new List<StyleRule>();
            foreach (var r in f.GetProperty("m_Rules").EnumerateArray())
            {
                var properties = new List<StyleProperty>();
                foreach (var p in r.GetProperty("m_Properties").EnumerateArray())
                {
                    var values = new List<StyleValueHandle>();
                    foreach (var h in p.GetProperty("m_Values").EnumerateArray())
                        values.Add(new StyleValueHandle(Int(h, "valueIndex"), (StyleValueType)Int(h, "m_ValueType")));
                    properties.Add(new StyleProperty { name = Str(p, "m_Name"), line = Int(p, "m_Line"), values = values.ToArray() });
                }
                rules.Add(new StyleRule { line = Int(r, "line"), properties = properties.ToArray() });
            }
            var complex = new List<StyleComplexSelector>();
            foreach (var c in f.GetProperty("m_ComplexSelectors").EnumerateArray())
            {
                var selectors = new List<StyleSelector>();
                foreach (var s in c.GetProperty("m_Selectors").EnumerateArray())
                {
                    var parts = new List<StyleSelectorPart>();
                    foreach (var part in s.GetProperty("m_Parts").EnumerateArray())
                        parts.Add(new StyleSelectorPart { value = Str(part, "m_Value"), type = (StyleSelectorType)Int(part, "m_Type") });
                    selectors.Add(new StyleSelector { parts = parts.ToArray(), previousRelationship = (StyleSelectorRelationship)Int(s, "m_PreviousRelationship") });
                }
                complex.Add(new StyleComplexSelector { specificity = Int(c, "m_Specificity"), ruleIndex = Int(c, "ruleIndex"), selectors = selectors.ToArray() });
            }
            var imports = new List<StyleSheet.ImportStruct>();
            if (f.TryGetProperty("imports", out var importArray))
                foreach (var i in importArray.EnumerateArray())
                    imports.Add(new StyleSheet.ImportStruct { styleSheet = ResolveReference(i.GetProperty("styleSheet")) as StyleSheet, mediaQueries = Array.Empty<string>() });
            var scalable = new List<ScalableImage>();
            if (f.TryGetProperty("scalableImages", out var scalableArray))
                foreach (var s in scalableArray.EnumerateArray())
                    scalable.Add(new ScalableImage { normalImage = ResolveReference(s.GetProperty("normalImage")) as Texture2D, highResolutionImage = ResolveReference(s.GetProperty("highResolutionImage")) as Texture2D });
            sheet.floats = floats.ToArray();
            sheet.dimensions = dimensions.ToArray();
            sheet.colors = colors.ToArray();
            sheet.strings = strings.ToArray();
            sheet.assets = assets.ToArray();
            sheet.imports = imports.ToArray();
            sheet.scalableImages = scalable.ToArray();
            sheet.contentHash = Int(f, "m_ContentHash");
            sheet.importedWithErrors = Str(f, "m_ImportedWithErrors") == "1";
            sheet.importedWithWarnings = Str(f, "m_ImportedWithWarnings") == "1";
            sheet.rules = rules.ToArray();
            sheet.complexSelectors = complex.ToArray();
            if (sheet.imports.Length > 0) sheet.FlattenImportedStyleSheetsRecursive();
            return sheet;
        }

        static Object ResolveReference(JsonElement r)
        {
            long fileId = r.TryGetProperty("fileID", out var f) ? f.GetInt64() : 0;
            if (fileId == 0) return null;
            var guid = r.TryGetProperty("guid", out var g) ? g.GetString() : null;
            if (string.Equals(guid, EditorGuid, StringComparison.OrdinalIgnoreCase)) return LoadObject(fileId);
            return guid == null ? null : AssetDatabaseRuntime.Load(guid, fileId, null);
        }

        /// <summary>Panel.LoadResource as the Editor answers it in Play mode: the Editor's resources
        /// (with their @2x variant on high-DPI panels), then Resources, then project paths.</summary>
        internal static Object Load(string pathName, Type type, float dpiScaling)
        {
            if (string.IsNullOrEmpty(pathName)) return null;
            bool highDpi = dpiScaling > 1f && type == typeof(Texture2D);
            Object obj = null;
            bool isHighDpi = false;
            string highPath = null;
            if (highDpi)
            {
                highPath = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(pathName) ?? "",
                    System.IO.Path.GetFileNameWithoutExtension(pathName) + "@2x" + System.IO.Path.GetExtension(pathName)).Replace('\\', '/');
                obj = EditorLoad(highPath);
                isHighDpi = obj is Texture2D;
            }
            if (obj == null) obj = EditorLoad(pathName);
            if (obj != null && type != null && !type.IsInstanceOfType(obj)) obj = null;
            if (obj == null) obj = Resources.Load(pathName, type);
            if (obj == null)
            {
                try
                {
                    var info = AssetDatabaseRuntime.InfoByPath(pathName);
                    if (info != null && !string.IsNullOrEmpty(info.Guid)) obj = AssetDatabaseRuntime.LoadMain(info.Guid, type);
                }
                catch (Exception)
                {
                }
            }
            if (obj is Texture2D texture)
            {
                if (isHighDpi) texture.pixelsPerPoint = 2f;
                if (!Mathf.Approximately(dpiScaling % 1f, 0f)) texture.filterMode = FilterMode.Bilinear;
            }
            return obj;
        }
    }
}
