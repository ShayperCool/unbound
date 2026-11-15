using System;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

// Asset types the engine reads and clones through its own dispatch: the interfaces they declare,
// and Object.Instantiate of runtime-made assets in a nondefault state (the copy's state, then a
// change of the copy against the original).
public class AssetDispatchProbe : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod] static void Install() { new GameObject("AssetDispatchProbe").AddComponent<AssetDispatchProbe>(); }
    [Serializable] class Row { public string key; public string value; }
    StreamWriter output;

    void Put(string key, object value) => output.WriteLine(JsonUtility.ToJson(new Row { key = key, value = Convert.ToString(value, CultureInfo.InvariantCulture) }));

    void Start()
    {
        string path = Environment.GetEnvironmentVariable("ASSET_DISPATCH_OUT"); if (string.IsNullOrEmpty(path)) return;
        try
        {
            using (output = new StreamWriter(path)) { Record(); Put("complete", 1); }
            EditorApplication.Exit(0);
        }
        catch (Exception ex) { Debug.LogException(ex); EditorApplication.Exit(1); }
    }

    static readonly Type[] Types =
    {
        typeof(UnityEngine.AI.NavMeshData), typeof(AnimationClip), typeof(UnityEngine.Audio.AudioMixer), typeof(Avatar), typeof(AvatarMask),
        typeof(BillboardAsset), typeof(ComputeShader), typeof(Cubemap), typeof(CubemapArray), typeof(CustomRenderTexture), typeof(Flare),
        typeof(LightingSettings), typeof(Material), typeof(Mesh), typeof(RenderTexture), typeof(RuntimeAnimatorController), typeof(Shader),
        typeof(ShaderVariantCollection), typeof(Sprite), typeof(TerrainData), typeof(TerrainLayer), typeof(Texture2D), typeof(Texture2DArray),
        typeof(Texture3D), typeof(UnityEngine.U2D.SpriteAtlas),
    };

    void Clone<T>(string key, T original, Func<T, string> state, Action<T> change) where T : UnityEngine.Object
    {
        var copy = Instantiate(original);
        Put(key + ".type", copy.GetType().Name);
        Put(key + ".name", copy.name);
        Put(key + ".distinct", !ReferenceEquals(copy, original));
        Put(key + ".copy", state(copy));
        change(copy);
        Put(key + ".changedCopy", state(copy));
        Put(key + ".original", state(original));
        DestroyImmediate(copy);
    }

    static string F(float v) => v.ToString("R", CultureInfo.InvariantCulture);

    void Record()
    {
        foreach (var type in Types)
            Put("interfaces." + type.Name, string.Join(",", type.GetInterfaces().Select(i => i.FullName).OrderBy(n => n, StringComparer.Ordinal)));

        var mesh = new Mesh { name = "mesh", vertices = new[] { new Vector3(1, 2, 3), Vector3.up, Vector3.right }, triangles = new[] { 0, 1, 2 } };
        Clone("mesh", mesh, m => string.Join(";", m.vertices.Select(v => F(v.x) + "/" + F(v.y) + "/" + F(v.z))) + "|" + string.Join(",", m.triangles),
            m => m.vertices = new[] { new Vector3(7, 8, 9), Vector3.up, Vector3.right });

        var material = new Material(Shader.Find("Unlit/Color")) { name = "material", color = new Color(0.25f, 0.5f, 0.75f, 1f), renderQueue = 2100 };
        Clone("material", material, m => F(m.color.r) + "/" + F(m.color.g) + "/" + F(m.color.b) + "|" + m.renderQueue + "|" + m.shader.name,
            m => { m.color = Color.red; m.renderQueue = 3000; });

        var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false) { name = "texture", filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
        texture.SetPixels32(new[] { new Color32(10, 20, 30, 40), new Color32(50, 60, 70, 80), new Color32(90, 100, 110, 120), new Color32(130, 140, 150, 160) });
        texture.Apply();
        Clone("texture", texture, t => string.Join(",", t.GetPixels32().Select(c => c.r + "-" + c.a)) + "|" + t.filterMode + "|" + t.wrapMode,
            t => { t.SetPixel(0, 0, Color.white); t.Apply(); t.filterMode = FilterMode.Trilinear; });

        var cube = new Cubemap(2, TextureFormat.RGBA32, false) { name = "cube" };
        cube.SetPixel(CubemapFace.NegativeY, 1, 1, new Color32(1, 2, 3, 4));
        cube.Apply();
        Clone("cube", cube, c => { var p = (Color32)c.GetPixel(CubemapFace.NegativeY, 1, 1); return p.r + "/" + p.g + "/" + p.b + "/" + p.a + "|" + c.width; },
            c => { c.SetPixel(CubemapFace.NegativeY, 1, 1, Color.white); c.Apply(); });

        var target = new RenderTexture(32, 16, 24, RenderTextureFormat.ARGB32) { name = "target", antiAliasing = 1, useMipMap = false, wrapMode = TextureWrapMode.Mirror };
        // The depth buffer's bits depend on the graphics device: not compared.
        Clone("renderTexture", target, t => t.width + "x" + t.height + "|" + t.format + "|" + t.wrapMode,
            t => t.wrapMode = TextureWrapMode.Repeat);

        var custom = new CustomRenderTexture(16, 8) { name = "custom", updateMode = CustomRenderTextureUpdateMode.Realtime, initializationColor = new Color(0.5f, 0.25f, 1f, 1f), doubleBuffered = true };
        Clone("customRenderTexture", custom, t => t.width + "x" + t.height + "|" + t.updateMode + "|" + F(t.initializationColor.g) + "|" + t.doubleBuffered,
            t => t.updateMode = CustomRenderTextureUpdateMode.OnDemand);

        var clip = new AnimationClip { name = "clip", frameRate = 24, legacy = true, wrapMode = WrapMode.Loop };
        clip.SetCurve("", typeof(Transform), "localPosition.x", AnimationCurve.Linear(0, 1, 2, 3));
        // WrapMode has aliased values: compared as numbers.
        Clone("clip", clip, c => F(c.frameRate) + "|" + F(c.length) + "|" + c.legacy + "|" + (int)c.wrapMode,
            c => { c.frameRate = 60; c.wrapMode = WrapMode.Once; });

        var lighting = new LightingSettings { name = "lighting", lightmapResolution = 17, bakedGI = false, indirectScale = 0.5f };
        Clone("lighting", lighting, l => F(l.lightmapResolution) + "|" + l.bakedGI + "|" + F(l.indirectScale),
            l => l.lightmapResolution = 3);

        var variants = new ShaderVariantCollection { name = "variants" };
        variants.Add(new ShaderVariantCollection.ShaderVariant(Shader.Find("Unlit/Color"), PassType.Normal));
        Clone("variants", variants, v => v.shaderCount + "|" + v.variantCount, v => v.Clear());

        var sprite = Sprite.Create(texture, new Rect(0, 0, 2, 1), new Vector2(0.25f, 0.75f), 50);
        sprite.name = "sprite";
        Clone("sprite", sprite, s => s.rect + "|" + F(s.pivot.x) + "/" + F(s.pivot.y) + "|" + F(s.pixelsPerUnit) + "|" + (s.texture == texture),
            s => { });

        var controller = new AnimatorOverrideController { name = "overrides" };
        Clone("overrides", (RuntimeAnimatorController)controller, c => c.GetType().Name + "|" + c.animationClips.Length, c => { });

        var shader = Shader.Find("Unlit/Color");
        var shaderCopy = Instantiate(shader);
        Put("shader.type", shaderCopy.GetType().Name);
        Put("shader.name", shaderCopy.name);
        Put("shader.distinct", !ReferenceEquals(shaderCopy, shader));
    }
}
