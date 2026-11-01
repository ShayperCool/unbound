using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Rendering;

public partial class ScreenCaptureProbe
{
    bool internalState;

    // The same caller draw with and without the screenshot call. Both attachments,
    // the viewport, scissor, wireframe and sRGB write state are observed through pixels.
    void StateControl(bool copy)
    {
        string key = copy ? "state.copy" : "state.none";
        var a = Target(W, H, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        var b = Target(W, H, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        var destination = Target(W, H, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        var scissor = new CommandBuffer { name = "screenshot caller scissor" };
        var saved = RenderTexture.active;
        bool srgbWrite = GL.sRGBWrite, wireframe = GL.wireframe;
        try
        {
            RenderTexture.active = destination;
            GL.Clear(false, true, Color.clear);
            Graphics.SetRenderTarget(new[] { a.colorBuffer, b.colorBuffer }, a.depthBuffer);
            GL.Viewport(new Rect(8, 4, 48, 24));
            scissor.EnableScissorRect(new Rect(20, 8, 24, 16));
            Graphics.ExecuteCommandBuffer(scissor);
            GL.sRGBWrite = false;
            GL.wireframe = true;
            BeginLogs();
            if (copy) ScreenCapture.CaptureScreenshotIntoRenderTexture(destination);
            // No target, viewport, scissor or flag reset between capture and caller draw.
            GL.PushMatrix();
            try
            {
                GL.LoadOrtho();
                marker.SetPass(1);
                GL.Begin(GL.TRIANGLES);
                GL.Vertex3(0, 0, 0); GL.Vertex3(1, 1, 0); GL.Vertex3(0, 1, 0);
                GL.Vertex3(0, 0, 0); GL.Vertex3(1, 0, 0); GL.Vertex3(1, 1, 0);
                GL.End();
            }
            finally { GL.PopMatrix(); }
            EndLogs(key);
        }
        finally
        {
            GL.wireframe = wireframe;
            GL.sRGBWrite = srgbWrite;
            scissor.Clear();
            scissor.DisableScissorRect();
            Graphics.ExecuteCommandBuffer(scissor);
            scissor.Release();
            RenderTexture.active = saved;
            GL.Viewport(new Rect(0, 0, W, H));
        }
        ReadTarget(key + ".first", a);
        ReadTarget(key + ".second", b);
        if (copy) ReadTarget(key + ".image", destination);
        foreach (var rt in new[] { a, b, destination })
        {
            rt.Release();
            DestroyImmediate(rt);
        }
    }

    IEnumerator InternalStateSteps()
    {
        for (int i = 0; i < 20; i++) yield return null;
        Value("screen", "[" + Screen.width + "," + Screen.height + "]");
        Value("colorSpace", Q(QualitySettings.activeColorSpace.ToString()));
        Value("profile", Q("internal-state"));
        yield return new WaitForEndOfFrame();
        DrawMarker();
        ReadScreen("state.source", TextureFormat.RGBAFloat, true);
        marker.SetVector("_Value", new Vector4(0.25f, 0.5f, 0.75f, 1));
        StateControl(false);
        StateControl(true);
        Flush();

        // An empty buffer makes the screen camera use its intermediate target.
        // Only superSize 1 is called inside OnPostRender, once, without re-rendering.
        marker.SetVector("_Value", new Vector4(1, 0, 1, 0.8f));
        var destination = Target(W, H, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        var empty = new CommandBuffer { name = "screenshot screen callback" };
        var callback = cam.gameObject.AddComponent<ScreenCapturePostControl>();
        int calls = 0;
        Exception failure = null;
        callback.capture = () =>
        {
            callback.capture = null;
            calls++;
            try
            {
                DrawMarker();
                AsTexture("post.buffer.asTexture", () => ScreenCapture.CaptureScreenshotAsTexture(1));
                BeginLogs();
                ScreenCapture.CaptureScreenshotIntoRenderTexture(destination);
                EndLogs("post.buffer.into");
                ReadTarget("post.buffer.into", destination);
            }
            catch (Exception e) { failure = e; }
        };
        cam.AddCommandBuffer(CameraEvent.AfterEverything, empty);
        yield return null;
        yield return new WaitForEndOfFrame();
        cam.RemoveCommandBuffer(CameraEvent.AfterEverything, empty);
        empty.Release();
        DestroyImmediate(callback);
        Value("post.buffer.calls", calls.ToString());
        ReadScreen("post.buffer.final", TextureFormat.RGBAFloat, true);
        destination.Release();
        DestroyImmediate(destination);
        if (failure != null) throw failure;
        if (calls != 1) throw new InvalidOperationException("Expected one screen callback, got " + calls);
    }
}

public sealed class ScreenCapturePostControl : MonoBehaviour
{
    public Action capture;
    void OnPostRender() => capture?.Invoke();
}
