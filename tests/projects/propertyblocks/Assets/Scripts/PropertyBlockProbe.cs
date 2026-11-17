using System;
using System.Collections;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>What a draw is given when a MaterialPropertyBlock changes after it was set: on a
/// renderer (Renderer.SetPropertyBlock), on one material slot (SetPropertyBlock with an index)
/// and on a terrain (Terrain.SetSplatMaterialPropertyBlock). Every step changes the blocks or the
/// materials, waits a frame, renders a camera looking down on four places and records the colour
/// drawn at each, and what the getters return. Writes values.jsonl into PROPERTY_BLOCKS_OUT and
/// exits.</summary>
public sealed class PropertyBlockProbe : MonoBehaviour
{
    readonly StringBuilder m_Out = new StringBuilder();
    Camera m_Camera;
    RenderTexture m_Target;
    Texture2D m_Read;
    MeshRenderer m_Single, m_Slots;
    Terrain m_Terrain;
    Material m_SingleMaterial, m_SlotMaterial0, m_SlotMaterial1, m_TerrainMaterial;
    // The blocks the steps edit and set again (the same objects throughout).
    MaterialPropertyBlock m_RendererBlock, m_SlotBlock, m_TerrainBlock;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot() => new GameObject("PropertyBlockProbe").AddComponent<PropertyBlockProbe>();

    static readonly Color Red = new Color(1f, 0f, 0f), Green = new Color(0f, 1f, 0f), Blue = new Color(0f, 0f, 1f);
    static readonly Color Yellow = new Color(1f, 1f, 0f), Cyan = new Color(0f, 1f, 1f), Magenta = new Color(1f, 0f, 1f);
    static readonly Color Gray = new Color(0.5f, 0.5f, 0.5f), Orange = new Color(1f, 0.5f, 0f);

    void Rec(string key, string value) =>
        m_Out.Append("{\"k\":\"").Append(key).Append("\",\"v\":\"").Append(value).Append("\"}\n");

    static string Bytes(Color32 c) => c.r + "," + c.g + "," + c.b;

    static string F(float f) => f.ToString("R", CultureInfo.InvariantCulture);

    static string Of(Color c) => F(c.r) + "," + F(c.g) + "," + F(c.b) + "," + F(c.a);

    /// <summary>A quad in the XZ plane (the camera looks down on it) per submesh, at the x given.</summary>
    static Mesh Quads(params float[] xs)
    {
        var mesh = new Mesh();
        var vertices = new Vector3[xs.Length * 4];
        for (int i = 0; i < xs.Length; i++)
        {
            vertices[i * 4] = new Vector3(xs[i] - 0.5f, 0f, -0.5f);
            vertices[i * 4 + 1] = new Vector3(xs[i] + 0.5f, 0f, -0.5f);
            vertices[i * 4 + 2] = new Vector3(xs[i] + 0.5f, 0f, 0.5f);
            vertices[i * 4 + 3] = new Vector3(xs[i] - 0.5f, 0f, 0.5f);
        }
        mesh.vertices = vertices;
        mesh.subMeshCount = xs.Length;
        for (int i = 0; i < xs.Length; i++)
            mesh.SetTriangles(new[] { i * 4, i * 4 + 2, i * 4 + 1, i * 4, i * 4 + 3, i * 4 + 2 }, i);
        mesh.RecalculateBounds();
        return mesh;
    }

    MeshRenderer Renderer(string name, Mesh mesh, params Material[] materials)
    {
        var go = new GameObject(name);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var renderer = go.AddComponent<MeshRenderer>();
        renderer.sharedMaterials = materials;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.lightProbeUsage = LightProbeUsage.Off;
        renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        return renderer;
    }

    static Material Solid(Shader shader, Color color) => new Material(shader) { color = color };

