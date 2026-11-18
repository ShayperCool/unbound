using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

/// <summary>
/// Which sampler state a graphics program samples a texture through: its own, another texture's,
/// in the vertex stage, beside loads and legacy samplers, for cubes and volumes, and with
/// comparisons. Each case is a shader `Probe/SamplerStates/&lt;case&gt;` drawn into one float
/// texel; `_A` and `_C` are point filtered, `_B` is bilinear, so a sample between two texels
/// tells the state. The comparison and depth cases are drawn again with depth targets of two
/// texels, 0.25 and 0.75, of each depth format and filter (`&lt;case&gt;.&lt;target&gt;`): there a
/// comparison, a filter and a depth read give different values, recorded in twentieths (5 is
/// the first texel, 8 both filtered, 6 and 14 a filtered comparison) so that the bits a depth
/// buffer stores do not tell; `DepthChannels.&lt;target&gt;.raw` is the value as sampled. Last,
/// the programs that fill the depth targets drawn where there is no depth buffer: `Fill.color`
/// writes a color and the depth, `DepthOnly` only the depth (`DepthOnly.color` over a cleared
/// target, `DepthOnly.depth` into a depth target). After those, stencil tests (`Stencil.*`) and
/// compute kernels reading the depth targets (`Compute.*`). Writes `values.jsonl` into
/// `SAMPLER_STATES_OUT` and quits.
/// </summary>
public class UitkProbe : MonoBehaviour
{
    public UnityEngine.UIElements.UIDocument document;
    public Texture2D picture;

    static readonly string[] Cases =
    {
        "One", "Two", "Foreign", "Main", "Load", "Legacy", "Shapes", "Vertex",
        "CmpOnly", "CmpTwo", "CmpOwn", "CmpInline", "CmpOther",
        "Inline", "DepthPlain", "DepthChannels",
    };

    /// <summary>What an inline sampler's name spells (wrap modes of one axis, a volume's W, the
    /// anisotropic level, which word wins) and what counts as using a sampler's texture, then
    /// the wrongly spelt names (`Bad*`: two filters, `Clamp` with `Repeat` or `Mirror`, an
    /// anisotropic level that is none); recorded after the other rows.</summary>
    static readonly string[] LateCases =
    {
        "InlineAxes", "InlineMirrorOnce", "InlineVolume", "InlineAniso", "UsedLoad", "UsedVertex",
        "InlineV", "InlineWords",
        "BadFilters", "BadClampRepeat", "BadClampMirror", "BadAniso", "BadAnisoTail", "BadBare",
    };

    /// <summary>The value of each mip level of `_Mips`.</summary>
    static readonly float[] LevelValues = { 0.1f, 0.3f, 0.5f, 0.7f, 0.9f };

    /// <summary>The cases that sample `_Shadow`, drawn again with each depth target.</summary>
    static readonly string[] DepthCases =
    {
        "CmpOnly", "CmpTwo", "CmpOwn", "CmpInline", "CmpOther", "DepthPlain", "DepthChannels",
    };

    /// <summary>A depth target of two texels, 0.25 and 0.75.</summary>
    static RenderTexture Split(RenderTextureFormat format, FilterMode filter, Texture source)
    {
        var texture = new RenderTexture(2, 1, 16, format);
        texture.filterMode = filter;
        texture.Create();
        Graphics.Blit(source, texture, new Material(Shader.Find("Probe/SamplerStates/Fill")));
        return texture;
    }

