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
    }

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
