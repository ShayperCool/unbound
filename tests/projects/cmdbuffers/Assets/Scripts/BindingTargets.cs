using System;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;

public partial class CmdBuffersProbe
{
    /// <summary>
    /// CommandBuffer.SetRenderTarget(RenderTargetBinding): what a draw of four outputs writes into
    /// each of four targets for one to four bound colors, the depth target (a depth texture, the
    /// first color's own depth buffer, none), load and store actions, mismatched and empty arrays,
    /// a mip, a cube face, an array slice (and every slice), identifiers carrying their own mip;
    /// and the recorded sizes of the binding and of the color/depth overloads (<c>bind.*</c>).
    /// </summary>
    void BindingTargets()
    {
        var mrt = Keep(new Material(Resources.Load<Shader>("BindingMrt")));
        var block = new MaterialPropertyBlock();
        RenderTexture Color4(int depth = 0)
        {
            var rt = Keep(new RenderTexture(4, 4, depth, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear));
            rt.Create();
            return rt;
        }
        var colors = Enumerable.Range(0, 4).Select(_ => Color4()).ToArray();
        var depthTexture = Keep(new RenderTexture(4, 4, 24, RenderTextureFormat.Depth));
        depthTexture.Create();
        var withDepth = Color4(24);

        void Fill(RenderTexture rt, int i) => Execute(cmd =>
        {
            cmd.SetRenderTarget(rt);
            cmd.ClearRenderTarget(true, true, new Color32((byte)(10 + i), (byte)(20 + i), (byte)(30 + i), 255), 1f);
        });
        void Reset()
        {
            for (int i = 0; i < colors.Length; i++) Fill(colors[i], i);
            Fill(withDepth, 9);
            Execute(cmd => { cmd.SetRenderTarget(depthTexture); cmd.ClearRenderTarget(true, false, Color.clear, 1f); });
        }
        void Draw(CommandBuffer cmd, int tag, float depth, CompareFunction test = CompareFunction.LessEqual)
        {
            block.SetFloat("_Tag", tag);
            block.SetFloat("_Depth", depth);
            block.SetFloat("_ZTest", (float)test);
            cmd.DrawProcedural(Matrix4x4.identity, mrt, 0, MeshTopology.Triangles, 3, 1, block);
        }
        void ReadAll(string key)
        {
            for (int i = 0; i < colors.Length; i++) Value(key + ".c" + i, Read(colors[i]));
        }
        RenderTargetBinding Bind(int count, RenderTargetIdentifier depth, RenderBufferLoadAction load = RenderBufferLoadAction.Load,
            RenderBufferStoreAction store = RenderBufferStoreAction.Store, RenderBufferLoadAction depthLoad = RenderBufferLoadAction.Load,
            RenderBufferStoreAction depthStore = RenderBufferStoreAction.Store) =>
            new RenderTargetBinding(colors.Take(count).Select(c => new RenderTargetIdentifier(c)).ToArray(),
                Enumerable.Repeat(load, count).ToArray(), Enumerable.Repeat(store, count).ToArray(), depth, depthLoad, depthStore);
        // One case: the targets reset, the binding recorded and executed (with what it throws or
        // logs), then every color read.
        void Case(string key, Func<RenderTargetBinding> binding, Action<CommandBuffer> draws)
        {
            Reset();
            Try(key, () => Execute(cmd => { cmd.SetRenderTarget(binding()); draws(cmd); }));
            ReadAll(key);
        }

        for (int n = 1; n <= 4; n++)
        {
            int count = n;
            Case("bind.colors." + n, () => Bind(count, depthTexture), cmd => Draw(cmd, 100, 0.5f));
        }
        // Depth: 0.5, then nearer (0.25), then farther (0.75): a bound depth buffer keeps 150.
        void DepthDraws(CommandBuffer cmd) { Draw(cmd, 100, 0.5f); Draw(cmd, 150, 0.25f); Draw(cmd, 200, 0.75f); }
        Case("bind.depth.texture", () => Bind(2, depthTexture), DepthDraws);
        Case("bind.depth.cleared", () => Bind(2, depthTexture), cmd => { cmd.ClearRenderTarget(true, false, Color.clear, 0.4f); Draw(cmd, 100, 0.5f); Draw(cmd, 150, 0.25f); });
        Case("bind.depth.none", () => Bind(2, new RenderTargetIdentifier(BuiltinRenderTextureType.None)), DepthDraws);
        Case("bind.depth.firstColor", () => Bind(2, colors[0]), DepthDraws);
        Case("bind.depth.ownBuffer", () => new RenderTargetBinding(new RenderTargetIdentifier[] { withDepth, colors[1] },
            new[] { RenderBufferLoadAction.Load, RenderBufferLoadAction.Load }, new[] { RenderBufferStoreAction.Store, RenderBufferStoreAction.Store },
            withDepth, RenderBufferLoadAction.Load, RenderBufferStoreAction.Store), DepthDraws);
        Value("bind.depth.ownBuffer.own", Read(withDepth));
        // The None depth target through the other overloads.
        var none = new RenderTargetIdentifier(BuiltinRenderTextureType.None);
        Reset();
        Try("bind.noneDepth.colors", () => Execute(cmd => { cmd.SetRenderTarget(new RenderTargetIdentifier[] { colors[0], colors[1] }, none); DepthDraws(cmd); }));
        ReadAll("bind.noneDepth.colors");
        Reset();
        Try("bind.noneDepth.color", () => Execute(cmd => { cmd.SetRenderTarget(colors[0], none); DepthDraws(cmd); }));
        ReadAll("bind.noneDepth.color");
        Reset();
        Try("bind.noneDepth.actions", () => Execute(cmd =>
        {
            cmd.SetRenderTarget(colors[0], RenderBufferLoadAction.Load, RenderBufferStoreAction.Store, none, RenderBufferLoadAction.DontCare, RenderBufferStoreAction.DontCare);
            DepthDraws(cmd);
        }));
        ReadAll("bind.noneDepth.actions");
        // The same with the fourth color bound before: whether the draws go there instead.
        Reset();
        Try("bind.noneDepth.previous.binding", () => Execute(cmd => { cmd.SetRenderTarget(colors[3]); cmd.SetRenderTarget(Bind(2, none)); DepthDraws(cmd); }));
        ReadAll("bind.noneDepth.previous.binding");
        Reset();
        Try("bind.noneDepth.previous.colors", () => Execute(cmd => { cmd.SetRenderTarget(colors[3]); cmd.SetRenderTarget(new RenderTargetIdentifier[] { colors[0], colors[1] }, none); DepthDraws(cmd); }));
        ReadAll("bind.noneDepth.previous.colors");
        Reset();
        Try("bind.noneDepth.previous.color", () => Execute(cmd => { cmd.SetRenderTarget(colors[3]); cmd.SetRenderTarget(colors[0], none); DepthDraws(cmd); }));
        ReadAll("bind.noneDepth.previous.color");
        // A None color with a depth texture.
        Reset();
        Try("bind.noneColor", () => Execute(cmd => { cmd.SetRenderTarget(colors[3]); cmd.SetRenderTarget(none, depthTexture); DepthDraws(cmd); }));
        ReadAll("bind.noneColor");
        // A clear of every bound color, and of only as many background colors as given.
        Case("bind.clear.all", () => Bind(3, depthTexture), cmd => cmd.ClearRenderTarget(false, true, new Color32(77, 88, 99, 255)));
        Case("bind.clear.colors", () => Bind(3, depthTexture), cmd =>
            cmd.ClearRenderTarget(RTClearFlags.Color, new Color[] { new Color32(1, 2, 3, 255), new Color32(4, 5, 6, 255), new Color32(7, 8, 9, 255) }));

        // Load and store actions.
        Case("bind.load.clear", () => Bind(2, depthTexture, RenderBufferLoadAction.Clear), cmd => Draw(cmd, 100, 0.5f, CompareFunction.Always));
        Case("bind.load.clearDepth", () => Bind(2, depthTexture, depthLoad: RenderBufferLoadAction.Clear), cmd => Draw(cmd, 100, 0.5f, CompareFunction.Always));
        Case("bind.load.dontCare", () => Bind(2, depthTexture, RenderBufferLoadAction.DontCare, depthLoad: RenderBufferLoadAction.DontCare), cmd => Draw(cmd, 100, 0.5f));
        Case("bind.load.dontCare.nodraw", () => Bind(2, depthTexture, RenderBufferLoadAction.DontCare), cmd => { });
        Case("bind.store.dontCare", () => Bind(2, depthTexture, store: RenderBufferStoreAction.DontCare, depthStore: RenderBufferStoreAction.DontCare), cmd => Draw(cmd, 100, 0.5f));
        Case("bind.store.resolve", () => Bind(2, depthTexture, store: RenderBufferStoreAction.Resolve), cmd => Draw(cmd, 100, 0.5f));
        // The store action of a binding, then another binding of the same targets that loads them.
        Case("bind.store.dontCare.reload", () => Bind(2, depthTexture, store: RenderBufferStoreAction.DontCare), cmd =>
        {
            Draw(cmd, 100, 0.5f);
            cmd.SetRenderTarget(Bind(2, depthTexture));
            Draw(cmd, 150, 0.25f);
        });

        // Arrays that do not match, none, more than eight.
        Case("bind.mismatch.loads", () => new RenderTargetBinding(new RenderTargetIdentifier[] { colors[0], colors[1] },
            new[] { RenderBufferLoadAction.Load }, new[] { RenderBufferStoreAction.Store, RenderBufferStoreAction.Store },
            depthTexture, RenderBufferLoadAction.Load, RenderBufferStoreAction.Store), cmd => Draw(cmd, 100, 0.5f));
        Case("bind.mismatch.stores", () => new RenderTargetBinding(new RenderTargetIdentifier[] { colors[0], colors[1] },
            new[] { RenderBufferLoadAction.Load, RenderBufferLoadAction.Load }, new[] { RenderBufferStoreAction.Store },
            depthTexture, RenderBufferLoadAction.Load, RenderBufferStoreAction.Store), cmd => Draw(cmd, 100, 0.5f));
        Case("bind.empty", () => new RenderTargetBinding(new RenderTargetIdentifier[0], new RenderBufferLoadAction[0], new RenderBufferStoreAction[0],
            depthTexture, RenderBufferLoadAction.Load, RenderBufferStoreAction.Store), cmd => Draw(cmd, 100, 0.5f));
        Case("bind.nine", () => new RenderTargetBinding(Enumerable.Range(0, 9).Select(i => new RenderTargetIdentifier(colors[i % 4])).ToArray(),
            Enumerable.Repeat(RenderBufferLoadAction.Load, 9).ToArray(), Enumerable.Repeat(RenderBufferStoreAction.Store, 9).ToArray(),
            depthTexture, RenderBufferLoadAction.Load, RenderBufferStoreAction.Store), cmd => Draw(cmd, 100, 0.5f));
        Case("bind.single", () => new RenderTargetBinding(colors[1], RenderBufferLoadAction.Load, RenderBufferStoreAction.Store,
            depthTexture, RenderBufferLoadAction.Load, RenderBufferStoreAction.Store), DepthDraws);
        Case("bind.setup", () => new RenderTargetBinding(new RenderTargetSetup(new[] { colors[0].colorBuffer, colors[2].colorBuffer }, depthTexture.depthBuffer)), DepthDraws);
        var unset = default(RenderTargetBinding);
        Try("bind.default", () => Execute(cmd => cmd.SetRenderTarget(unset)));

        // Mip, face and slice.
        RenderTexture Mipped()
        {
            var rt = Keep(new RenderTexture(new RenderTextureDescriptor(8, 8, RenderTextureFormat.ARGB32, 0) { useMipMap = true, autoGenerateMips = false, sRGB = false }));
            rt.Create();
            return rt;
        }
        RenderTexture Sliced(TextureDimension dimension)
        {
            var rt = Keep(new RenderTexture(new RenderTextureDescriptor(4, 4, RenderTextureFormat.ARGB32, 0)
            { dimension = dimension, volumeDepth = dimension == TextureDimension.Cube ? 1 : 3, sRGB = false }));
            rt.Create();
            return rt;
        }
        void Layered(string key, RenderTexture[] targets, int mip, CubemapFace face, int slice, bool explicitLevel = true, int mipsToRead = 1)
        {
            foreach (var t in targets)
                Execute(cmd =>
                {
                    for (int m = 0; m < t.mipmapCount; m++)
                        for (int f = 0; f < (t.dimension == TextureDimension.Cube ? 6 : 1); f++)
                        {
                            if (t.dimension == TextureDimension.Cube) cmd.SetRenderTarget(t, m, (CubemapFace)f, 0);
                            else cmd.SetRenderTarget(t, m, CubemapFace.Unknown, -1);
                            cmd.ClearRenderTarget(false, true, new Color32(10, 20, (byte)(30 + m), 255));
                        }
                });
            var ids = targets.Select(t => explicitLevel ? new RenderTargetIdentifier(t) : new RenderTargetIdentifier(t, mip, face, slice)).ToArray();
            // The depth target is the first color's identifier (no depth buffer): a binding with
            // the None depth target draws nothing (bind.depth.none).
            var binding = new RenderTargetBinding(ids, ids.Select(_ => RenderBufferLoadAction.Load).ToArray(), ids.Select(_ => RenderBufferStoreAction.Store).ToArray(),
                ids[0], RenderBufferLoadAction.DontCare, RenderBufferStoreAction.DontCare);
            Try(key, () => Execute(cmd =>
            {
                if (explicitLevel) cmd.SetRenderTarget(binding, mip, face, slice);
                else cmd.SetRenderTarget(binding);
                Draw(cmd, 100, 0.5f, CompareFunction.Always);
            }));
            for (int i = 0; i < targets.Length; i++)
                for (int m = 0; m < mipsToRead; m++) Value(key + ".t" + i + ".mip" + m, Read(targets[i], m));
        }
        var mipped = new[] { Mipped(), Mipped() };
        Layered("bind.mip", mipped, 1, CubemapFace.Unknown, 0, mipsToRead: 2);
        Layered("bind.mip.identifier", mipped, 1, CubemapFace.Unknown, 0, explicitLevel: false, mipsToRead: 2);
        var arrays = new[] { Sliced(TextureDimension.Tex2DArray), Sliced(TextureDimension.Tex2DArray) };
        Layered("bind.slice", arrays, 0, CubemapFace.Unknown, 2);
        Layered("bind.slice.all", arrays, 0, CubemapFace.Unknown, -1);
        Layered("bind.slice.identifier", arrays, 0, CubemapFace.Unknown, 1, explicitLevel: false);
        var cubes = new[] { Sliced(TextureDimension.Cube), Sliced(TextureDimension.Cube) };
        Layered("bind.face", cubes, 0, CubemapFace.NegativeY, 0);

        // Recorded sizes.
        void Size(string key, Action<CommandBuffer> record)
        {
            var cb = new CommandBuffer();
            Try("bind.size." + key, () => { record(cb); Value("bind.size." + key, cb.sizeInBytes.ToString()); });
            Try("bind.size." + key + ".twice", () => { record(cb); Value("bind.size." + key + ".twice", cb.sizeInBytes.ToString()); });
            cb.Release();
        }
        RenderTargetIdentifier c0 = colors[0], d = depthTexture;
        foreach (int n in new[] { 1, 2, 3, 4, 8 })
        {
            var b = new RenderTargetBinding(Enumerable.Range(0, n).Select(i => new RenderTargetIdentifier(colors[i % 4])).ToArray(),
                Enumerable.Repeat(RenderBufferLoadAction.Load, n).ToArray(), Enumerable.Repeat(RenderBufferStoreAction.Store, n).ToArray(),
                d, RenderBufferLoadAction.Load, RenderBufferStoreAction.Store);
            Size("binding" + n, cb => cb.SetRenderTarget(b));
            Size("binding" + n + ".level", cb => cb.SetRenderTarget(b, 1, CubemapFace.PositiveX, 2));
        }
        Size("colorDepth", cb => cb.SetRenderTarget(c0, d));
        Size("colorDepth.mip", cb => cb.SetRenderTarget(c0, d, 1));
        Size("colorDepth.face", cb => cb.SetRenderTarget(c0, d, 1, CubemapFace.PositiveX));
        Size("colorDepth.slice", cb => cb.SetRenderTarget(c0, d, 1, CubemapFace.Unknown, 2));
        Size("colorDepth.actions", cb => cb.SetRenderTarget(c0, RenderBufferLoadAction.Load, RenderBufferStoreAction.Store, d, RenderBufferLoadAction.DontCare, RenderBufferStoreAction.DontCare));
        Size("color.actions", cb => cb.SetRenderTarget(c0, RenderBufferLoadAction.Load, RenderBufferStoreAction.Store));
        Size("color.actions2", cb => cb.SetRenderTarget(c0, RenderBufferLoadAction.Load, RenderBufferStoreAction.Store, RenderBufferLoadAction.DontCare, RenderBufferStoreAction.DontCare));
        Size("color.face", cb => cb.SetRenderTarget(c0, 1, CubemapFace.PositiveX));
        Size("color.slice", cb => cb.SetRenderTarget(c0, 1, CubemapFace.Unknown, 2));
        Size("colors2.level", cb => cb.SetRenderTarget(new[] { c0, new RenderTargetIdentifier(colors[1]) }, d, 1, CubemapFace.Unknown, 2));
        Size("colors4", cb => cb.SetRenderTarget(colors.Select(c => new RenderTargetIdentifier(c)).ToArray(), d));
    }
}