    // Own and inline samplers along each volume axis. Samples outside both ends distinguish
    // mirror-clamp from both ordinary clamp and repeated mirroring.
    static void MirrorWrap(StringBuilder rows)
    {
        var volume = new Texture3D(2, 2, 2, TextureFormat.RGBAFloat, false);
        volume.filterMode = FilterMode.Point;
        var colors = new Color[8];
        for (int i = 0; i < colors.Length; i++) colors[i] = new Color(i / 8f, 0, 0, 1);
        volume.SetPixels(colors);
        volume.Apply(false);
        var target = new RenderTexture(1, 1, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
        target.Create();
        var cpu = new Texture2D(1, 1, TextureFormat.RGBAFloat, false, true);
        var material = new Material(Shader.Find("Probe/SamplerStates/MirrorWrap"));
        material.SetTexture("_Vol", volume);
        foreach (var mode in new[] { TextureWrapMode.Clamp, TextureWrapMode.Mirror, TextureWrapMode.MirrorOnce })
        {
            volume.wrapMode = mode;
            for (int axis = 0; axis < 3; axis++)
            {
                material.SetVector("_Axis", new Vector4(axis == 0 ? 1 : 0, axis == 1 ? 1 : 0, axis == 2 ? 1 : 0, 0));
                material.SetFloat("_Inline", 0);
                Graphics.Blit(null, target, material);
                Row(rows, "MirrorWrap.own." + mode + "." + axis, Read(target, cpu));
                if (mode == TextureWrapMode.Clamp)
                {
                    material.SetFloat("_Inline", 1);
                    Graphics.Blit(null, target, material);
                    Row(rows, "MirrorWrap.inline." + axis, Read(target, cpu));
                }
            }
        }
        DestroyImmediate(material); DestroyImmediate(cpu); DestroyImmediate(target); DestroyImmediate(volume);
    }

    static Texture2D Pair(float first, float second, FilterMode filter)
    {
        var texture = new Texture2D(2, 1, TextureFormat.RGBAFloat, false, true);
        texture.filterMode = filter;
        texture.wrapMode = TextureWrapMode.Clamp;
        texture.SetPixels(new[] { new Color(first, first, first, 1), new Color(second, second, second, 1) });
        texture.Apply(false, false);
        return texture;
    }

    void Start()
    {
        var output = Environment.GetEnvironmentVariable("SAMPLER_STATES_OUT");
        Directory.CreateDirectory(output);
        var rows = new StringBuilder();
        try
        {
            var a = Pair(0.25f, 0.75f, FilterMode.Point);
            var b = Pair(0.5f, 1f, FilterMode.Bilinear);
            var c = Pair(0.125f, 0.625f, FilterMode.Point);
            var shadow = new RenderTexture(2, 1, 16, RenderTextureFormat.Depth);
            shadow.filterMode = FilterMode.Point;
            shadow.Create();
            var previous = RenderTexture.active;
            RenderTexture.active = shadow;
            GL.Clear(true, true, Color.black, 1f);
            RenderTexture.active = previous;
            var texels = new[] { new Color(.25f, 0, 0, 1), new Color(.25f, 0, 0, 1), new Color(.75f, 0, 0, 1), new Color(.75f, 0, 0, 1) };
            var cube = new Cubemap(2, TextureFormat.RGBAFloat, false);
            cube.filterMode = FilterMode.Point;
            for (int face = 0; face < 6; face++) cube.SetPixels(texels, (CubemapFace)face);
            cube.Apply(false);
            var volume = new Texture3D(2, 1, 1, TextureFormat.RGBAFloat, false);
            volume.filterMode = FilterMode.Point;
            volume.wrapMode = TextureWrapMode.Clamp;
            volume.SetPixels(new[] { new Color(.25f, 0, 0, 1), new Color(.75f, 0, 0, 1) });
            volume.Apply(false);

            // A volume of two slices along W, 0.25 and 0.75, and a texture whose mip levels
            // are 0.1, 0.3, 0.5, 0.7 and 0.9.
            var deep = new Texture3D(1, 1, 2, TextureFormat.RGBAFloat, false);
            deep.filterMode = FilterMode.Point;
            deep.wrapMode = TextureWrapMode.Clamp;
            deep.SetPixels(new[] { new Color(.25f, 0, 0, 1), new Color(.75f, 0, 0, 1) });
            deep.Apply(false);
            // Four texels, bilinear and clamped: 0.2 and 0.4 below 0.6 and 0.8.
            var quad = new Texture2D(2, 2, TextureFormat.RGBAFloat, false, true);
            quad.filterMode = FilterMode.Bilinear;
            quad.wrapMode = TextureWrapMode.Clamp;
            quad.SetPixels(new[] { new Color(.2f, 0, 0, 1), new Color(.4f, 0, 0, 1), new Color(.6f, 0, 0, 1), new Color(.8f, 0, 0, 1) });
            quad.Apply(false, false);
            var mips = new Texture2D(16, 16, TextureFormat.RGBAFloat, true, true);
            mips.filterMode = FilterMode.Trilinear;
            mips.wrapMode = TextureWrapMode.Clamp;
            mips.anisoLevel = 1;
            for (int level = 0; level < mips.mipmapCount; level++)
            {
                float value = LevelValues[level];
                var levelTexels = new Color[Math.Max(1, 16 >> level) * Math.Max(1, 16 >> level)];
                for (int i = 0; i < levelTexels.Length; i++) levelTexels[i] = new Color(value, value, value, 1);
                mips.SetPixels(levelTexels, level);
            }
            mips.Apply(false);

            var target = new RenderTexture(1, 1, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
            target.Create();
            var readback = new Texture2D(1, 1, TextureFormat.RGBAFloat, false, true);
            var depths = new (string, RenderTexture)[]
            {
                ("split", Split(RenderTextureFormat.Depth, FilterMode.Point, a)),
                ("splitLinear", Split(RenderTextureFormat.Depth, FilterMode.Bilinear, a)),
                ("map", Split(RenderTextureFormat.Shadowmap, FilterMode.Point, a)),
                ("mapLinear", Split(RenderTextureFormat.Shadowmap, FilterMode.Bilinear, a)),
            };
            var twentieths = new Material(Shader.Find("Probe/SamplerStates/Twentieths"));
            var rounded = new RenderTexture(1, 1, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
            rounded.Create();
            // The depth targets' cases in twentieths, and each target as sampled.
            var draws = new List<(string name, string key, RenderTexture depth, bool exact)>();
            foreach (var name in Cases) draws.Add((name, name, shadow, true));
            foreach (var (suffix, depth) in depths)
            {
                foreach (var name in DepthCases) draws.Add((name, name + "." + suffix, depth, false));
                draws.Add(("DepthChannels", "DepthChannels." + suffix + ".raw", depth, true));
            }
            // A depth target filled with the program that writes only the depth.
            var only = new Material(Shader.Find("Probe/SamplerStates/DepthOnly"));
            var onlyDepth = new RenderTexture(2, 1, 16, RenderTextureFormat.Depth);
            onlyDepth.filterMode = FilterMode.Point;
            onlyDepth.Create();
            Graphics.Blit(a, onlyDepth, only);
            draws.Add(("DepthChannels", "DepthOnly.depth", onlyDepth, false));
            void Draw(string name, string key, RenderTexture depth, bool exact)
            {
                var shader = Shader.Find("Probe/SamplerStates/" + name);
                if (shader == null)
                {
                    rows.Append("{\"k\":\"").Append(key).Append("\",\"v\":\"missing\"}\n");
                    return;
                }
                var material = new Material(shader);
                material.SetTexture("_A", a);
                material.SetTexture("_B", b);
                material.SetTexture("_C", c);
                material.SetTexture("_Shadow", depth);
                material.SetTexture("_Cube", cube);
                material.SetTexture("_Vol", volume);
                material.SetTexture("_Deep", deep);
                material.SetTexture("_Mips", mips);
                material.SetTexture("_Q", quad);
                Graphics.Blit(a, target, material);
                if (!exact) Graphics.Blit(target, rounded, twentieths);
                Row(rows, key, Read(exact ? target : rounded, readback));
            }
            foreach (var (name, key, depth, exact) in draws) Draw(name, key, depth, exact);
            // The depth-writing programs where there is no depth buffer.
            Graphics.Blit(a, target, new Material(Shader.Find("Probe/SamplerStates/Fill")));
            Row(rows, "Fill.color", Read(target, readback));
            previous = RenderTexture.active;
            RenderTexture.active = target;
            GL.Clear(true, true, new Color(0.25f, 0.5f, 0.75f, 1f));
            RenderTexture.active = previous;
            Graphics.Blit(a, target, only);
            Row(rows, "DepthOnly.color", Read(target, readback));
            foreach (var name in LateCases) Draw(name, name, shadow, true);
            Stencils(rows);
            Computes(rows, depths);
            StencilFaces(rows);
            RendererFaces(rows);
            MirrorWrap(rows);
            D16Controls(rows);
            DepthStateControls(rows);
            PoolDepthControls(rows);
            RawDepthSourceControls(rows);
            FrontFaces(rows);
            LenientNames(rows);
            PackedTargets(rows);
            DepthTargetReads(rows);
            rows.Append("{\"k\":\"complete\",\"v\":true}\n");
        }
        catch (Exception e)
        {
            rows.Append("{\"k\":\"fatal\",\"v\":\"").Append(e.GetType().Name).Append("\"}\n");
            Debug.LogException(e);
        }
        File.WriteAllText(Path.Combine(output, "values.jsonl"), rows.ToString());
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(0);
#else
        Application.Quit();
#endif
    }

    /// <summary>Recorded last: what a stencil test does in a target of 16 depth bits (no stencil
    /// buffer, as documented for RenderTexture.depth), of 24, and of the D16_UNorm_S8_UInt
    /// depth-stencil format. Each target of two texels is cleared, its left texel drawn with
    /// stencil 1, then the whole target drawn red where the stencil is 1.</summary>
    static void Stencils(StringBuilder rows)
    {
        var write = new Material(Shader.Find("Probe/SamplerStates/StencilWrite"));
        var test = new Material(Shader.Find("Probe/SamplerStates/StencilTest"));
        var readback = new Texture2D(2, 1, TextureFormat.RGBAFloat, false, true);
        var targets = new (string, RenderTexture)[]
        {
            ("depth16", new RenderTexture(2, 1, 16, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear)),
            ("depth24", new RenderTexture(2, 1, 24, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear)),
            ("d16s8", new RenderTexture(2, 1, UnityEngine.Experimental.Rendering.GraphicsFormat.R32G32B32A32_SFloat,
                UnityEngine.Experimental.Rendering.GraphicsFormat.D16_UNorm_S8_UInt)),
        };
        void Quad(Material material, float right)
        {
            material.SetPass(0);
            GL.PushMatrix();
            GL.LoadOrtho();
            GL.Begin(GL.QUADS);
            GL.Vertex3(0, 0, 0); GL.Vertex3(0, 1, 0); GL.Vertex3(right, 1, 0); GL.Vertex3(right, 0, 0);
            GL.End();
            GL.PopMatrix();
        }
        foreach (var (name, target) in targets)
        {
            target.filterMode = FilterMode.Point;
            target.Create();
            var previous = RenderTexture.active;
            RenderTexture.active = target;
            GL.Clear(true, true, new Color(0, 0, 0, 1));
            Quad(write, 0.5f);
            Quad(test, 1f);
            readback.ReadPixels(new Rect(0, 0, 2, 1), 0, 0);
            readback.Apply();
            RenderTexture.active = previous;
            Row(rows, "Stencil." + name + ".left", readback.GetPixel(0, 0));
            Row(rows, "Stencil." + name + ".right", readback.GetPixel(1, 0));
            rows.Append("{\"k\":\"Stencil.").Append(name).Append(".format\",\"v\":\"").Append(target.depthStencilFormat).Append("\"}\n");
        }
    }

    /// <summary>Recorded last: which stencil face state a mirrored draw takes.
    /// A two-texel target with a stencil buffer gets a quad over its left texel and the same quad
    /// mirrored (a matrix of negative determinant) over its right one, through a two-sided stencil
    /// state that writes 1 for front faces and 255 for back faces; then red where the stencil is 1,
    /// green where it is 255. The same with GL.invertCulling set.</summary>
    static void StencilFaces(StringBuilder rows)
    {
#if UNITY_EDITOR
        var faces = new Material(UnityEditor.AssetDatabase.LoadAssetAtPath<Shader>("Assets/Shaders/StencilFaces.shader"));
        var facesCulled = new Material(UnityEditor.AssetDatabase.LoadAssetAtPath<Shader>("Assets/Shaders/StencilFacesCullBack.shader"));
        var equal = new Material(UnityEditor.AssetDatabase.LoadAssetAtPath<Shader>("Assets/Shaders/StencilEqual.shader"));
#else
        var faces = new Material(Shader.Find("Probe/SamplerStates/StencilFaces"));
        var facesCulled = new Material(Shader.Find("Probe/SamplerStates/StencilFacesCullBack"));
        var equal = new Material(Shader.Find("Probe/SamplerStates/StencilEqual"));
#endif
        var quad = new Mesh { vertices = new[] { new Vector3(0, 0, 0), new Vector3(0, 1, 0), new Vector3(.5f, 1, 0), new Vector3(.5f, 0, 0) } };
        quad.triangles = new[] { 0, 1, 2, 0, 2, 3 };
        var full = new Mesh { vertices = new[] { new Vector3(0, 0, 0), new Vector3(0, 1, 0), new Vector3(1, 1, 0), new Vector3(1, 0, 0) } };
        full.triangles = new[] { 0, 1, 2, 0, 2, 3 };
        var readback = new Texture2D(2, 1, TextureFormat.RGBAFloat, false, true);
        var target = new RenderTexture(2, 1, 24, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear) { filterMode = FilterMode.Point };
        target.Create();
        var mirrored = Matrix4x4.TRS(new Vector3(1, 0, 0), Quaternion.identity, new Vector3(-1, 1, 1));
        foreach (bool invert in new[] { false, true })
        {
            var previous = RenderTexture.active;
            RenderTexture.active = target;
            GL.Clear(true, true, new Color(0, 0, 0, 1));
            GL.PushMatrix();
            GL.LoadOrtho();
            GL.invertCulling = invert;
            faces.SetPass(0);
            Graphics.DrawMeshNow(quad, Matrix4x4.identity);
            Graphics.DrawMeshNow(quad, mirrored);
            GL.invertCulling = false;
            equal.SetInt("_Ref", 1); equal.SetColor("_Color", Color.red); equal.SetPass(0);
            Graphics.DrawMeshNow(full, Matrix4x4.identity);
            equal.SetInt("_Ref", 255); equal.SetColor("_Color", Color.green); equal.SetPass(0);
            Graphics.DrawMeshNow(full, Matrix4x4.identity);
            GL.PopMatrix();
            readback.ReadPixels(new Rect(0, 0, 2, 1), 0, 0);
            readback.Apply();
            RenderTexture.active = previous;
            string name = "StencilFaces." + (invert ? "invertCulling" : "plain");
            Row(rows, name + ".normal", readback.GetPixel(0, 0));
            Row(rows, name + ".mirrored", readback.GetPixel(1, 0));
        }
        // Then the same quads drawn culling back faces (a mirrored quad drawn shows that the draw
        // flips its culling), and both kinds drawn by a command buffer.
        void Test()
        {
            equal.SetInt("_Ref", 1); equal.SetColor("_Color", Color.red); equal.SetPass(0);
            Graphics.DrawMeshNow(full, Matrix4x4.identity);
            equal.SetInt("_Ref", 255); equal.SetColor("_Color", Color.green); equal.SetPass(0);
            Graphics.DrawMeshNow(full, Matrix4x4.identity);
        }
        void Record(string name)
        {
            readback.ReadPixels(new Rect(0, 0, 2, 1), 0, 0);
            readback.Apply();
            Row(rows, "StencilFaces." + name + ".normal", readback.GetPixel(0, 0));
            Row(rows, "StencilFaces." + name + ".mirrored", readback.GetPixel(1, 0));
        }
        {
            var previous = RenderTexture.active;
            RenderTexture.active = target;
            GL.Clear(true, true, new Color(0, 0, 0, 1));
            GL.PushMatrix();
            GL.LoadOrtho();
            facesCulled.SetPass(0);
            Graphics.DrawMeshNow(quad, Matrix4x4.identity);
            Graphics.DrawMeshNow(quad, mirrored);
            Test();
            GL.PopMatrix();
            Record("cullBack");
            RenderTexture.active = previous;
        }
        foreach (var (material, name) in new[] { (faces, "commandBuffer"), (facesCulled, "commandBufferCullBack") })
        {
            using (var cmd = new UnityEngine.Rendering.CommandBuffer())
            {
                cmd.SetRenderTarget(target);
                cmd.ClearRenderTarget(true, true, new Color(0, 0, 0, 1));
                cmd.SetViewProjectionMatrices(Matrix4x4.identity, Matrix4x4.Ortho(0, 1, 0, 1, -1, 1));
                cmd.DrawMesh(quad, Matrix4x4.identity, material, 0, 0);
                cmd.DrawMesh(quad, mirrored, material, 0, 0);
                Graphics.ExecuteCommandBuffer(cmd);
            }
            var previous = RenderTexture.active;
            RenderTexture.active = target;
            GL.PushMatrix();
            GL.LoadOrtho();
            Test();
            GL.PopMatrix();
            Record(name);
            RenderTexture.active = previous;
        }
    }

    // Negative renderer transforms, including a reflected parent, tested independently of
    // the immediate/command-buffer mesh draws above. Both stencil faces and Cull Back are read.
    static void RendererFaces(StringBuilder rows)
    {
        Material Material(string name) => new Material(Shader.Find("Probe/SamplerStates/" + name));
        var faces = Material("StencilFaces");
        var culled = Material("StencilFacesCullBack");
        var equal = Material("StencilEqual");
        var quad = new Mesh { vertices = new[] { new Vector3(0, 0, 0), new Vector3(0, 1, 0), new Vector3(.5f, 1, 0), new Vector3(.5f, 0, 0) },
            triangles = new[] { 0, 1, 2, 0, 2, 3 }, bindposes = new[] { Matrix4x4.identity } };
        quad.boneWeights = new[] { new BoneWeight { boneIndex0 = 0, weight0 = 1 }, new BoneWeight { boneIndex0 = 0, weight0 = 1 },
            new BoneWeight { boneIndex0 = 0, weight0 = 1 }, new BoneWeight { boneIndex0 = 0, weight0 = 1 } };
        var full = new Mesh { vertices = new[] { new Vector3(0, 0, 0), new Vector3(0, 1, 0), new Vector3(1, 1, 0), new Vector3(1, 0, 0) },
            triangles = new[] { 0, 1, 2, 0, 2, 3 } };
        var cameraObject = new GameObject("Face camera");
        var camera = cameraObject.AddComponent<Camera>();
        camera.enabled = false;
        camera.orthographic = true; camera.orthographicSize = .5f; camera.aspect = 1;
        camera.transform.position = new Vector3(.5f, .5f, -2);
        camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(0, 0, 0, 1);
        camera.cullingMask = 1 << 30;
        var target = new RenderTexture(2, 1, 24, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
        target.Create(); camera.targetTexture = target;
        var readback = new Texture2D(2, 1, TextureFormat.RGBAFloat, false, true);
        var scales = new[] { ("x", new Vector3(-1, 1, 1)), ("y", new Vector3(1, -1, 1)),
            ("z", new Vector3(1, 1, -1)), ("xy", new Vector3(-1, -1, 1)), ("zero", new Vector3(0, 1, 1)) };
        foreach (string path in new[] { "meshRenderer", "skinnedRenderer", "commandRenderer", "immediate", "commandMesh", "commandMultiple", "skinnedRendererGpu", "commandSkinned", "commandSkinnedGpu" })
        foreach (var (axis, scale) in scales)
        {
            var objects = new GameObject[2];
            var renderers = new Renderer[2];
            var parent = new GameObject("Face parent");
            for (int i = 0; i < 2; i++)
            {
                var go = objects[i] = new GameObject("Face quad"); go.layer = 30;
                if (path == "skinnedRenderer" || path == "skinnedRendererGpu" || path == "commandSkinned" || path == "commandSkinnedGpu")
                {
                    var skin = go.AddComponent<SkinnedMeshRenderer>(); skin.sharedMesh = quad;
                    skin.bones = new[] { go.transform }; skin.rootBone = go.transform;
                    skin.localBounds = new Bounds(new Vector3(.25f, .5f, 0), new Vector3(2, 2, 2));
                    skin.updateWhenOffscreen = true; renderers[i] = skin;
                }
                else
                {
                    go.AddComponent<MeshFilter>().sharedMesh = quad;
                    renderers[i] = go.AddComponent<MeshRenderer>();
                }
                if (i == 1)
                {
                    go.transform.SetParent(parent.transform, false);
                    parent.transform.localScale = scale;
                    parent.transform.position = new Vector3(scale.x < 0 ? 1 : .5f, scale.y < 0 ? 1 : 0, 0);
                }
            }
            if (path.EndsWith("Gpu", StringComparison.Ordinal))
            {
                foreach (var renderer in renderers) renderer.sharedMaterial = faces;
                camera.Render();
                for (int i = 0; i < renderers.Length; i++)
                {
                    var buffer = ((SkinnedMeshRenderer)renderers[i]).GetVertexBuffer();
                    Row(rows, "RendererFaces." + path + "." + axis + ".bufferAvailable." + i, buffer != null ? Color.white : Color.black);
                    buffer?.Dispose();
                }
            }
            foreach (bool invert in new[] { false, true })
            foreach (var (material, cull) in new[] { (faces, "twoSided"), (culled, "cullBack") })
            {
                foreach (var renderer in renderers) renderer.sharedMaterial = material;
                GL.invertCulling = invert;
                if (path == "immediate")
                {
                    var active = RenderTexture.active; RenderTexture.active = target;
                    GL.Clear(true, true, new Color(0, 0, 0, 1));
                    GL.PushMatrix(); GL.modelview = camera.worldToCameraMatrix; GL.LoadProjectionMatrix(camera.projectionMatrix);
                    material.SetPass(0);
                    foreach (var go in objects) Graphics.DrawMeshNow(quad, go.transform.localToWorldMatrix);
                    GL.PopMatrix(); RenderTexture.active = active;
                }
                else if (path == "commandRenderer" || path == "commandMesh" || path == "commandMultiple" || path == "commandSkinned" || path == "commandSkinnedGpu")
                {
                    using (var cmd = new UnityEngine.Rendering.CommandBuffer())
                    {
                        cmd.SetRenderTarget(target); cmd.ClearRenderTarget(true, true, new Color(0, 0, 0, 1));
                        cmd.SetViewProjectionMatrices(camera.worldToCameraMatrix, camera.projectionMatrix);
                        if (path == "commandRenderer" || path == "commandSkinned" || path == "commandSkinnedGpu") foreach (var renderer in renderers) cmd.DrawRenderer(renderer, material, 0, 0);
                        else if (path == "commandMultiple") cmd.DrawMultipleMeshes(new[] { objects[0].transform.localToWorldMatrix, objects[1].transform.localToWorldMatrix },
                            new[] { quad, quad }, new[] { 0, 0 }, 2, material, 0, null);
                        else foreach (var go in objects) cmd.DrawMesh(quad, go.transform.localToWorldMatrix, material, 0, 0);
                        Graphics.ExecuteCommandBuffer(cmd);
                    }
                }
                else camera.Render();
                GL.invertCulling = false;
                var previous = RenderTexture.active; RenderTexture.active = target;
                GL.PushMatrix(); GL.LoadOrtho();
                equal.SetInt("_Ref", 1); equal.SetColor("_Color", Color.red); equal.SetPass(0); Graphics.DrawMeshNow(full, Matrix4x4.identity);
                equal.SetInt("_Ref", 255); equal.SetColor("_Color", Color.green); equal.SetPass(0); Graphics.DrawMeshNow(full, Matrix4x4.identity);
                GL.PopMatrix();
                readback.ReadPixels(new Rect(0, 0, 2, 1), 0, 0); readback.Apply(); RenderTexture.active = previous;
                string key = "RendererFaces." + path + "." + axis + "." + cull + (invert ? ".inverted" : ".plain");
                Row(rows, key + ".normal", readback.GetPixel(0, 0));
                Row(rows, key + ".transformed", readback.GetPixel(1, 0));
            }
            foreach (var go in objects) UnityEngine.Object.DestroyImmediate(go);
            UnityEngine.Object.DestroyImmediate(parent);
        }
        camera.targetTexture = null;
        UnityEngine.Object.DestroyImmediate(cameraObject);
        target.Release(); UnityEngine.Object.DestroyImmediate(target);
        UnityEngine.Object.DestroyImmediate(readback); UnityEngine.Object.DestroyImmediate(quad); UnityEngine.Object.DestroyImmediate(full);
        UnityEngine.Object.DestroyImmediate(faces); UnityEngine.Object.DestroyImmediate(culled); UnityEngine.Object.DestroyImmediate(equal);
    }

    /// <summary>Recorded last: what a two-sided program reads as its face, through VFACE
    /// (`FrontFaceVFace`) and SV_IsFrontFace (`FrontFaceSystem`): red for a front face, green for
    /// a back face. A two-texel target gets a quad over its left texel and the same quad mirrored
    /// (a matrix of negative determinant) over its right one, drawn by DrawMeshNow
    /// (`immediate`), a command buffer's DrawMesh (`commandMesh`) and a camera's MeshRenderers
    /// (`camera`, the mirrored one under a parent scaled -1 along x), each plain and with
    /// GL.invertCulling (a command buffer's SetInvertCulling for `commandMesh`).</summary>
    static void FrontFaces(StringBuilder rows)
    {
        var quad = new Mesh { vertices = new[] { new Vector3(0, 0, 0), new Vector3(0, 1, 0), new Vector3(.5f, 1, 0), new Vector3(.5f, 0, 0) },
            triangles = new[] { 0, 1, 2, 0, 2, 3 } };
        var mirrored = Matrix4x4.TRS(new Vector3(1, 0, 0), Quaternion.identity, new Vector3(-1, 1, 1));
        var target = new RenderTexture(2, 1, 24, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear) { filterMode = FilterMode.Point };
        target.Create();
        var readback = new Texture2D(2, 1, TextureFormat.RGBAFloat, false, true);
        var cameraObject = new GameObject("Front face camera");
        var camera = cameraObject.AddComponent<Camera>();
        camera.enabled = false;
        camera.orthographic = true; camera.orthographicSize = .5f; camera.aspect = 1;
        camera.transform.position = new Vector3(.5f, .5f, -2);
        camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(0, 0, 0, 1);
        camera.cullingMask = 1 << 30; camera.targetTexture = target;
        var plain = new GameObject("Front face quad") { layer = 30 };
        plain.AddComponent<MeshFilter>().sharedMesh = quad;
        var parent = new GameObject("Front face parent");
        parent.transform.position = new Vector3(1, 0, 0);
        parent.transform.localScale = new Vector3(-1, 1, 1);
        var reflected = new GameObject("Front face reflected quad") { layer = 30 };
        reflected.transform.SetParent(parent.transform, false);
        reflected.AddComponent<MeshFilter>().sharedMesh = quad;
        var renderers = new Renderer[] { plain.AddComponent<MeshRenderer>(), reflected.AddComponent<MeshRenderer>() };
        foreach (var shader in new[] { "FrontFaceVFace", "FrontFaceSystem" })
        {
#if UNITY_EDITOR
            var material = new Material(UnityEditor.AssetDatabase.LoadAssetAtPath<Shader>("Assets/Shaders/" + shader + ".shader"));
#else
            var material = new Material(Shader.Find("Probe/SamplerStates/" + shader));
#endif
            foreach (var renderer in renderers) renderer.sharedMaterial = material;
            foreach (string path in new[] { "immediate", "commandMesh", "camera" })
            foreach (bool invert in new[] { false, true })
            {
                var previous = RenderTexture.active;
                RenderTexture.active = target;
                GL.Clear(true, true, new Color(0, 0, 0, 1));
                RenderTexture.active = previous;
                if (path == "immediate")
                {
                    RenderTexture.active = target;
                    GL.PushMatrix(); GL.LoadOrtho();
                    GL.invertCulling = invert;
                    material.SetPass(0);
                    Graphics.DrawMeshNow(quad, Matrix4x4.identity);
                    Graphics.DrawMeshNow(quad, mirrored);
                    GL.invertCulling = false;
                    GL.PopMatrix();
                    RenderTexture.active = previous;
                }
                else if (path == "commandMesh")
                {
                    using (var cmd = new UnityEngine.Rendering.CommandBuffer())
                    {
                        cmd.SetRenderTarget(target);
                        cmd.SetViewProjectionMatrices(Matrix4x4.identity, Matrix4x4.Ortho(0, 1, 0, 1, -1, 1));
                        cmd.SetInvertCulling(invert);
                        cmd.DrawMesh(quad, Matrix4x4.identity, material, 0, 0);
                        cmd.DrawMesh(quad, mirrored, material, 0, 0);
                        cmd.SetInvertCulling(false);
                        Graphics.ExecuteCommandBuffer(cmd);
                    }
                }
                else
                {
                    GL.invertCulling = invert;
                    camera.Render();
                    GL.invertCulling = false;
                }
                RenderTexture.active = target;
                readback.ReadPixels(new Rect(0, 0, 2, 1), 0, 0);
                readback.Apply();
                RenderTexture.active = previous;
                string key = "FrontFaces." + shader + "." + path + (invert ? ".inverted" : ".plain");
                Row(rows, key + ".normal", readback.GetPixel(0, 0));
                Row(rows, key + ".mirrored", readback.GetPixel(1, 0));
            }
            UnityEngine.Object.DestroyImmediate(material);
        }
        camera.targetTexture = null;
        UnityEngine.Object.DestroyImmediate(cameraObject);
        UnityEngine.Object.DestroyImmediate(plain); UnityEngine.Object.DestroyImmediate(parent);
        target.Release(); UnityEngine.Object.DestroyImmediate(target);
        UnityEngine.Object.DestroyImmediate(readback); UnityEngine.Object.DestroyImmediate(quad);
    }

    static void RawDepthSourceControls(StringBuilder rows)
    {
#if UNITY_EDITOR
        var shader = UnityEditor.AssetDatabase.LoadAssetAtPath<ComputeShader>("Assets/Shaders/DepthCompute.compute");
        var previous = RenderTexture.active;
        var first = new RenderTexture(2, 1, 24, RenderTextureFormat.Depth, RenderTextureReadWrite.Linear);
        var second = new RenderTexture(2, 1, 24, RenderTextureFormat.Depth, RenderTextureReadWrite.Linear);
        first.filterMode = second.filterMode = FilterMode.Point;
        first.Create(); second.Create();
        RenderTexture.active = first; GL.Clear(true, false, Color.black, .125f);
        RenderTexture.active = second; GL.Clear(true, false, Color.black, .875f);
        RenderTexture.active = previous;
        var output = new ComputeBuffer(3, 16);
        var unset = new[] { -Vector4.one, -Vector4.one, -Vector4.one };
        var data = new Vector4[3];
        int kernel = shader.FindKernel("CompareLoad");
        shader.SetBuffer(kernel, "_Out", output);
        void Fetch(string key, RenderTexture source)
        {
            output.SetData(unset);
            shader.SetTexture(kernel, "_Depth", source);
            shader.Dispatch(kernel, 1, 1, 1); output.GetData(data);
            // Compare history is recorded elsewhere. These lanes must read the requested image.
            Row(rows, "Compute.RawSource." + key, new Color(data[1].y, data[1].z, 0, 1));
        }
        Fetch("first", first);
        Fetch("second", second);
        Fetch("firstAgain", first);
        output.Release();
        RenderTexture.active = previous;
        UnityEngine.Object.DestroyImmediate(first); UnityEngine.Object.DestroyImmediate(second);
#endif
    }

    /// <summary>Recorded after the stencil rows: compute kernels reading each depth target (a
    /// comparison against 0.5 through the texture's own comparison sampler at both texels, a
    /// comparison and Loads of the same texture, Loads alone), one float4 each; a kernel that
    /// does not run leaves -1.</summary>
    static void Computes(StringBuilder rows, (string, RenderTexture)[] depths)
    {
#if UNITY_EDITOR
        var shader = UnityEditor.AssetDatabase.LoadAssetAtPath<ComputeShader>("Assets/Shaders/DepthCompute.compute");
#else
        ComputeShader shader = null;
#endif
        if (shader == null)
        {
            rows.Append("{\"k\":\"Compute\",\"v\":\"missing\"}\n");
            return;
        }
        var output = new ComputeBuffer(3, 16);
        var unset = new[] { -Vector4.one, -Vector4.one, -Vector4.one };
        var data = new Vector4[3];
        var kernels = new[] { "Compare", "CompareLoad", "LoadOnly" };
        foreach (var (suffix, depth) in depths)
            for (int index = 0; index < kernels.Length; index++)
            {
                output.SetData(unset);
                int kernel = shader.FindKernel(kernels[index]);
                shader.SetTexture(kernel, "_Depth", depth);
                shader.SetBuffer(kernel, "_Out", output);
                shader.Dispatch(kernel, 1, 1, 1);
                output.GetData(data);
                var v = data[index];
                Row(rows, "Compute." + kernels[index] + "." + suffix, new Color(v.x, v.y, v.z, v.w));
                if (kernels[index] == "CompareLoad")
                    Row(rows, "Compute.RawLoad." + suffix, new Color(v.y, v.z, 0, 1));
            }
        output.Release();
    }

    /// <summary>Recorded last: a texture of two texels (0.25, 0.75) of three own states (point
    /// and repeat, bilinear and clamp, point and mirror) through wrongly spelt inline sampler
    /// names (`Lenient*`), sampled at u 0.4, 1.4, -0.4 and 1.9 (red, green, blue, alpha), where
    /// the texture's state and the state the name spells give different values.</summary>
    static readonly string[] LenientCases =
    {
        "LenientPointLinear", "LenientLinearPoint", "LenientTrilinearPoint", "LenientClampRepeat",
        "LenientClampMirror", "LenientMirrorClamp", "LenientAniso", "LenientBare",
    };

    static void LenientNames(StringBuilder rows)
    {
        var target = new RenderTexture(1, 1, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
        target.Create();
        var readback = new Texture2D(1, 1, TextureFormat.RGBAFloat, false, true);
        var texture = Pair(0.25f, 0.75f, FilterMode.Point);
        var states = new (string, FilterMode, TextureWrapMode)[]
        {
            ("pointRepeat", FilterMode.Point, TextureWrapMode.Repeat),
            ("bilinearClamp", FilterMode.Bilinear, TextureWrapMode.Clamp),
            ("pointMirror", FilterMode.Point, TextureWrapMode.Mirror),
        };
        foreach (var (state, filter, wrap) in states)
        {
            texture.filterMode = filter;
            texture.wrapMode = wrap;
            foreach (var name in LenientCases)
            {
                var shader = Shader.Find("Probe/SamplerStates/" + name);
                if (shader == null)
                {
                    rows.Append("{\"k\":\"").Append(name).Append('.').Append(state).Append("\",\"v\":\"missing\"}\n");
                    continue;
                }
                var material = new Material(shader);
                material.SetTexture("_L", texture);
                Graphics.Blit(null, target, material);
                Row(rows, name + "." + state, Read(target, readback));
                DestroyImmediate(material);
            }
        }
        DestroyImmediate(texture); DestroyImmediate(readback); DestroyImmediate(target);
    }

    /// <summary>Recorded last: what a program samples from packed 16-bit color targets (4444,
    /// 5-6-5, 1-5-5-5) of two texels: (0.25, 0.5, 0.3, 0.7) drawn and cleared, point sampled;
    /// 0.2 and 0.6 drawn side by side, sampled between them through a bilinear state; 0.25 then
    /// 0.04 added by a blended draw (`Packed.&lt;format&gt;.*`). A float target's rows are the
    /// control.</summary>
    static void PackedTargets(StringBuilder rows)
    {
        var target = new RenderTexture(1, 1, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
        target.Create();
        var readback = new Texture2D(1, 1, TextureFormat.RGBAFloat, false, true);
        var fill = new Material(Shader.Find("Probe/SamplerStates/PackedFill"));
        var read = new Material(Shader.Find("Probe/SamplerStates/PackedRead"));
        foreach (var format in new[] { RenderTextureFormat.ARGB4444, RenderTextureFormat.RGB565, RenderTextureFormat.ARGB1555, RenderTextureFormat.ARGBFloat })
        {
            string key = "Packed." + format;
            rows.Append("{\"k\":\"").Append(key).Append(".supported\",\"v\":").Append(SystemInfo.SupportsRenderTextureFormat(format) ? "true" : "false").Append("}\n");
            var packed = new RenderTexture(2, 1, 0, format, RenderTextureReadWrite.Linear);
            packed.Create();
            void Sample(string name, FilterMode filter, float u)
            {
                packed.filterMode = filter;
                read.SetTexture("_P", packed);
                read.SetFloat("_U", u);
                Graphics.Blit(null, target, read);
                Row(rows, key + "." + name, Read(target, readback));
            }
            void Fill(float mode, UnityEngine.Rendering.BlendMode src = UnityEngine.Rendering.BlendMode.One, UnityEngine.Rendering.BlendMode dst = UnityEngine.Rendering.BlendMode.Zero)
            {
                fill.SetFloat("_Mode", mode);
                fill.SetFloat("_Src", (float)src);
                fill.SetFloat("_Dst", (float)dst);
                Graphics.Blit(null, packed, fill);
            }
            Fill(0);
            Sample("draw", FilterMode.Point, 0.25f);
            var previous = RenderTexture.active;
            RenderTexture.active = packed;
            GL.Clear(false, true, new Color(0.25f, 0.5f, 0.3f, 0.7f));
            RenderTexture.active = previous;
            Sample("clear", FilterMode.Point, 0.25f);
            Fill(1);
            Sample("halves.left", FilterMode.Point, 0.25f);
            Sample("halves.between", FilterMode.Bilinear, 0.5f);
            Fill(0);
            Fill(2, UnityEngine.Rendering.BlendMode.One, UnityEngine.Rendering.BlendMode.One);
            Sample("blend", FilterMode.Point, 0.25f);
            DestroyImmediate(packed);
            PackedLevelsAndColors(rows, key, format, fill, target, readback);
        }
        DestroyImmediate(fill); DestroyImmediate(read); DestroyImmediate(readback); DestroyImmediate(target);
    }

    /// <summary>Recorded last: a depth-only target (0.25, 0.75 drawn) that no draw has sampled,
    /// read first by a kernel as four floats per texel (`DepthReads.kernel.first.t0/t1`) and
    /// through an integer slot (`DepthReads.uint.first.high/.low`: the 16-bit halves of its four
    /// words), then sampled by a draw as floats
    /// (`DepthReads.float`), then read by both again (`.after`).</summary>
    static void DepthTargetReads(StringBuilder rows)
    {
#if UNITY_EDITOR
        var shader = UnityEditor.AssetDatabase.LoadAssetAtPath<ComputeShader>("Assets/Shaders/DepthReadsCompute.compute");
#else
        ComputeShader shader = null;
#endif
        if (shader == null)
        {
            rows.Append("{\"k\":\"DepthReads\",\"v\":\"missing\"}\n");
            return;
        }
        var depth = new RenderTexture(2, 1, 24, RenderTextureFormat.Depth, RenderTextureReadWrite.Linear);
        depth.filterMode = FilterMode.Point;
        depth.Create();
        var only = new Material(Shader.Find("Probe/SamplerStates/DepthOnly"));
        Graphics.Blit(Texture2D.whiteTexture, depth, only);
        var target = new RenderTexture(1, 1, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear); target.Create();
        var readback = new Texture2D(1, 1, TextureFormat.RGBAFloat, false, true);
        var output = new ComputeBuffer(2, 16);
        var data = new Vector4[2];
        int kernel = shader.FindKernel("Channels");
        void Kernel(string key)
        {
            output.SetData(new[] { -Vector4.one, -Vector4.one });
            shader.SetTexture(kernel, "_Depth", depth);
            shader.SetBuffer(kernel, "_Out", output);
            shader.Dispatch(kernel, 1, 1, 1);
            output.GetData(data);
            for (int t = 0; t < 2; t++) Row(rows, key + ".t" + t, new Color(data[t].x, data[t].y, data[t].z, data[t].w));
        }
        void Draw(string key, string program, float high = 1)
        {
            var material = new Material(Shader.Find(program));
            material.SetTexture("_Shadow", depth);
            material.SetFloat("_High", high);
            Graphics.Blit(Texture2D.whiteTexture, target, material);
            Row(rows, key, Read(target, readback));
            DestroyImmediate(material);
        }
        Kernel("DepthReads.kernel.first");
        Draw("DepthReads.uint.first.high", "Probe/SamplerStates/DepthUint");
        Draw("DepthReads.uint.first.low", "Probe/SamplerStates/DepthUint", 0);
        Draw("DepthReads.float", "Probe/SamplerStates/DepthChannels");
        Kernel("DepthReads.kernel.after");
        Draw("DepthReads.uint.after.high", "Probe/SamplerStates/DepthUint");
        Draw("DepthReads.uint.after.low", "Probe/SamplerStates/DepthUint", 0);
        output.Release();
        DestroyImmediate(only); DestroyImmediate(readback);
        target.Release(); DestroyImmediate(target);
        depth.Release(); DestroyImmediate(depth);
    }

    /// <summary>Recorded after the other packed rows: a packed target's mip level 1 (of a 2x2
    /// target with mips) cleared to (0.25, 0.5, 0.3, 0.7), drawn with that color, and made by
    /// GenerateMips from level 0 drawn so (`.mip.*`); and the packed target as the second color
    /// of a two-target binding (a float first color, a separate depth target) cleared with a
    /// command buffer to that color (`.mrt.clear`), point sampled.</summary>
    static void PackedLevelsAndColors(StringBuilder rows, string key, RenderTextureFormat format, Material fill,
        RenderTexture target, Texture2D readback)
    {
        var read = new Material(Shader.Find("Probe/SamplerStates/PackedReadLod"));
        var color = new Color(0.25f, 0.5f, 0.3f, 0.7f);
        var mipped = new RenderTexture(new RenderTextureDescriptor(2, 2, format, 0)
            { useMipMap = true, autoGenerateMips = false, sRGB = false });
        mipped.filterMode = FilterMode.Point;
        mipped.Create();
        void Sample(RenderTexture source, string name, int lod)
        {
            read.SetTexture("_P", source);
            read.SetFloat("_U", 0.25f);
            read.SetFloat("_Lod", lod);
            Graphics.Blit(null, target, read);
            Row(rows, key + "." + name, Read(target, readback));
        }
        void DrawLevel(int level)
        {
            fill.SetFloat("_Mode", 0f);
            fill.SetFloat("_Src", (float)UnityEngine.Rendering.BlendMode.One);
            fill.SetFloat("_Dst", (float)UnityEngine.Rendering.BlendMode.Zero);
            var previous = RenderTexture.active;
            Graphics.SetRenderTarget(mipped, level);
            GL.PushMatrix();
            GL.LoadOrtho();
            fill.SetPass(0);
            GL.Begin(GL.QUADS);
            GL.TexCoord2(0, 0); GL.Vertex3(0, 0, 0);
            GL.TexCoord2(0, 1); GL.Vertex3(0, 1, 0);
            GL.TexCoord2(1, 1); GL.Vertex3(1, 1, 0);
            GL.TexCoord2(1, 0); GL.Vertex3(1, 0, 0);
            GL.End();
            GL.PopMatrix();
            RenderTexture.active = previous;
        }
        using (var clear = new UnityEngine.Rendering.CommandBuffer())
        {
            clear.SetRenderTarget(mipped, 1);
            clear.ClearRenderTarget(false, true, color);
            Graphics.ExecuteCommandBuffer(clear);
        }
        Sample(mipped, "mip.clear", 1);
        DrawLevel(1);
        Sample(mipped, "mip.draw", 1);
        DrawLevel(0);
        mipped.GenerateMips();
        Sample(mipped, "mip.generated", 1);
        DestroyImmediate(mipped);

        var first = new RenderTexture(2, 1, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
        var second = new RenderTexture(2, 1, 0, format, RenderTextureReadWrite.Linear) { filterMode = FilterMode.Point };
        var depth = new RenderTexture(2, 1, 24, RenderTextureFormat.Depth);
        first.Create(); second.Create(); depth.Create();
        using (var clear = new UnityEngine.Rendering.CommandBuffer())
        {
            clear.SetRenderTarget(new UnityEngine.Rendering.RenderTargetIdentifier[] { first, second }, depth);
            clear.ClearRenderTarget(false, true, color);
            Graphics.ExecuteCommandBuffer(clear);
        }
        Sample(second, "mrt.clear", 0);
        Sample(first, "mrt.clear.first", 0);
        DestroyImmediate(first); DestroyImmediate(second); DestroyImmediate(depth);
        DestroyImmediate(read);
    }

    static Color Read(RenderTexture texture, Texture2D readback)
    {
        var previous = RenderTexture.active;
        RenderTexture.active = texture;
        readback.ReadPixels(new Rect(0, 0, 1, 1), 0, 0);
        readback.Apply();
        RenderTexture.active = previous;
        return readback.GetPixel(0, 0);
    }

    static void Row(StringBuilder rows, string key, Color v)
    {
        rows.Append("{\"k\":\"").Append(key).Append("\",\"v\":[")
            .Append(F(v.r)).Append(',').Append(F(v.g)).Append(',').Append(F(v.b)).Append(',').Append(F(v.a))
            .Append("]}\n");
    }

    static string F(float value) { return value.ToString("R", CultureInfo.InvariantCulture); }
    static void D16Controls(StringBuilder rows)
    {
        var source = Pair(.25f, .75f, FilterMode.Point);
        var d16 = Split(RenderTextureFormat.Depth, FilterMode.Bilinear, source);
        var normalized = new Texture2D(2, 1, TextureFormat.R16, false, true);
        normalized.SetPixelData(new ushort[] { 16384, 49151 }, 0); normalized.Apply(false);
        var floats = Pair(16384f / 65535f, 49151f / 65535f, FilterMode.Bilinear);
        var unquantized = Pair(.25f, .75f, FilterMode.Bilinear);
        normalized.filterMode = FilterMode.Bilinear;
        normalized.wrapMode = TextureWrapMode.Clamp;
        d16.wrapMode = TextureWrapMode.Clamp;
        var target = new RenderTexture(1, 1, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear); target.Create();
        var cpu = new Texture2D(1, 1, TextureFormat.RGBAFloat, false, true);
        var material = new Material(Shader.Find("Probe/SamplerStates/D16Filter"));
        var textures = new (string, Texture)[] { ("depth16", d16), ("unorm16", normalized), ("floatQuantized", floats), ("floatExact", unquantized) };
        foreach (var (name, texture) in textures)
        foreach (float u in new[] { .25f, .3f, .4f, .45f, .5f, .55f, .6f, .7f, .75f })
        {
            material.SetTexture("_Source", texture); material.SetVector("_P", new Vector4(u, .5f, 0, 0));
            Graphics.Blit(source, target, material);
            Row(rows, "D16Filter." + name + "." + F(u), Read(target, cpu));
        }
        DestroyImmediate(material); DestroyImmediate(cpu); DestroyImmediate(target);
        foreach (var (_, texture) in textures) DestroyImmediate(texture);
        DestroyImmediate(source);
    }
    static void DepthStateControls(StringBuilder rows)
    {
        var previous = RenderTexture.active;
        var source = Pair(.25f, .75f, FilterMode.Point);
        var target = new RenderTexture(1, 1, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear); target.Create();
        var cpu = new Texture2D(1, 1, TextureFormat.RGBAFloat, false, true);
        var fill = new Material(Shader.Find("Probe/SamplerStates/Fill"));
        var material = new Material(Shader.Find("Probe/SamplerStates/DepthState"));
        var other = Split(RenderTextureFormat.Shadowmap, FilterMode.Point, source);
        material.SetTexture("_OtherDepth", other);
        foreach (var format in new[] {UnityEngine.Experimental.Rendering.GraphicsFormat.D16_UNorm,
            UnityEngine.Experimental.Rendering.GraphicsFormat.D24_UNorm_S8_UInt,
            UnityEngine.Experimental.Rendering.GraphicsFormat.D32_SFloat})
        foreach (var filter in new[] {FilterMode.Point, FilterMode.Bilinear})
        {
            var textures = new RenderTexture[3];
            var modes = new[] {UnityEngine.Rendering.ShadowSamplingMode.None, UnityEngine.Rendering.ShadowSamplingMode.CompareDepths, UnityEngine.Rendering.ShadowSamplingMode.RawDepth};
            for (int i=0;i<3;i++)
            {
                var descriptor = new RenderTextureDescriptor(2,1) {graphicsFormat=UnityEngine.Experimental.Rendering.GraphicsFormat.None,depthStencilFormat=format,shadowSamplingMode=modes[i],msaaSamples=1};
                textures[i]=new RenderTexture(descriptor) {filterMode=filter,wrapMode=TextureWrapMode.Clamp};textures[i].Create();
                Graphics.Blit(source,textures[i],fill);
            }
            int step=0;
            foreach(int index in new[]{0,1,0,2})
            {
                material.SetTexture("_Depth",textures[index]);
                Graphics.Blit(null,target,material);
                Row(rows,"DepthState."+format+"."+filter+"."+(step++)+"."+modes[index],Read(target,cpu));
            }
            foreach(var texture in textures) DestroyImmediate(texture);
        }
        RenderTexture.active = previous;
        DestroyImmediate(other);DestroyImmediate(material);DestroyImmediate(fill);DestroyImmediate(cpu);DestroyImmediate(target);DestroyImmediate(source);
    }
    static void PoolDepthControls(StringBuilder rows)
    {
        var previous = RenderTexture.active;
        var source = Pair(.25f,.75f,FilterMode.Point);
        var target = new RenderTexture(1,1,0,RenderTextureFormat.ARGBFloat,RenderTextureReadWrite.Linear);target.Create();
        var cpu = new Texture2D(1,1,TextureFormat.RGBAFloat,false,true);
        var fill = new Material(Shader.Find("Probe/SamplerStates/Fill"));
        var sample = new Material(Shader.Find("Probe/SamplerStates/DepthState"));
        var other = Split(RenderTextureFormat.Shadowmap,FilterMode.Point,source);sample.SetTexture("_OtherDepth",other);
        var descriptor = new RenderTextureDescriptor(2,1) {graphicsFormat=UnityEngine.Experimental.Rendering.GraphicsFormat.None,
            depthStencilFormat=UnityEngine.Experimental.Rendering.GraphicsFormat.D24_UNorm_S8_UInt};
        int step=0;
        foreach(var mode in new[]{UnityEngine.Rendering.ShadowSamplingMode.None,UnityEngine.Rendering.ShadowSamplingMode.CompareDepths,
            UnityEngine.Rendering.ShadowSamplingMode.None,UnityEngine.Rendering.ShadowSamplingMode.RawDepth})
        {
            descriptor.shadowSamplingMode=mode;
            var texture=RenderTexture.GetTemporary(descriptor);texture.filterMode=FilterMode.Bilinear;texture.wrapMode=TextureWrapMode.Clamp;
            Graphics.Blit(source,texture,fill);sample.SetTexture("_Depth",texture);Graphics.Blit(null,target,sample);
            Row(rows,"DepthState.pool."+(step++)+"."+mode,Read(target,cpu));
            RenderTexture.ReleaseTemporary(texture);
        }
        descriptor=new RenderTextureDescriptor(2,1,RenderTextureFormat.ARGB32,0) {msaaSamples=4};step=0;
        foreach(bool bound in new[]{false,true,false,true})
        {
            descriptor.bindMS=bound;
            var texture=RenderTexture.GetTemporary(descriptor);
            Row(rows,"DepthState.pool.bind."+(step++),new Color(texture.descriptor.bindMS?1:0,texture.antiAliasing,0,1));
            RenderTexture.ReleaseTemporary(texture);
        }
        RenderTexture.active=previous;
        DestroyImmediate(sample);DestroyImmediate(fill);DestroyImmediate(other);DestroyImmediate(cpu);DestroyImmediate(target);DestroyImmediate(source);
    }
}
