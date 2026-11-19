using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.U2D.Sprites;
using UnityEngine;

/// <summary>
/// What a SerializedObject over a TextureImporter holds (the properties, their types and values,
/// a new array element's tree), what the 2D sprite data provider reads through it, and what an
/// applied edit changes. With <c>IMPORTER_SERIALIZED_OUT</c> set, the Editor (and Unbound) records
/// once the project has loaded and quits; <see cref="Create"/> (batch mode) slices the sheet.
/// </summary>
[InitializeOnLoad]
public static class ImporterSerializedProbe
{
    static readonly string[] Textures = { "Assets/Tex/Plain.png", "Assets/Tex/Single.png", "Assets/Tex/Sheet.png" };

    static ImporterSerializedProbe()
    {
        var output = Environment.GetEnvironmentVariable("IMPORTER_SERIALIZED_OUT");
        if (string.IsNullOrEmpty(output) || SessionState.GetBool("ImporterSerializedProbe.done", false)) return;
        SessionState.SetBool("ImporterSerializedProbe.done", true);
        EditorApplication.delayCall += () =>
        {
            int code = 0;
            var rows = new StringBuilder();
            try { Record(rows); }
            catch (Exception e) { Row(rows, "fatal", e.GetType().Name + ": " + e.Message); code = 1; }
            Row(rows, "complete", "True");
            Directory.CreateDirectory(output);
            File.WriteAllText(Path.Combine(output, "values.jsonl"), rows.ToString());
            EditorApplication.Exit(code);
        };
    }

    /// <summary>Batch mode, -quit: the Plain texture stays Default, Single a single sprite, the
    /// Sheet is sliced into three sprites by the sprite data provider (as the Sprite Editor does).</summary>
    public static void Create()
    {
        var sheet = (TextureImporter)AssetImporter.GetAtPath("Assets/Tex/Sheet.png");
        sheet.textureType = TextureImporterType.Sprite;
        sheet.spriteImportMode = SpriteImportMode.Multiple;
        sheet.SaveAndReimport();
        var factory = new SpriteDataProviderFactories();
        factory.Init();
        var provider = factory.GetSpriteEditorDataProviderFromObject(sheet);
        provider.InitSpriteEditorDataProvider();
        provider.SetSpriteRects(new[]
        {
            new SpriteRect { name = "Sheet_a", rect = new Rect(0, 0, 32, 32), alignment = SpriteAlignment.Center, pivot = new Vector2(0.5f, 0.5f), spriteID = new GUID("0123456789abcdef0123456789abcdef") },
            new SpriteRect { name = "Sheet_b", rect = new Rect(32, 0, 32, 32), alignment = SpriteAlignment.Custom, pivot = new Vector2(0.25f, 0.75f), border = new Vector4(1, 2, 3, 4), spriteID = new GUID("fedcba9876543210fedcba9876543210") },
            new SpriteRect { name = "Sheet_c", rect = new Rect(0, 32, 64, 32), alignment = SpriteAlignment.BottomLeft, pivot = new Vector2(0, 0), spriteID = new GUID("00112233445566778899aabbccddeeff") },
        });
        provider.Apply();
        sheet.SaveAndReimport();
        var single = (TextureImporter)AssetImporter.GetAtPath("Assets/Tex/Single.png");
        single.textureType = TextureImporterType.Sprite;
        single.spriteImportMode = SpriteImportMode.Single;
        single.SaveAndReimport();
    }

    static void Record(StringBuilder rows)
    {
        foreach (var path in Textures)
        {
            var name = Path.GetFileNameWithoutExtension(path);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            Row(rows, name + "/importer", importer == null ? "null" : importer.GetType().FullName);
            if (importer == null) continue;
            Properties(rows, "value/" + name, new SerializedObject(importer));
            Schema(rows, "schema/" + name, importer);
            // A Default texture's provider makes up a sprite with a new GUID each run: sprites only.
            if (importer.textureType == TextureImporterType.Sprite) Provider(rows, "provider/" + name, importer);
        }
        Edit(rows);
    }

    /// <summary>Every property (strings not entered) with its type and value.</summary>
    static void Properties(StringBuilder rows, string prefix, SerializedObject so)
    {
        var it = so.GetIterator();
        bool enter = true;
        while (it.Next(enter))
        {
            enter = it.propertyType != SerializedPropertyType.String;
            Row(rows, prefix + "/" + it.propertyPath, Describe(it));
        }
    }

