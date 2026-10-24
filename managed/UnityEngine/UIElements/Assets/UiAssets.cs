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

        static Texture2D ReadTexture(JsonElement f)
        {
            int format = Int(f, "m_TextureFormat");
            if (format != (int)TextureFormat.RGBA32 && format != (int)TextureFormat.ARGB32 && format != (int)TextureFormat.Alpha8)
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
            texture.Apply(false, false);
            return texture;
        }

        static Font ReadFont(JsonElement f, long pathId) => Font.UaFromEditorResource(pathId, Str(f, "m_Name") ?? "", Float(f, "m_FontSize"));

        static Object ReadMonoBehaviour(JsonElement f)
        {
            if (!f.TryGetProperty("m_Script", out var script)) return null;
            long fileId = script.TryGetProperty("fileID", out var fid) ? fid.GetInt64() : 0;
            var type = EngineScripts.Resolve(script.TryGetProperty("guid", out var g) ? g.GetString() : null, fileId);
            if (type == typeof(ThemeStyleSheet) || type == typeof(StyleSheet)) return ReadStyleSheet(f, type);
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
