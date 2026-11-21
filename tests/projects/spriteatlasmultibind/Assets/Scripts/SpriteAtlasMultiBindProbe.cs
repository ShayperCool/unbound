using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.U2D;
using Object = UnityEngine.Object;

/// <summary>
/// Measures Unity 6000.0 runtime multi-atlas binding in Play mode across V1 (AlwaysOnAtlas) and
/// V2 (SpriteAtlasV2): built-in master GUID word ordering, built-in vs excluded eligibility,
/// master vs variant priority, excluded master with multiple built-in variants, and late-binding
/// request/registration order controls.
/// </summary>
public sealed class SpriteAtlasMultiBindProbe : MonoBehaviour
{
    readonly StringBuilder m_Output = new StringBuilder();
    readonly List<string> m_Requests = new List<string>();
    readonly List<string> m_CallbackSteps = new List<string>();
    readonly List<string> m_RegisteredEvents = new List<string>();
    readonly Dictionary<string, SpriteAtlas> m_Atlases = new Dictionary<string, SpriteAtlas>(StringComparer.Ordinal);
    readonly Dictionary<string, Sprite> m_SheetSprites = new Dictionary<string, Sprite>(StringComparer.Ordinal);

    Sprite m_Single;
    Sprite m_FolderBig;
    Sprite m_FolderTri;
    Texture2D m_SingleTex;
    Texture2D m_FolderBigTex;
    Texture2D m_SheetTex;
    Texture2D m_FolderTriTex;
    string m_OutDir;
    int m_Frames;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        var dir = Environment.GetEnvironmentVariable("SPRITE_ATLAS_MULTIBIND_OUT");
        if (string.IsNullOrEmpty(dir)) return;
        var go = new GameObject("SpriteAtlasMultiBindProbe");
        DontDestroyOnLoad(go);
        var probe = go.AddComponent<SpriteAtlasMultiBindProbe>();
        probe.StartProbe(dir);
    }

    void Put(string key, string value) => m_Output.Append("{\"k\":").Append(Quote(key))
        .Append(",\"v\":").Append(Quote(value)).Append("}\n");

    static string RectStr(Rect r) => string.Format(CultureInfo.InvariantCulture,
        "{0:0.##},{1:0.##},{2:0.##},{3:0.##}", r.x, r.y, r.width, r.height);

    string MatchSummary(Sprite sprite, Texture2D sourceTex, params string[] candidates)
    {
        if (sprite == null) return "<null>";
        bool hasTex = sprite.texture != null;
        bool isSource = hasTex && ReferenceEquals(sprite.texture, sourceTex);
        var matched = new List<string>();
        var cloneDistinct = new List<string>();
        for (int i = 0; i < candidates.Length; i++)
        {
            var stem = candidates[i];
            if (!m_Atlases.TryGetValue(stem, out var atlas) || atlas == null) continue;
            var clone = atlas.GetSprite(sprite.name);
            if (clone == null) continue;
            bool sameTex = hasTex && ReferenceEquals(sprite.texture, clone.texture);
            bool sameRect = sprite.textureRect == clone.textureRect;
            if (sameTex) matched.Add(stem + (sameRect ? "(rectSame)" : "(rectDiff)"));
            cloneDistinct.Add($"{stem}:scale={clone.spriteAtlasTextureScale.ToString(CultureInfo.InvariantCulture)}|rect={RectStr(clone.textureRect)}");
            DestroyImmediate(clone);
        }
        string hit = matched.Count == 0 ? "<none>" : string.Join(",", matched);
        return $"packed={sprite.packed}|hasTex={hasTex}|isSource={isSource}|scale={sprite.spriteAtlasTextureScale.ToString(CultureInfo.InvariantCulture)}|rect={RectStr(sprite.textureRect)}|match={hit}";
    }

    void OnAtlasRequested(string tag, Action<SpriteAtlas> callback)
    {
        m_Requests.Add(tag);
        if (tag == "LateFirst")
        {
            callback(m_Atlases["LateFirst"]);
            m_CallbackSteps.Add("afterLateFirst:" + MatchSummary(m_SheetSprites["SheetACell2"], m_SheetTex, "LateFirst", "LateSecond"));
        }
        else if (tag == "LateSecond")
        {
            callback(m_Atlases["LateSecond"]);
            m_CallbackSteps.Add("afterLateSecond:" + MatchSummary(m_SheetSprites["SheetACell2"], m_SheetTex, "LateFirst", "LateSecond"));
        }
        else if (tag == "LateVarMaster")
        {
            callback(m_Atlases["LateVarChild"]);
            m_CallbackSteps.Add("afterLateVarChild:" + MatchSummary(m_SheetSprites["SheetACell3"], m_SheetTex, "LateVarMaster", "LateVarChild"));
        }
        else if (tag == "LateReverseB")
        {
            callback(m_Atlases["LateReverseB"]);
            m_CallbackSteps.Add("afterLateReverseB:" + MatchSummary(m_FolderTri, m_FolderTriTex, "LateReverseA", "LateReverseB"));
        }
        // Intentionally ignore LateReverseA to verify only the registered atlas binds FolderTri.
    }

    void OnAtlasRegistered(SpriteAtlas atlas)
    {
        m_RegisteredEvents.Add(atlas != null ? atlas.name : "<null>");
    }

    void StartProbe(string dir)
    {
        m_OutDir = dir;
        Directory.CreateDirectory(m_OutDir);

        SpriteAtlasManager.atlasRequested += OnAtlasRequested;
        SpriteAtlasManager.atlasRegistered += OnAtlasRegistered;

        var paths = AssetDatabase.FindAssets("t:SpriteAtlas", new[] { "Assets/Atlases" })
            .Select(AssetDatabase.GUIDToAssetPath)
            .Distinct()
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToArray();
        Put("atlases", string.Join("|", paths));
        foreach (var path in paths)
        {
            var atlas = AssetDatabase.LoadAssetAtPath<SpriteAtlas>(path);
            if (atlas != null) m_Atlases[Path.GetFileNameWithoutExtension(path)] = atlas;
        }

        // Capture logs while loading the multi-atlas sprites so built-in multi-atlas warnings are recorded.
        var loadLogs = new List<string>();
        Application.LogCallback logHandler = (cond, _, type) =>
        {
            if (cond.Contains("atlas", StringComparison.OrdinalIgnoreCase))
                loadLogs.Add(type + ":" + cond.Replace("\r", "").Replace("\n", "\\n"));
        };
        Application.logMessageReceived += logHandler;
        try
        {
            m_SingleTex = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Sprites/Single.png");
            m_Single = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Sprites/Single.png");

            m_FolderBigTex = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Sprites/Folder/FolderBig.png");
            m_FolderBig = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Sprites/Folder/FolderBig.png");

            m_SheetTex = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Sprites/Sheet.png");
            foreach (var s in AssetDatabase.LoadAllAssetsAtPath("Assets/Sprites/Sheet.png").OfType<Sprite>())
                m_SheetSprites[s.name] = s;

            m_FolderTriTex = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Sprites/Folder/Nested/FolderTri.png");
            m_FolderTri = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Sprites/Folder/Nested/FolderTri.png");
        }
        finally
        {
            Application.logMessageReceived -= logHandler;
        }

        // Verify candidate atlases produce distinct clone textures/rects so match checks are not membership-only.
        Put("case1.twoBuiltInMasters", MatchSummary(m_Single, m_SingleTex, "A_MasterLowWord", "Z_MasterHighWord"));
        Put("case1.distinctPages", DistinctPages("Single", "A_MasterLowWord", "Z_MasterHighWord"));

        Put("case2.builtInVsExcluded", MatchSummary(m_FolderBig, m_FolderBigTex, "IncAtlas", "ExclAtlas"));
        Put("case2.distinctPages", DistinctPages("FolderBig", "IncAtlas", "ExclAtlas"));

        Put("case3.builtInMasterVsVariant", MatchSummary(m_SheetSprites["SheetACell0"], m_SheetTex, "MasterBoth", "VariantBoth"));
        Put("case3.distinctPages", DistinctPages("SheetACell0", "MasterBoth", "VariantBoth"));

        Put("case4.exclMasterTwoBuiltInVariants", MatchSummary(m_SheetSprites["SheetACell1"], m_SheetTex, "MasterExcl", "A_VariantLowWord", "Z_VariantHighWord"));
        Put("case4.distinctPages", DistinctPages("SheetACell1", "A_VariantLowWord", "Z_VariantHighWord"));

        Put("case5.lateInitial", MatchSummary(m_SheetSprites["SheetACell2"], m_SheetTex, "LateFirst", "LateSecond"));
        Put("case6.lateVariantInitial", MatchSummary(m_SheetSprites["SheetACell3"], m_SheetTex, "LateVarMaster", "LateVarChild"));
        Put("case7.lateSecondOnlyInitial", MatchSummary(m_FolderTri, m_FolderTriTex, "LateReverseA", "LateReverseB"));

        Put("loadLogs", loadLogs.Count == 0 ? "<none>" : string.Join("||", loadLogs));
    }

    string DistinctPages(string spriteName, string stemA, string stemB)
    {
        var a = m_Atlases[stemA].GetSprite(spriteName);
        var b = m_Atlases[stemB].GetSprite(spriteName);
        bool diffTex = a != null && b != null && !ReferenceEquals(a.texture, b.texture);
        bool diffRectOrScale = a != null && b != null && (a.textureRect != b.textureRect || a.spriteAtlasTextureScale != b.spriteAtlasTextureScale);
        if (a != null) DestroyImmediate(a);
        if (b != null) DestroyImmediate(b);
        return $"diffTex={diffTex}|diffRectOrScale={diffRectOrScale}";
    }

    void Update()
    {
        m_Frames++;
        if (m_Frames < 2) return;

        SpriteAtlasManager.atlasRequested -= OnAtlasRequested;
        SpriteAtlasManager.atlasRegistered -= OnAtlasRegistered;

        Put("late.requests", m_Requests.Count == 0 ? "<none>" : string.Join("|", m_Requests));
        Put("late.registered", m_RegisteredEvents.Count == 0 ? "<none>" : string.Join("|", m_RegisteredEvents));
        Put("late.callbackSteps", m_CallbackSteps.Count == 0 ? "<none>" : string.Join("||", m_CallbackSteps));
        Put("case5.lateFinal", MatchSummary(m_SheetSprites["SheetACell2"], m_SheetTex, "LateFirst", "LateSecond"));
        Put("case6.lateVariantFinal", MatchSummary(m_SheetSprites["SheetACell3"], m_SheetTex, "LateVarMaster", "LateVarChild"));
        Put("case7.lateSecondOnlyFinal", MatchSummary(m_FolderTri, m_FolderTriTex, "LateReverseA", "LateReverseB"));

        m_Output.Append("{\"k\":\"complete\",\"v\":true}\n");
        File.WriteAllText(Path.Combine(m_OutDir, "values.jsonl"), m_Output.ToString());
        EditorApplication.Exit(0);
    }

    static string Quote(string s)
    {
        var b = new StringBuilder("\"");
        foreach (char c in s ?? "<null>")
        {
            if (c == '"' || c == '\\') b.Append('\\').Append(c);
            else if (c < ' ') b.Append("\\u").Append(((int)c).ToString("x4"));
            else b.Append(c);
        }
        return b.Append('"').ToString();
    }
}
