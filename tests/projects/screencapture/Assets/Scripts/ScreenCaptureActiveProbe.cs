using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Rendering;

public partial class ScreenCaptureProbe
{
    static readonly Color CaptureSourceColor = new Color(0.75f, 0.125f, 0.625f, 0.75f);

    IEnumerator ActiveSteps()
    {
        for (int i = 0; i < 20; i++) yield return null;
        Value("screen", "[" + Screen.width + "," + Screen.height + "]");
        Value("colorSpace", Q(QualitySettings.activeColorSpace.ToString()));

        foreach (bool endOfFrame in new[] { false, true })
        {
            if (endOfFrame) yield return new WaitForEndOfFrame();
            else yield return null;
            var source = Target(W, H, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var destination = Target(W, H, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            string key = endOfFrame ? "active.end" : "active.update";
            RenderTexture.active = source;
            GL.Clear(true, true, CaptureSourceColor);
            ReadTarget(key + ".source", source);
            ReadTarget(key + ".destinationBefore", destination);
            AsTexture(key + ".texture", () => ScreenCapture.CaptureScreenshotAsTexture());
            Value(key + ".texture.retainedActive", B(RenderTexture.active == source));
            IntoTarget(key + ".target", destination);
            Value(key + ".target.retainedActive", B(RenderTexture.active == source));
            ReadTarget(key + ".sourceAfter", source);
            RenderTexture.active = null;
            DestroyImmediate(source);
            DestroyImmediate(destination);
        }

        if (QualitySettings.activeColorSpace != ColorSpace.Linear) yield break;
        foreach (var c in new[] {
            (name: "larger", width: W * 2, height: H * 2, format: RenderTextureFormat.ARGB32, rw: RenderTextureReadWrite.sRGB),
            (name: "smaller", width: W / 2, height: H / 2, format: RenderTextureFormat.ARGB32, rw: RenderTextureReadWrite.sRGB),
            (name: "linearByte", width: W, height: H, format: RenderTextureFormat.ARGB32, rw: RenderTextureReadWrite.Linear),
            (name: "float", width: W, height: H, format: RenderTextureFormat.ARGBFloat, rw: RenderTextureReadWrite.Linear),
        })
        {
            yield return null;
            yield return new WaitForEndOfFrame();
            var source = Target(c.width, c.height, c.format, c.rw);
            var destination = Target(W, H, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            string key = "active." + c.name;
            RenderTexture.active = source;
            GL.Clear(true, true, CaptureSourceColor);
            DrawSourcePattern();
            ReadTarget(key + ".source", source);
            // AsTexture returns the full screen-sized image. Only the source's overlapping
            // rectangle has reproducible pixels when that attachment is smaller.
            AsTexture(key + ".texture", () => ScreenCapture.CaptureScreenshotAsTexture(),
                Mathf.Min(W, c.width), Mathf.Min(H, c.height));
            IntoTarget(key + ".target", destination);
            if (c.name == "larger") IntoTarget(key + ".itself", source);
            RenderTexture.active = null;
            DestroyImmediate(source);
            DestroyImmediate(destination);
        }

        foreach (TextureDimension dimension in new[] {
            TextureDimension.Tex2D, TextureDimension.Tex2DArray, TextureDimension.Tex3D, TextureDimension.Cube })
        {
            yield return null;
            yield return new WaitForEndOfFrame();
            bool isMipped = dimension == TextureDimension.Tex2D;
            bool isCubemap = dimension == TextureDimension.Cube;
            bool hasLayers = dimension == TextureDimension.Tex2DArray || dimension == TextureDimension.Tex3D;
            int width = isMipped ? 2 * W : W;
            int height = isMipped ? 2 * H : isCubemap ? W : H;
            var desc = new RenderTextureDescriptor(width, height, RenderTextureFormat.ARGB32, 0)
            {
                autoGenerateMips = false,
                useMipMap = isMipped,
                sRGB = true,
                dimension = dimension,
                volumeDepth = hasLayers ? 2 : 1,
            };
            var source = new RenderTexture(desc);
            source.Create();
            var destination = Target(W, H, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            Graphics.SetRenderTarget(source, 0, isCubemap ? CubemapFace.PositiveX : CubemapFace.Unknown, 0);
            GL.Clear(true, true, new Color(0.25f, 0.625f, 0.875f, 0.5f));
            Graphics.SetRenderTarget(source, isMipped ? 1 : 0,
                isCubemap ? CubemapFace.NegativeX : CubemapFace.Unknown, hasLayers ? 1 : 0);
            GL.Clear(true, true, CaptureSourceColor);
            string key = isMipped ? "active.mip" : "active." + dimension;
            AsTexture(key + ".texture", () => ScreenCapture.CaptureScreenshotAsTexture());
            var read = new Texture2D(W, H, TextureFormat.RGBAFloat, false, true);
            read.ReadPixels(new Rect(0, 0, W, H), 0, 0);
            Pixels(key + ".sourceAfterTexture", read);
            BeginLogs();
            ScreenCapture.CaptureScreenshotIntoRenderTexture(destination);
            EndLogs(key + ".target");
            read.ReadPixels(new Rect(0, 0, W, H), 0, 0);
            Pixels(key + ".sourceAfterTarget", read);
            ReadTarget(key + ".target", destination);
            RenderTexture.active = null;
            DestroyImmediate(read);
            DestroyImmediate(source);
            DestroyImmediate(destination);
        }

        foreach (bool bind in new[] { false, true })
        {
            yield return null;
            yield return new WaitForEndOfFrame();
            var desc = new RenderTextureDescriptor(W, H, RenderTextureFormat.ARGB32, 0)
            {
                msaaSamples = 4,
                bindMS = bind,
                sRGB = true,
            };
            var source = new RenderTexture(desc);
            source.Create();
            var target = Target(W, H, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            string key = bind ? "active.msaa.bound" : "active.msaa.resolved";
            Value(key + ".samples", source.antiAliasing.ToString());
            RenderTexture.active = source;
            GL.Clear(false, true, CaptureSourceColor);
            DrawSourcePattern();
            AsTexture(key + ".texture", () => ScreenCapture.CaptureScreenshotAsTexture());
            Value(key + ".texture.active", B(RenderTexture.active == source));
            IntoTarget(key + ".target", target);
            Value(key + ".target.active", B(RenderTexture.active == source));
            RenderTexture.active = null;
            DestroyImmediate(source);
            DestroyImmediate(target);
        }

        foreach (var format in new[] { RenderTextureFormat.Depth, RenderTextureFormat.RInt,
            RenderTextureFormat.RGInt, RenderTextureFormat.ARGBInt })
        {
            yield return null;
            yield return new WaitForEndOfFrame();
            bool depthOnly = format == RenderTextureFormat.Depth;
            var source = new RenderTexture(W, H, 24, format);
            if (!source.Create()) throw new System.InvalidOperationException("active test target was not created");
            var destination = Target(W, H, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            string key = "active.unavailable." + format;
            if (depthOnly) ReadTarget(key + ".before", destination);
            RenderTexture.active = source;
            GL.Clear(true, !depthOnly, CaptureSourceColor);
            // The metadata and binding agree between Editor runs. Pixels from a target
            // without readable colors vary between runs.
            AsTexture(key + ".texture", () => ScreenCapture.CaptureScreenshotAsTexture(), recordPixels: false);
            Value(key + ".texture.active", B(RenderTexture.active == source));
            if (depthOnly) IntoTarget(key + ".target", destination);
            else
            {
                BeginLogs();
                ScreenCapture.CaptureScreenshotIntoRenderTexture(destination);
                EndLogs(key + ".target");
            }
            Value(key + ".target.active", B(RenderTexture.active == source));
            RenderTexture.active = null;
            DestroyImmediate(source);
            DestroyImmediate(destination);
        }

        foreach (bool previousActive in new[] { false, true })
        {
            yield return null;
            yield return new WaitForEndOfFrame();
            var previous = Target(W, H, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var commands = Target(W, H, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var destination = Target(W, H, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            string key = previousActive ? "active.command.previous" : "active.command.screen";
            RenderTexture.active = previous;
            GL.Clear(false, true, new Color(.25f,.5f,.75f,1f));
            ReadTarget(key + ".previous", previous);
            RenderTexture.active = previousActive ? previous : null;
            var work = new CommandBuffer();
            work.SetRenderTarget(commands);
            work.ClearRenderTarget(false, true, CaptureSourceColor);
            Graphics.ExecuteCommandBuffer(work);
            Value(key + ".activePrevious", B(RenderTexture.active == previous));
            Value(key + ".activeCommand", B(RenderTexture.active == commands));
            AsTexture(key + ".texture", () => ScreenCapture.CaptureScreenshotAsTexture());
            IntoTarget(key + ".target", destination);
            Value(key + ".retainsPrevious", B(RenderTexture.active == previous));
            Value(key + ".retainsCommand", B(RenderTexture.active == commands));
            ReadTarget(key + ".commandColor", commands);
            RenderTexture.active = null;
            work.Release();
            DestroyImmediate(previous);
            DestroyImmediate(commands);
            DestroyImmediate(destination);
        }

        yield return null;
        yield return new WaitForEndOfFrame();
        foreach (bool useActiveTarget in new[] { false, true })
        foreach (bool wireframe in new[] { false, true })
        foreach (bool encode in new[] { false, true })
        {
            var source = useActiveTarget
                ? Target(W, H, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear) : null;
            var destination = Target(W, H, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            RenderTexture.active = source;
            if (useActiveTarget) GL.Clear(false, true, CaptureSourceColor);
            bool savedWrite = GL.sRGBWrite;
            bool savedWireframe = GL.wireframe;
            GL.sRGBWrite = encode;
            GL.wireframe = wireframe;
            string origin = useActiveTarget ? "active" : "screen";
            string key = "blit.state." + origin + ".wire." + B(wireframe) + ".srgb." + B(encode);
            IntoTarget(key, destination);
            Value(key + ".retainedWrite", B(GL.sRGBWrite == encode));
            Value(key + ".retainedWire", B(GL.wireframe == wireframe));
            Value(key + ".retainedTarget", B(RenderTexture.active == source));
            GL.sRGBWrite = savedWrite;
            GL.wireframe = savedWireframe;
            RenderTexture.active = null;
            if (source != null) DestroyImmediate(source);
            DestroyImmediate(destination);
        }

        foreach (bool larger in new[] { false, true })
        {
            var source = larger ? null : Target(W / 2, H / 2, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            int width = larger ? W * 2 : W / 2;
            int height = larger ? H * 2 : H / 2;
            var destination = Target(width, height, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            RenderTexture.active = source;
            if (source != null) GL.Clear(false, true, CaptureSourceColor);
            bool saved = GL.wireframe;
            GL.wireframe = true;
            IntoTarget(larger ? "blit.state.wire.largerDestination" : "blit.state.wire.smallerSourceAndDestination", destination);
            GL.wireframe = saved;
            RenderTexture.active = null;
            if (source != null) DestroyImmediate(source);
            DestroyImmediate(destination);
        }
        RecordCaptureDestinationCreation();
        RecordCaptureIntoMsaaTargets();
        var colorSource = Target(W, H, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
        var depthDestination = new RenderTexture(W, H, 24, RenderTextureFormat.Depth);
        RenderTexture.active = colorSource;
        Value("blit.depthDestination.before", B(depthDestination.IsCreated()));
        BeginLogs();
        ScreenCapture.CaptureScreenshotIntoRenderTexture(depthDestination);
        EndLogs("blit.depthDestination");
        Value("blit.depthDestination.created", B(depthDestination.IsCreated()));
        Value("blit.depthDestination.active", B(RenderTexture.active == colorSource));
        RenderTexture.active = null;
        DestroyImmediate(colorSource);
        DestroyImmediate(depthDestination);
    }

    void RecordCaptureDestinationCreation()
    {
        foreach (string kind in new[] { "screen", "color", "depth", "integer", "array" })
        foreach (bool wireframe in new[] { false, true })
        {
            var format = kind == "depth" ? RenderTextureFormat.Depth
                : kind == "integer" ? RenderTextureFormat.RInt : RenderTextureFormat.ARGB32;
            var desc = new RenderTextureDescriptor(W, H, format, kind == "depth" ? 24 : 0);
            if (kind == "array")
            {
                desc.dimension = TextureDimension.Tex2DArray;
                desc.volumeDepth = 2;
            }
            var source = kind == "screen" ? null : new RenderTexture(desc);
            var destination = new RenderTexture(W, H, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            Graphics.SetRenderTarget(source, 0, CubemapFace.Unknown, 0);
            if (source != null && kind == "color") GL.Clear(false, true, CaptureSourceColor);
            bool saved = GL.wireframe;
            GL.wireframe = wireframe;
            string key = "blit.create." + kind + ".wire." + B(wireframe);
            Value(key + ".before", B(destination.IsCreated()));
            BeginLogs();
            ScreenCapture.CaptureScreenshotIntoRenderTexture(destination);
            EndLogs(key);
            Value(key + ".after", B(destination.IsCreated()));
            GL.wireframe = saved;
            RenderTexture.active = null;
            if (source != null) DestroyImmediate(source);
            DestroyImmediate(destination);
        }
    }

    void RecordCaptureIntoMsaaTargets()
    {
        foreach (string kind in new[] { "screen", "linear", "msaa", "msaaBound", "integer", "depth" })
        foreach (bool bind in new[] { false, true })
        {
            var format = kind == "integer" ? RenderTextureFormat.RInt
                : kind == "depth" ? RenderTextureFormat.Depth : RenderTextureFormat.ARGB32;
            bool sourceMsaa = kind == "msaa" || kind == "msaaBound";
            var source = kind == "screen" ? null : new RenderTexture(new RenderTextureDescriptor(W, H, format, kind == "depth" ? 24 : 0)
            {
                msaaSamples = sourceMsaa ? 4 : 1, bindMS = kind == "msaaBound",
            });
            var target = new RenderTexture(new RenderTextureDescriptor(W, H, RenderTextureFormat.ARGB32, 0)
            {
                msaaSamples = 4, bindMS = bind, sRGB = true,
            });
            target.Create();
            RenderTexture.active = target;
            GL.Clear(false, true, Sentinel);
            RenderTexture.active = source;
            if (source != null) GL.Clear(kind == "depth", kind != "depth", CaptureSourceColor);
            string key = "blit.msaa." + kind + ".bind." + B(bind);
            var previous = ReadRetainedColor(target);
            IntoTarget(key, target);
            var retained = ReadRetainedColor(target);
            Value(key + ".readError", B(retained == null));
            if (retained != null)
            {
                string bytes = FirstColorBytes(retained);
                // Compare the full retained buffer across the capture call. Its initial
                // retained bytes on a bound multisampled texture differ between engines.
                Value(key + ".retainedUnchanged", B(previous != null && previous.AsSpan().SequenceEqual(retained)));
                if (!bind) Value(key + ".retained", bytes);
                else Debug.Log(key + " retained: " + FirstColorBytes(previous) + " before, " + bytes + " after");
            }
            RenderTexture.active = null;
            if (source != null) DestroyImmediate(source);
            DestroyImmediate(target);
        }
    }

    static byte[] ReadRetainedColor(RenderTexture target)
    {
        var request = AsyncGPUReadback.Request(target, 0, UnityEngine.Experimental.Rendering.GraphicsFormat.R8G8B8A8_UNorm);
        request.WaitForCompletion();
        return request.hasError ? null : request.GetData<byte>().ToArray();
    }

    static string FirstColorBytes(byte[] colors) => colors == null ? "null"
        : "[" + colors[0] + "," + colors[1] + "," + colors[2] + "," + colors[3] + "]";

    void DrawSourcePattern()
    {
        GL.PushMatrix();
        GL.LoadOrtho();
        marker.SetPass(0);
        GL.Begin(GL.QUADS);
        GL.Vertex3(0.25f, 0.25f, 0); GL.Vertex3(0.25f, 0.75f, 0);
        GL.Vertex3(0.75f, 0.75f, 0); GL.Vertex3(0.75f, 0.25f, 0);
        GL.End();
        GL.PopMatrix();
    }
}
