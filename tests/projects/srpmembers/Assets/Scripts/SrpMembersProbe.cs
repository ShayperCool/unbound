using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

/// <summary>
/// Render API members the render pipelines use: CommandBuffer.ThrowOnSetRenderTarget, GraphicsBuffer
/// usage flags and LockBufferForWrite, MaterialPropertyBlock.SetTexture with a render texture
/// sub-element, ShaderKeyword names and ShaderKeywordSet, Graphics.activeTier and
/// minOpenGLESVersion, the tiled storage caps and DrawMeshInstanced's argument checks and draws.
/// Each record is a value, "ok" or the exception's type and message.
/// </summary>
public class SrpMembersProbe : MonoBehaviour
{
    public static bool Finished;
    const string OutVariable = "SRP_MEMBERS_OUT";
    static readonly StringBuilder s_Output = new StringBuilder();
    static readonly HashSet<string> s_Keys = new HashSet<string>();

    static string Quote(string s)
    {
        var b = new StringBuilder("\"");
        foreach (char c in s)
        {
            if (c == '"' || c == '\\') b.Append('\\').Append(c);
            else if (c < 32) b.Append("\\u").Append(((int)c).ToString("x4"));
            else b.Append(c);
        }
        return b.Append('"').ToString();
    }

    static void Record(string key, string value)
    {
        if (!s_Keys.Add(key)) value = "duplicate key";
        s_Output.Append("{\"k\":").Append(Quote(key)).Append(",\"v\":").Append(Quote(value)).Append("}\n");
    }

    static string Describe(Exception e) =>
        "throws " + e.GetType().Name + ": " + e.Message + (e.InnerException != null ? " (inner " + e.InnerException.GetType().Name + ")" : "");

    static void Try(string key, Func<string> action)
    {
        try { Record(key, action()); }
        catch (Exception e) { Record(key, Describe(e)); }
    }

    static void Try(string key, Action action) => Try(key, () => { action(); return "ok"; });

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Boot()
    {
        if (Environment.GetEnvironmentVariable(OutVariable) == null) return;
        Application.runInBackground = true;
        DontDestroyOnLoad(new GameObject("SrpMembersProbe").AddComponent<SrpMembersProbe>().gameObject);
    }

    void Start()
    {
        try
        {
            SetRenderTargetGuard();
            BufferUsage();
            BufferLocks();
            SubElements();
            Keywords();
            TiersAndCaps();
            Instanced();
        }
        catch (Exception e) { Record("error", e.ToString()); }
        s_Output.Append("{\"k\":\"complete\",\"v\":true}\n");
        File.WriteAllText(Environment.GetEnvironmentVariable(OutVariable), s_Output.ToString());
        Finished = true;
    }

    // ------------------------------------------------------------------ ThrowOnSetRenderTarget

    static void SetRenderTargetGuard()
    {
        Record("guard.default", CommandBuffer.ThrowOnSetRenderTarget.ToString());
        var rt = new RenderTexture(4, 4, 24, GraphicsFormat.R8G8B8A8_UNorm);
        rt.Create();
        RenderTargetIdentifier id = rt;
        var colors = new RenderTargetIdentifier[] { rt };
        var binding = new RenderTargetBinding(rt, RenderBufferLoadAction.Load, RenderBufferStoreAction.Store, rt, RenderBufferLoadAction.Load, RenderBufferStoreAction.Store);
        var calls = new (string Name, Action<CommandBuffer> Call)[]
        {
            ("rt", cb => cb.SetRenderTarget(id)),
            ("rtLoadStore", cb => cb.SetRenderTarget(id, RenderBufferLoadAction.Load, RenderBufferStoreAction.Store)),
            ("rtColorDepthLoadStore", cb => cb.SetRenderTarget(id, RenderBufferLoadAction.Load, RenderBufferStoreAction.Store, RenderBufferLoadAction.Load, RenderBufferStoreAction.Store)),
            ("rtMip", cb => cb.SetRenderTarget(id, 0)),
            ("rtMipFace", cb => cb.SetRenderTarget(id, 0, CubemapFace.Unknown)),
            ("rtMipFaceSlice", cb => cb.SetRenderTarget(id, 0, CubemapFace.Unknown, 0)),
            ("colorDepth", cb => cb.SetRenderTarget(id, id)),
            ("colorDepthMip", cb => cb.SetRenderTarget(id, id, 0)),
            ("colorDepthMipFace", cb => cb.SetRenderTarget(id, id, 0, CubemapFace.Unknown)),
            ("colorDepthMipFaceSlice", cb => cb.SetRenderTarget(id, id, 0, CubemapFace.Unknown, 0)),
            ("colorDepthLoadStore", cb => cb.SetRenderTarget(id, RenderBufferLoadAction.Load, RenderBufferStoreAction.Store, id, RenderBufferLoadAction.Load, RenderBufferStoreAction.Store)),
            ("colorsDepth", cb => cb.SetRenderTarget(colors, id)),
            ("colorsDepthMipFaceSlice", cb => cb.SetRenderTarget(colors, id, 0, CubemapFace.Unknown, 0)),
            ("binding", cb => cb.SetRenderTarget(binding)),
            ("bindingMipFaceSlice", cb => cb.SetRenderTarget(binding, 0, CubemapFace.Unknown, 0)),
            ("blit", cb => cb.Blit(rt, id)),
            ("clear", cb => cb.ClearRenderTarget(true, true, Color.black)),
        };
        try
        {
            CommandBuffer.ThrowOnSetRenderTarget = true;
            foreach (var (name, call) in calls)
            {
                var cb = new CommandBuffer();
                Try("guard.on." + name, () => call(cb));
                cb.Release();
            }
            Try("guard.on.graphicsSetRenderTarget", () => Graphics.SetRenderTarget(rt));
            Graphics.SetRenderTarget(null);
        }
        finally
        {
            CommandBuffer.ThrowOnSetRenderTarget = false;
        }
        var after = new CommandBuffer();
        Try("guard.off.rt", () => after.SetRenderTarget(id));
        after.Release();
        rt.Release();
    }