    /// <summary>The tree with every array emptied and given one new element (so an element's tree
    /// and a new element's values are there), as tests/projects/settings records the settings'.</summary>
    static void Schema(StringBuilder rows, string prefix, TextureImporter importer)
    {
        var so = new SerializedObject(importer);
        var arrays = new List<string>();
        var all = so.GetIterator();
        while (all.Next(all.propertyType != SerializedPropertyType.String))
            if (all.isArray && all.propertyType != SerializedPropertyType.String && all.name != "Array") arrays.Add(all.propertyPath);
        foreach (var a in arrays.Where(a => !arrays.Any(o => o != a && a.StartsWith(o + ".", StringComparison.Ordinal))))
            so.FindProperty(a).arraySize = 0;
        for (int pass = 0; pass < 6; pass++)
        {
            var empty = new List<string>();
            var it = so.GetIterator();
            while (it.Next(true))
                if (it.isArray && it.propertyType != SerializedPropertyType.String && it.name != "Array" && it.arraySize == 0) empty.Add(it.propertyPath);
            if (empty.Count == 0) break;
            foreach (var p in empty) so.FindProperty(p)?.InsertArrayElementAtIndex(0);
        }
        var visible = new HashSet<string>();
        var vis = so.GetIterator();
        while (vis.NextVisible(true)) visible.Add(vis.propertyPath);
        var iter = so.GetIterator();
        bool enter = true;
        while (iter.Next(enter))
        {
            enter = iter.propertyType != SerializedPropertyType.String;
            var p = iter.propertyPath;
            if (Regex.IsMatch(p, @"data\[[1-9][0-9]*\]")) { enter = false; continue; }
            Row(rows, prefix + "/" + p, string.Join("|", iter.name, iter.type, ((int)iter.propertyType).ToString(CultureInfo.InvariantCulture),
                iter.depth.ToString(CultureInfo.InvariantCulture), iter.isArray ? "array" : "-", iter.editable ? "editable" : "readonly",
                visible.Contains(p) ? "visible" : "hidden", Value(iter)));
        }
        so.Dispose();
    }

    /// <summary>What the 2D sprite data provider reads from the importer's SerializedObject
    /// (com.unity.2d.sprite TextureImporterDataProvider), as an OnPostprocessSprites hook reads it.</summary>
    static void Provider(StringBuilder rows, string prefix, TextureImporter importer)
    {
        var factory = new SpriteDataProviderFactories();
        factory.Init();
        var provider = factory.GetSpriteEditorDataProviderFromObject(importer);
        if (provider == null) { Row(rows, prefix, "null"); return; }
        try { provider.InitSpriteEditorDataProvider(); }
        catch (Exception e) { Row(rows, prefix + "/init", e.GetType().Name); return; }
        Row(rows, prefix + "/init", "ok");
        var rects = provider.GetSpriteRects();
        Row(rows, prefix + "/count", rects.Length.ToString(CultureInfo.InvariantCulture));
        foreach (var r in rects)
            Row(rows, prefix + "/rect/" + r.name, string.Join(";", F(r.rect.x), F(r.rect.y), F(r.rect.width), F(r.rect.height), r.alignment.ToString(),
                F(r.pivot.x), F(r.pivot.y), F(r.border.x), F(r.border.y), F(r.border.z), F(r.border.w), r.spriteID.ToString()));
        var names = provider.GetDataProvider<ISpriteNameFileIdDataProvider>();
        if (names != null)
            foreach (var pair in names.GetNameFileIdPairs())
                Row(rows, prefix + "/nameFileId/" + pair.name, pair.GetFileGUID().ToString());
        var outlines = provider.GetDataProvider<ISpriteOutlineDataProvider>();
        if (outlines != null)
            foreach (var r in rects)
                Row(rows, prefix + "/outline/" + r.name, outlines.GetOutlines(r.spriteID).Count.ToString(CultureInfo.InvariantCulture));
        var textures = provider.GetDataProvider<ISecondaryTextureDataProvider>();
        if (textures != null) Row(rows, prefix + "/secondaryTextures", textures.textures.Length.ToString(CultureInfo.InvariantCulture));
    }