    void Setup()
    {
        // Four places along x: the renderer's block (-1.5), material slots 0 and 1 (-0.5, 0.5),
        // the terrain (1.5).
        m_Target = new RenderTexture(4, 1, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
        m_Read = new Texture2D(4, 1, TextureFormat.RGBA32, false, true);
        var cameraObject = new GameObject("Block Probe Camera");
        cameraObject.transform.SetPositionAndRotation(new Vector3(0f, 5f, 0f), Quaternion.Euler(90f, 0f, 0f));
        m_Camera = cameraObject.AddComponent<Camera>();
        m_Camera.enabled = false;
        m_Camera.orthographic = true;
        m_Camera.orthographicSize = 0.5f;
        m_Camera.nearClipPlane = 0.1f;
        m_Camera.farClipPlane = 20f;
        m_Camera.clearFlags = CameraClearFlags.SolidColor;
        m_Camera.backgroundColor = Color.black;
        m_Camera.allowHDR = false;
        m_Camera.allowMSAA = false;
        m_Camera.targetTexture = m_Target;

        m_RendererBlock = new MaterialPropertyBlock();
        m_SlotBlock = new MaterialPropertyBlock();
        m_TerrainBlock = new MaterialPropertyBlock();
        var shader = Resources.Load<Shader>("BlockProbeSolid");
        m_SingleMaterial = Solid(shader, Gray);
        m_SlotMaterial0 = Solid(shader, Gray);
        m_SlotMaterial1 = Solid(shader, Gray);
        m_TerrainMaterial = Solid(shader, Gray);
        m_Single = Renderer("Single", Quads(-1.5f), m_SingleMaterial);
        m_Slots = Renderer("Slots", Quads(-0.5f, 0.5f), m_SlotMaterial0, m_SlotMaterial1);

        var data = new TerrainData { heightmapResolution = 33, size = new Vector3(1f, 1f, 1f) };
        var terrainObject = Terrain.CreateTerrainGameObject(data);
        terrainObject.transform.position = new Vector3(1f, 0f, -0.5f);
        m_Terrain = terrainObject.GetComponent<Terrain>();
        m_Terrain.materialTemplate = m_TerrainMaterial;
        m_Terrain.drawInstanced = false;
        m_Terrain.basemapDistance = 1000f;
        m_Terrain.shadowCastingMode = ShadowCastingMode.Off;
        m_Terrain.reflectionProbeUsage = ReflectionProbeUsage.Off;
    }

    static void Fill(MaterialPropertyBlock block, Color color)
    {
        block.Clear();
        block.SetColor("_Color", color);
    }

    void SetAll()
    {
        m_Single.SetPropertyBlock(m_RendererBlock);
        m_Slots.SetPropertyBlock(m_SlotBlock, 1);
        m_Terrain.SetSplatMaterialPropertyBlock(m_TerrainBlock);
    }

    /// <summary>The colours drawn at the four places, and what the getters return.</summary>
    void Snapshot(string step)
    {
        m_Camera.Render();
        var previous = RenderTexture.active;
        RenderTexture.active = m_Target;
        m_Read.ReadPixels(new Rect(0, 0, 4, 1), 0, 0);
        m_Read.Apply();
        RenderTexture.active = previous;
        var pixels = m_Read.GetPixels32();
        Rec(step + ".drawn.renderer", Bytes(pixels[0]));
        Rec(step + ".drawn.slot0", Bytes(pixels[1]));
        Rec(step + ".drawn.slot1", Bytes(pixels[2]));
        Rec(step + ".drawn.terrain", Bytes(pixels[3]));

        var read = new MaterialPropertyBlock();
        m_Single.GetPropertyBlock(read);
        Rec(step + ".get.renderer", read.isEmpty ? "empty" : Of(read.GetColor("_Color")));
        m_Slots.GetPropertyBlock(read);
        Rec(step + ".get.slots", read.isEmpty ? "empty" : Of(read.GetColor("_Color")));
        m_Slots.GetPropertyBlock(read, 1);
        Rec(step + ".get.slot1", read.isEmpty ? "empty" : Of(read.GetColor("_Color")));
        m_Terrain.GetSplatMaterialPropertyBlock(read);
        Rec(step + ".get.terrain", read.isEmpty ? "empty" : Of(read.GetColor("_Color")));
        Rec(step + ".has.renderer", m_Single.HasPropertyBlock() ? "true" : "false");
        Rec(step + ".has.slots", m_Slots.HasPropertyBlock() ? "true" : "false");
    }

    IEnumerator Start()
    {
        yield return null;
        int code = 0;
        var dir = Environment.GetEnvironmentVariable("PROPERTY_BLOCKS_OUT");
        bool failed = false;
        try { Setup(); }
        catch (Exception e) { Debug.LogException(e); failed = true; }
        if (!failed)
        {
            var steps = Steps();
            while (true)
            {
                bool more;
                try { more = steps.MoveNext(); }
                catch (Exception e) { Debug.LogException(e); failed = true; break; }
                if (!more) break;
                yield return null;
                try { Snapshot((string)steps.Current); }
                catch (Exception e) { Debug.LogException(e); failed = true; break; }
            }
        }
        if (failed) code = 1;
        else
        {
            m_Out.Append("{\"k\":\"complete\",\"v\":true}\n");
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
                File.WriteAllText(Path.Combine(dir, "values.jsonl"), m_Out.ToString());
            }
            else Debug.Log(m_Out.ToString());
        }
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(code);
#else
        Application.Quit(code);
#endif
    }

