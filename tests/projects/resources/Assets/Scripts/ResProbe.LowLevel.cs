using System;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;

public partial class ResProbe
{
    string PixelGrid(RenderTexture rt)
    {
        var old = RenderTexture.active;
        RenderTexture.active = rt;
        var read = Add(new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, false, true));
        read.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
        read.Apply(false);
        RenderTexture.active = old;
        var text = new StringBuilder();
        foreach (var color in read.GetPixels32())
            text.Append(color.r > 200 ? color.g > 200 ? color.b > 200 ? 'W' : 'Y' : 'R'
                : color.g > 200 ? 'G' : color.b > 200 ? 'B' : 'K');
        return text.ToString();
    }

    void LowLevelGraphics()
    {
        Value("lowlevel.scale.initial", V(new Vector4(ScalableBufferManager.widthScaleFactor, ScalableBufferManager.heightScaleFactor)));
        ScalableBufferManager.ResizeBuffers(0.5f, 0.25f);
        Value("lowlevel.scale.resized", V(new Vector4(ScalableBufferManager.widthScaleFactor, ScalableBufferManager.heightScaleFactor)));
        ScalableBufferManager.ResizeBuffers(1f, 1f);
        var quad = Add(new Mesh());
        quad.vertices = new[] { new Vector3(-1,-1,0), new Vector3(1,-1,0), new Vector3(1,1,0), new Vector3(-1,1,0) };
        quad.triangles = new[] { 0,1,2,0,2,3 };
        var material = Add(new Material(Shader.Find("Probe/ClipSolid")));
        var rt = Add(new RenderTexture(8, 8, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear));
        var scissors = new[] { new Rect(2,1,3,4), new Rect(2.8f,1.8f,3.8f,4.8f), new Rect(-2,-1,4,4), new Rect(2,2,0,3) };
        for (int i = 0; i < 9; i++)
        {
            var commands = new CommandBuffer();
            commands.SetRenderTarget(rt);
            commands.ClearRenderTarget(false, true, Color.black);
            commands.EnableScissorRect(scissors[i < 4 ? i : 0]);
            if (i == 4) commands.SetViewport(new Rect(1,2,5,3));
            if (i == 5) commands.ClearRenderTarget(false, true, Color.red);
            else
            {
                if (i == 6) commands.DisableScissorRect();
                if (i == 7) commands.SetRenderTarget(rt);
                if (i == 8) commands.Blit(Texture2D.whiteTexture, rt);
                else commands.DrawMesh(quad, Matrix4x4.identity, material, 0, 0);
            }
            commands.DisableScissorRect();
            Graphics.ExecuteCommandBuffer(commands);
            commands.Release();
            Value("lowlevel.scissor." + i, S(PixelGrid(rt)));
        }
        foreach (int count in new[] { -1, 0, 1, 2, 9 })
            Try("lowlevel.mrt.color_count." + count, () =>
            {
                using (var commands = new CommandBuffer())
                    commands.ClearRenderTarget(RTClearFlags.All, count < 0 ? null : new Color[count], 1f, 0);
                return S("ok");
            });
        var colors = new[] { Color.red, Color.green, Color.blue };
        var targets = new RenderTexture[3];
        var ids = new RenderTargetIdentifier[3];
        for (int i = 0; i < 3; i++)
        {
            targets[i] = Add(new RenderTexture(8, 8, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear));
            ids[i] = targets[i];
        }
        var mrt = new CommandBuffer();
        mrt.SetRenderTarget(ids, targets[0]);
        mrt.ClearRenderTarget(RTClearFlags.All, colors, 1f, 0);
        colors[0] = Color.white; // Recorded commands retain a copy.
        Graphics.ExecuteCommandBuffer(mrt);
        for (int i = 0; i < 3; i++) Value("lowlevel.mrt.clear" + i, C(Pixel(targets[i])));
        mrt.Clear();
        mrt.SetRenderTarget(ids, targets[0]);
        mrt.ClearRenderTarget(RTClearFlags.Color, new[] { Color.white, Color.yellow, Color.black }, 1f, 0);
        mrt.DrawMesh(quad, Matrix4x4.identity, material, 0, 1);
        Graphics.ExecuteCommandBuffer(mrt);
        for (int i = 0; i < 3; i++) Value("lowlevel.mrt.after_draw" + i, C(Pixel(targets[i])));
        mrt.Release();
        Clear();
    }
}