    // ------------------------------------------------------------------ GraphicsBuffer

    static void BufferUsage()
    {
        var targets = new (string Name, GraphicsBuffer.Target Target)[]
        {
            ("vertex", GraphicsBuffer.Target.Vertex),
            ("index", GraphicsBuffer.Target.Index),
            ("vertexIndex", GraphicsBuffer.Target.Vertex | GraphicsBuffer.Target.Index),
            ("structured", GraphicsBuffer.Target.Structured),
            ("raw", GraphicsBuffer.Target.Raw),
            ("vertexStructured", GraphicsBuffer.Target.Vertex | GraphicsBuffer.Target.Structured),
            ("vertexCopyDestination", GraphicsBuffer.Target.Vertex | GraphicsBuffer.Target.CopyDestination),
        };
        foreach (var (name, target) in targets)
            Try("usage." + name, () =>
            {
                var b = new GraphicsBuffer(target, 4, 4);
                var flags = b.usageFlags.ToString();
                b.Release();
                return flags;
            });
        var shapes = new (string Name, GraphicsBuffer.Target Target, int Count, int Stride)[]
        {
            ("zeroCount", GraphicsBuffer.Target.Structured, 0, 4),
            ("negativeCount", GraphicsBuffer.Target.Structured, -1, 4),
            ("zeroStride", GraphicsBuffer.Target.Structured, 4, 0),
            ("indexStride3", GraphicsBuffer.Target.Index, 4, 3),
            ("indexStride8", GraphicsBuffer.Target.Index, 4, 8),
            ("structuredStride6", GraphicsBuffer.Target.Structured, 4, 6),
            ("vertexStride6", GraphicsBuffer.Target.Vertex, 4, 6),
            ("vertexCopySourceStride6", GraphicsBuffer.Target.Vertex | GraphicsBuffer.Target.CopySource, 4, 6),
            ("rawStride6", GraphicsBuffer.Target.Raw, 4, 6),
        };
        foreach (var (name, target, count, stride) in shapes)
            Try("create." + name, () => new GraphicsBuffer(target, count, stride).Release());
        Try("usage.lockCopyDestination", () => new GraphicsBuffer(GraphicsBuffer.Target.Structured | GraphicsBuffer.Target.CopyDestination,
            GraphicsBuffer.UsageFlags.LockBufferForWrite, 4, 4).Release());
    }

    static string Data(GraphicsBuffer buffer)
    {
        var data = new int[buffer.count];
        buffer.GetData(data);
        return string.Join(",", data);
    }

    static GraphicsBuffer Lockable(int count = 8)
    {
        var b = new GraphicsBuffer(GraphicsBuffer.Target.Structured, GraphicsBuffer.UsageFlags.LockBufferForWrite, count, 4);
        var initial = new int[count];
        for (int i = 0; i < count; i++) initial[i] = 100 + i;
        b.SetData(initial);
        return b;
    }

