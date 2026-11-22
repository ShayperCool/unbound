using System;
using System.Collections;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Four shader files of one name, "Unbound/Dup", in Assets/Shaders/A, B, C and Z (each drawing its
/// own colour, red, green, blue and white, and declaring a property of its own, _OnlyA.._OnlyZ;
/// their GUIDs sort B, C, A, Z, unlike their paths): which file Shader.Find answers, before
/// and after the others are loaded, and the colour each draws, through a material made from each
/// file's shader and through a material asset of B's (Resources/DupB.mat). Writes values.jsonl
/// into SHADER_NAMES_OUT and exits.
/// </summary>
public sealed class ShaderNamesProbe : MonoBehaviour
{
    const string Name = "Unbound/Dup";
    static readonly string[] Files = { "A", "B", "C", "Z" };

    readonly StringBuilder m_Out = new StringBuilder();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot() => new GameObject("ShaderNamesProbe").AddComponent<ShaderNamesProbe>();

    void Rec(string key, string value) => m_Out.Append("{\"k\":\"").Append(key).Append("\",\"v\":\"").Append(value).Append("\"}\n");

    /// <summary>Which file a shader is: the property only that file declares.</summary>
    static string Which(Shader shader)
    {
        if (shader == null) return "null";
        foreach (var file in Files)
            if (shader.FindPropertyIndex("_Only" + file) >= 0) return file;
        return "unknown";
    }

    /// <summary>The colour a material draws over a cleared 4x4 target.</summary>
    static string Draw(Material material)
    {
        var target = new RenderTexture(4, 4, 0, RenderTextureFormat.ARGB32);
        var mesh = new Mesh
        {
            vertices = new[] { new Vector3(-1, -1, 0), new Vector3(-1, 1, 0), new Vector3(1, 1, 0), new Vector3(1, -1, 0) },
            triangles = new[] { 0, 1, 2, 0, 2, 3 },
        };
        var commands = new CommandBuffer();
        commands.SetRenderTarget(target);
        commands.ClearRenderTarget(true, true, Color.black);
        commands.DrawMesh(mesh, Matrix4x4.identity, material, 0, 0);
        Graphics.ExecuteCommandBuffer(commands);
        var previous = RenderTexture.active;
        RenderTexture.active = target;
        var read = new Texture2D(4, 4, TextureFormat.RGBA32, false);
        read.ReadPixels(new Rect(0, 0, 4, 4), 0, 0);
        read.Apply();
        RenderTexture.active = previous;
        var c = read.GetPixel(2, 2);
        commands.Release();
        Destroy(read);
        Destroy(mesh);
        target.Release();
        return $"{c.r:0.##},{c.g:0.##},{c.b:0.##}";
    }

    IEnumerator Start()
    {
        yield return null;
        int code = 0;
        try
        {
            Rec("find/first", Which(Shader.Find(Name)));
            // A material asset of B's, loaded before the files are.
            var asset = Resources.Load<Material>("DupB");
            Rec("asset/shader", Which(asset != null ? asset.shader : null));
            Rec("asset/draw", asset != null ? Draw(asset) : "null");
            Rec("find/asset", Which(Shader.Find(Name)));
#if UNITY_EDITOR
            foreach (var file in Files)
            {
                var shader = UnityEditor.AssetDatabase.LoadAssetAtPath<Shader>("Assets/Shaders/" + file + "/Dup.shader");
                Rec("file/" + file + "/name", shader != null ? shader.name : "null");
                Rec("file/" + file + "/which", Which(shader));
                Rec("file/" + file + "/draw", shader != null ? Draw(new Material(shader)) : "null");
            }
#endif
            Rec("find/loaded", Which(Shader.Find(Name)));
            m_Out.Append("{\"k\":\"complete\",\"v\":true}\n");
        }
        catch (Exception e) { Debug.LogError("shader names probe failed: " + e); code = 1; }
        var dir = Environment.GetEnvironmentVariable("SHADER_NAMES_OUT");
        if (string.IsNullOrEmpty(dir)) Debug.Log(m_Out.ToString());
        else { Directory.CreateDirectory(dir); File.WriteAllText(Path.Combine(dir, "values.jsonl"), m_Out.ToString()); }
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(code);
#else
        Application.Quit(code);
#endif
    }
}