    /// <summary>A sprite renamed and a setting changed through a SerializedObject, applied: what
    /// the importer and a new SerializedObject read then (nothing is saved).</summary>
    static void Edit(StringBuilder rows)
    {
        var importer = (TextureImporter)AssetImporter.GetAtPath("Assets/Tex/Sheet.png");
        var so = new SerializedObject(importer);
        var name = so.FindProperty("m_SpriteSheet.m_Sprites.Array.data[1].m_Name");
        Row(rows, "edit/name.before", name == null ? "null" : name.stringValue);
        if (name != null) name.stringValue = "Sheet_renamed";
        var ppu = so.FindProperty("m_SpritePixelsToUnits");
        Row(rows, "edit/ppu.before", ppu == null ? "null" : F(ppu.floatValue));
        if (ppu != null) ppu.floatValue = 64;
        Row(rows, "edit/hasModified", so.hasModifiedProperties.ToString());
        Row(rows, "edit/applied", so.ApplyModifiedPropertiesWithoutUndo().ToString());
        Row(rows, "edit/importer.spritePixelsPerUnit", F(importer.spritePixelsPerUnit));
#pragma warning disable CS0618
        Row(rows, "edit/importer.spritesheet", string.Join(",", importer.spritesheet.Select(s => s.name)));
#pragma warning restore CS0618
        Row(rows, "edit/dirty", EditorUtility.IsDirty(importer).ToString());
        var again = new SerializedObject(importer);
        Row(rows, "edit/again.name", again.FindProperty("m_SpriteSheet.m_Sprites.Array.data[1].m_Name")?.stringValue ?? "null");
        Row(rows, "edit/again.ppu", F(again.FindProperty("m_SpritePixelsToUnits")?.floatValue ?? float.NaN));
    }

    static string Describe(SerializedProperty p) => ((int)p.propertyType).ToString(CultureInfo.InvariantCulture) + "|" + p.type + "|" + Value(p);

    static string Value(SerializedProperty p)
    {
        switch (p.propertyType)
        {
            case SerializedPropertyType.Integer: return p.longValue.ToString(CultureInfo.InvariantCulture);
            case SerializedPropertyType.Boolean: return p.boolValue ? "true" : "false";
            case SerializedPropertyType.Float: return F(p.doubleValue);
            case SerializedPropertyType.String: return p.stringValue ?? "null";
            case SerializedPropertyType.Enum: return p.intValue.ToString(CultureInfo.InvariantCulture);
            case SerializedPropertyType.ArraySize: return p.intValue.ToString(CultureInfo.InvariantCulture);
            case SerializedPropertyType.Vector2: return F(p.vector2Value.x) + "," + F(p.vector2Value.y);
            case SerializedPropertyType.Vector3: return F(p.vector3Value.x) + "," + F(p.vector3Value.y) + "," + F(p.vector3Value.z);
            case SerializedPropertyType.Vector4: return F(p.vector4Value.x) + "," + F(p.vector4Value.y) + "," + F(p.vector4Value.z) + "," + F(p.vector4Value.w);
            case SerializedPropertyType.Rect: return F(p.rectValue.x) + "," + F(p.rectValue.y) + "," + F(p.rectValue.width) + "," + F(p.rectValue.height);
            case SerializedPropertyType.Color: return F(p.colorValue.r) + "," + F(p.colorValue.g) + "," + F(p.colorValue.b) + "," + F(p.colorValue.a);
            case SerializedPropertyType.ObjectReference: return p.objectReferenceValue == null ? "null" : p.objectReferenceValue.GetType().Name + ":" + p.objectReferenceValue.name;
            case SerializedPropertyType.Hash128: return p.hash128Value.ToString();
            default: return p.hasChildren ? "children" : "-";
        }
    }

    static string F(double v) => v.ToString("R", CultureInfo.InvariantCulture);
    static string F(float v) => v.ToString("R", CultureInfo.InvariantCulture);

    static void Row(StringBuilder rows, string key, string value)
    {
        rows.Append("{\"key\":").Append(J(key)).Append(",\"value\":").Append(J(value)).Append("}\n");
    }

    static string J(string s)
    {
        var b = new StringBuilder("\"");
        foreach (var c in s ?? "")
        {
            if (c == '"' || c == '\\') b.Append('\\').Append(c);
            else if (c < ' ') b.Append("\\u").Append(((int)c).ToString("x4"));
            else b.Append(c);
        }
        return b.Append('"').ToString();
    }
}