    static void BufferLocks()
    {
        var plain = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 8, 4);
        Try("lock.withoutUsage", () => plain.LockBufferForWrite<int>(0, 1));
        plain.Release();
        Try("lock.released", () =>
        {
            var b = Lockable();
            b.Release();
            b.LockBufferForWrite<int>(0, 1);
        });

        var buffer = Lockable();
        Record("lock.initial", Data(buffer));
        Try("lock.negativeStart", () => buffer.LockBufferForWrite<int>(-1, 1));
        Try("lock.negativeCount", () => buffer.LockBufferForWrite<int>(0, -1));
        Try("lock.pastEnd", () => buffer.LockBufferForWrite<int>(6, 3));
        Try("lock.largerElement", () => buffer.LockBufferForWrite<long>(0, 5));
        Try("lock.unlockWithoutLock", () => buffer.UnlockBufferAfterWrite<int>(1));

        // Writes three elements at 2 and unlocks all three.
        Try("lock.write", () =>
        {
            var array = buffer.LockBufferForWrite<int>(2, 3);
            string length = array.Length.ToString();
            array[0] = 7; array[1] = 8; array[2] = 9;
            buffer.UnlockBufferAfterWrite<int>(3);
            return length + " " + Data(buffer);
        });
        // Writes three elements and unlocks one: what the buffer keeps of the rest.
        Try("lock.writePartial", () =>
        {
            var array = buffer.LockBufferForWrite<int>(0, 3);
            array[0] = 1; array[1] = 2; array[2] = 3;
            buffer.UnlockBufferAfterWrite<int>(1);
            return Data(buffer);
        });
        Try("lock.arrayAfterUnlock", () =>
        {
            var array = buffer.LockBufferForWrite<int>(0, 2);
            array[0] = 50;
            buffer.UnlockBufferAfterWrite<int>(2);
            array[1] = 51;
            return "written";
        });
        Try("lock.negativeCountWritten", () =>
        {
            buffer.LockBufferForWrite<int>(0, 1);
            buffer.UnlockBufferAfterWrite<int>(-1);
        });
        // The lock is still open: what GetData leaves in an array already holding values.
        Try("lock.afterNegativeCountWritten", () =>
        {
            var data = new int[buffer.count];
            for (int i = 0; i < data.Length; i++) data[i] = -1;
            buffer.GetData(data);
            return string.Join(",", data);
        });
        Try("lock.zero", () =>
        {
            var array = buffer.LockBufferForWrite<int>(8, 0);
            buffer.UnlockBufferAfterWrite<int>(0);
            return array.Length + " " + Data(buffer);
        });
        Try("lock.getDataWhileLocked", () =>
        {
            var array = buffer.LockBufferForWrite<int>(0, 1);
            array[0] = 60;
            var data = new int[buffer.count];
            for (int i = 0; i < data.Length; i++) data[i] = -2;
            buffer.GetData(data);
            buffer.UnlockBufferAfterWrite<int>(1);
            return string.Join(",", data) + " then " + Data(buffer);
        });
        Try("lock.setDataWhileUnlocked", () =>
        {
            buffer.SetData(new[] { 5, 6 }, 0, 6, 2);
            return Data(buffer);
        });
        buffer.Release();
    }

    // ------------------------------------------------------------------ property block sub-elements

    static Mesh s_Triangle;

    static Mesh Triangle()
    {
        if (s_Triangle != null) return s_Triangle;
        s_Triangle = new Mesh { vertices = new[] { new Vector3(-1, -1, 0), new Vector3(3, -1, 0), new Vector3(-1, 3, 0) }, triangles = new[] { 0, 1, 2 } };
        s_Triangle.bounds = new Bounds(Vector3.zero, Vector3.one * 100);
        return s_Triangle;
    }

    static string Sample(Material material, MaterialPropertyBlock block)
    {
        var output = new RenderTexture(4, 4, 0, GraphicsFormat.R32G32B32A32_SFloat);
        output.Create();
        var cb = new CommandBuffer();
        cb.SetRenderTarget(output);
        cb.ClearRenderTarget(false, true, new Color(0.9f, 0.9f, 0.9f, 0.9f));
        cb.DrawMesh(Triangle(), Matrix4x4.identity, material, 0, 0, block);
        Graphics.ExecuteCommandBuffer(cb);
        cb.Release();
        var previous = RenderTexture.active;
        RenderTexture.active = output;
        var read = new Texture2D(4, 4, TextureFormat.RGBAFloat, false);
        read.ReadPixels(new Rect(0, 0, 4, 4), 0, 0);
        RenderTexture.active = previous;
        var c = read.GetPixel(2, 2);
        UnityEngine.Object.Destroy(read);
        output.Release();
        return c.r.ToString("F3") + "," + c.g.ToString("F3") + "," + c.b.ToString("F3") + "," + c.a.ToString("F3");
    }

    static RenderTexture Source(bool color)
    {
        var rt = new RenderTexture(new RenderTextureDescriptor(8, 8, color ? GraphicsFormat.R8G8B8A8_UNorm : GraphicsFormat.None, GraphicsFormat.D24_UNorm_S8_UInt));
        rt.Create();
        var cb = new CommandBuffer();
        cb.SetRenderTarget(rt);
        cb.ClearRenderTarget(true, true, new Color(0.25f, 0.5f, 0.75f, 1f), 0.3f);
        Graphics.ExecuteCommandBuffer(cb);
        cb.Release();
        return rt;
    }

    static void SubElements()
    {
        var material = new Material(Resources.Load<Shader>("SrpMembersSample"));
        int src = Shader.PropertyToID("_Src");
        foreach (bool color in new[] { true, false })
        {
            string prefix = "element." + (color ? "colorDepth" : "depthOnly") + ".";
            var rt = Source(color);
            foreach (var element in new[] { RenderTextureSubElement.Default, RenderTextureSubElement.Color, RenderTextureSubElement.Depth })
            {
                Try(prefix + "block." + element, () =>
                {
                    var block = new MaterialPropertyBlock();
                    block.SetTexture(src, rt, element);
                    var got = block.GetTexture(src);
                    return Sample(material, block) + " get:" + (got == rt ? "rt" : got == null ? "null" : got.name);
                });
                Try(prefix + "material." + element, () =>
                {
                    var m = new Material(material);
                    m.SetTexture(src, rt, element);
                    return Sample(m, null);
                });
            }
            Try(prefix + "blockPlain", () =>
            {
                var block = new MaterialPropertyBlock();
                block.SetTexture(src, rt);
                return Sample(material, block);
            });
            // A later plain texture replaces the element binding.
            Try(prefix + "blockReplaced", () =>
            {
                var block = new MaterialPropertyBlock();
                block.SetTexture(src, rt, RenderTextureSubElement.Depth);
                block.SetTexture(src, (Texture)rt);
                return Sample(material, block);
            });
            rt.Release();
        }
        Try("element.blockNull", () => new MaterialPropertyBlock().SetTexture(src, (RenderTexture)null, RenderTextureSubElement.Depth));
        Try("element.blockByName", () =>
        {
            var rt = Source(true);
            var block = new MaterialPropertyBlock();
            block.SetTexture("_Src", rt, RenderTextureSubElement.Depth);
            var result = Sample(material, block);
            rt.Release();
            return result;
        });
    }

    // ------------------------------------------------------------------ keywords

    static void Keywords()
    {
        Try("keyword.global", () => new ShaderKeyword("SRPM_GLOBAL").name);
        Try("keyword.default", () => default(ShaderKeyword).name ?? "null");
        Try("keyword.empty", () => new ShaderKeyword("").name);
        Try("keyword.shaderLocal", () =>
        {
            var k = new ShaderKeyword(Resources.Load<Shader>("SrpMembersInstanced"), "INSTANCING_ON");
            return k.name + " " + k.IsValid();
        });
        Try("keyword.valid", () => new ShaderKeyword("SRPM_GLOBAL").IsValid() + " " + new ShaderKeyword("FOG_LINEAR").IsValid() + " " + default(ShaderKeyword).IsValid());
        Try("keyword.setBuiltin", () =>
        {
            var set = new ShaderKeywordSet();
            var k = new ShaderKeyword("FOG_LINEAR");
            string before = set.IsEnabled(k).ToString();
            set.Enable(k);
            string after = set.IsEnabled(k).ToString();
            return before + " " + after + " " + set.GetShaderKeywords().Length;
        });
        Try("keyword.set", () =>
        {
            var set = new ShaderKeywordSet();
            var k = new ShaderKeyword("SRPM_SET");
            string before = set.IsEnabled(k).ToString();
            set.Enable(k);
            string after = set.IsEnabled(k).ToString();
            set.Disable(k);
            return before + " " + after + " " + set.IsEnabled(k);
        });
    }

    // ------------------------------------------------------------------ tiers and caps

    static void TiersAndCaps()
    {
        Record("tier.active", Graphics.activeTier.ToString());
        Try("tier.set", () =>
        {
            var previous = Graphics.activeTier;
            Graphics.activeTier = GraphicsTier.Tier1;
            string set = Graphics.activeTier.ToString();
            Graphics.activeTier = previous;
            return set + " " + Graphics.activeTier;
        });
        Try("tier.outOfRange", () =>
        {
            var previous = Graphics.activeTier;
            Graphics.activeTier = (GraphicsTier)7;
            string set = ((int)Graphics.activeTier).ToString();
            Graphics.activeTier = previous;
            return set;
        });
        Record("gles.min", Graphics.minOpenGLESVersion.ToString());
        Record("tiled.max", SystemInfo.maxTiledPixelStorageSize.ToString());
        foreach (var (format, samples) in new[] { (GraphicsFormat.R8G8B8A8_UNorm, 1), (GraphicsFormat.R16G16B16A16_SFloat, 4), (GraphicsFormat.D24_UNorm_S8_UInt, 1), (GraphicsFormat.None, 1) })
            Try("tiled." + format + "." + samples, () => SystemInfo.GetTiledRenderTargetStorageSize(format, samples).ToString());
    }

    // ------------------------------------------------------------------ DrawMeshInstanced

    static int RedPixels(Camera camera, RenderTexture target)
    {
        camera.Render();
        var previous = RenderTexture.active;
        RenderTexture.active = target;
        var read = new Texture2D(target.width, target.height, TextureFormat.RGBA32, false);
        read.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
        RenderTexture.active = previous;
        int red = 0;
        foreach (var p in read.GetPixels32()) if (p.r > 200 && p.g < 50) red++;
        UnityEngine.Object.Destroy(read);
        return red;
    }

    static void Instanced()
    {
        var material = new Material(Resources.Load<Shader>("SrpMembersInstanced")) { enableInstancing = true };
        var cube = Resources.GetBuiltinResource<Mesh>("Cube.fbx");
        var target = new RenderTexture(32, 32, 24, GraphicsFormat.R8G8B8A8_UNorm);
        target.Create();
        var camera = new GameObject("SrpMembersCamera").AddComponent<Camera>();
        camera.enabled = false;
        camera.orthographic = true;
        camera.orthographicSize = 4;
        camera.transform.position = new Vector3(0, 0, -10);
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = Color.black;
        camera.targetTexture = target;
        var matrices = new[] { Matrix4x4.Translate(new Vector3(-2, 0, 0)), Matrix4x4.Translate(Vector3.zero), Matrix4x4.Translate(new Vector3(2, 0, 0)) };

        Try("instanced.eleven", () =>
        {
            Graphics.DrawMeshInstanced(cube, 0, material, matrices, 3, null, ShadowCastingMode.Off, false, 0, camera, LightProbeUsage.Off);
            return RedPixels(camera, target).ToString();
        });
        Try("instanced.twelve", () =>
        {
            Graphics.DrawMeshInstanced(cube, 0, material, matrices, 3, null, ShadowCastingMode.Off, false, 0, camera, LightProbeUsage.Off, null);
            return RedPixels(camera, target).ToString();
        });
        Try("instanced.twelveTwo", () =>
        {
            Graphics.DrawMeshInstanced(cube, 0, material, matrices, 2, null, ShadowCastingMode.Off, false, 0, camera, LightProbeUsage.Off, null);
            return RedPixels(camera, target).ToString();
        });
        Try("instanced.countPastMatrices", () => Graphics.DrawMeshInstanced(cube, 0, material, matrices, 5, null, ShadowCastingMode.Off, false, 0, camera, LightProbeUsage.Off));
        Try("instanced.negativeCount", () => Graphics.DrawMeshInstanced(cube, 0, material, matrices, -1));
        Try("instanced.proxyNull", () => Graphics.DrawMeshInstanced(cube, 0, material, matrices, 3, null, ShadowCastingMode.Off, false, 0, camera, LightProbeUsage.UseProxyVolume, null));
        Try("instanced.listProxyNull", () => Graphics.DrawMeshInstanced(cube, 0, material, new List<Matrix4x4>(matrices), null, ShadowCastingMode.Off, false, 0, camera, LightProbeUsage.UseProxyVolume, null));
        Try("instanced.nullMatrices", () => Graphics.DrawMeshInstanced(cube, 0, material, (Matrix4x4[])null, 0));
        Try("instanced.zeroCount", () =>
        {
            Graphics.DrawMeshInstanced(cube, 0, material, matrices, 0, null, ShadowCastingMode.Off, false, 0, camera, LightProbeUsage.Off, null);
            return RedPixels(camera, target).ToString();
        });
        // Throws after the checks would draw nothing: nothing is queued for the next render.
        Try("instanced.afterErrors", () => RedPixels(camera, target).ToString());
        Destroy(camera.gameObject);
        target.Release();
    }
}