    /// <summary>Each step makes its changes and yields its name; a frame later it is drawn.</summary>
    IEnumerator Steps()
    {
        yield return "none";

        // Each block set once.
        Fill(m_RendererBlock, Red);
        Fill(m_SlotBlock, Green);
        Fill(m_TerrainBlock, Blue);
        SetAll();
        yield return "set";

        // The blocks edited in place, not set again.
        m_RendererBlock.SetColor("_Color", Yellow);
        m_SlotBlock.SetColor("_Color", Cyan);
        m_TerrainBlock.SetColor("_Color", Magenta);
        yield return "edited";

        // The same blocks set again.
        SetAll();
        yield return "setAgain";

        // Set again without a change.
        SetAll();
        yield return "setUnchanged";

        // Cleared and filled again, then set (a block reused every frame).
        Fill(m_RendererBlock, Blue);
        Fill(m_SlotBlock, Red);
        Fill(m_TerrainBlock, Green);
        SetAll();
        yield return "refilled";

        // Refilled without _Color: the materials' colours again.
        foreach (var block in new[] { m_RendererBlock, m_SlotBlock, m_TerrainBlock })
        {
            block.Clear();
            block.SetFloat("_Other", 1f);
        }
        SetAll();
        yield return "withoutColor";

        // The materials change under blocks without _Color.
        m_SingleMaterial.color = Orange;
        m_SlotMaterial1.color = Orange;
        m_TerrainMaterial.color = Orange;
        yield return "materialChanged";

        // The materials change under blocks with _Color.
        Fill(m_RendererBlock, Cyan);
        Fill(m_SlotBlock, Magenta);
        Fill(m_TerrainBlock, Yellow);
        SetAll();
        m_SingleMaterial.color = Green;
        m_SlotMaterial1.color = Green;
        m_TerrainMaterial.color = Green;
        yield return "materialUnderBlock";

        // A renderer block on the renderer with a slot block: slot 0 takes it.
        var whole = new MaterialPropertyBlock();
        whole.SetColor("_Color", Blue);
        m_Slots.SetPropertyBlock(whole);
        yield return "rendererAndSlot";
        whole.SetColor("_Color", Red);
        yield return "rendererAndSlotEdited";

        // An edit after the last set and before clearing: cleared, the materials draw.
        m_RendererBlock.SetColor("_Color", Red);
        m_Single.SetPropertyBlock(null);
        m_Slots.SetPropertyBlock(null);
        m_Slots.SetPropertyBlock(null, 1);
        m_Terrain.SetSplatMaterialPropertyBlock(null);
        yield return "cleared";

        // Set again after clearing.
        SetAll();
        yield return "setAfterClear";
    }
}
