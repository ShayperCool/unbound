using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Textures of the other shapes and external textures of other formats: what the native
/// pointers of cubemaps, arrays, 3D and multisampled textures name (<c>shape.*</c>), that a
/// plugin's writes through them are the texture's texels, what a callback finds bound for a
/// multisampled target, and external textures over a plugin's textures with mip levels, sRGB
/// texels and other formats (<c>ext.*</c>). The plugin's texture callback keeps a record of its
/// own, so the records of the callbacks before stay as they are.
/// </summary>
public partial class NativeGfxProbe
{
    [DllImport(Plugin)] static extern IntPtr NativeGfx_TextureEvent();
    [DllImport(Plugin)] static extern void NativeGfx_SetShape(int index, string label, IntPtr name);
    [DllImport(Plugin)] static extern IntPtr NativeGfx_Made(int index);
    [DllImport(Plugin)] static extern int NativeGfx_TextureNotes(byte[] buffer, int capacity);

    int shapes;
    RenderTexture layeredCube, layeredArray;

    void Shape(string label, IntPtr name) => NativeGfx_SetShape(shapes++, label, name);

    /// <summary>Runs the plugin's texture callback and returns what it recorded.</summary>
    List<string> TextureEvent(int id)
    {
        GL.IssuePluginEvent(NativeGfx_TextureEvent(), id);
        var buffer = new byte[1 << 14];
        int length = NativeGfx_TextureNotes(buffer, buffer.Length);
        return Encoding.UTF8.GetString(buffer, 0, Math.Min(length, buffer.Length - 1)).Split('\n').Where(s => s.Length > 0).ToList();
    }

    static Color32[] Solid(int count, Color32 color) => Enumerable.Repeat(color, count).ToArray();

    /// <summary>One image of a texture (a face, a layer, a slice) as <see cref="Pixels"/>
    /// gives a texture's.</summary>
    string ElementPixels(Texture source, int element, int width, int height, bool linear)
    {
        var copy = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32, linear ? RenderTextureReadWrite.Linear : RenderTextureReadWrite.sRGB);
        copy.filterMode = FilterMode.Point;
        copy.Create();
        Graphics.CopyTexture(source, element, 0, copy, 0, 0);
        var result = Pixels(copy, width, height);
        copy.Release();
        DestroyImmediate(copy);
        return result;
    }

    void Shapes()
    {
        var cube = Keep(new Cubemap(8, TextureFormat.RGBA32, true) { name = "cube" });
        for (int face = 0; face < 6; face++)
            for (int mip = 0; mip < 4; mip++)
                cube.SetPixels(Enumerable.Repeat(new Color(0.2f, 0.4f, 0.6f, 1f), (8 >> mip) * (8 >> mip)).ToArray(), (CubemapFace)face, mip);
        cube.Apply(false);
        var cubeHalf = Keep(new Cubemap(4, TextureFormat.RGBAHalf, false) { name = "cube.half" });
        cubeHalf.Apply(false);
        var array = Keep(new Texture2DArray(8, 4, 3, TextureFormat.RGBA32, true, false) { name = "array" });
        for (int layer = 0; layer < 3; layer++) array.SetPixels32(Solid(32, new Color32(40, 80, 120, 255)), layer, 0);
        array.Apply(true);
        var arrayLinear = Keep(new Texture2DArray(4, 4, 2, TextureFormat.RGBA32, false, true) { name = "array.linear" });
        arrayLinear.Apply(false);
        // Six square layers: an array all the same.
        var arraySix = Keep(new Texture2DArray(4, 4, 6, TextureFormat.RGBA32, false, true) { name = "array.six" });
        arraySix.Apply(false);
        // One layer: an array all the same.
        var arrayOne = Keep(new Texture2DArray(4, 4, 1, TextureFormat.RGBA32, false, true) { name = "array.one" });
        arrayOne.Apply(false);
        var volume = Keep(new Texture3D(4, 4, 4, TextureFormat.RGBA32, false) { name = "volume" });
        volume.SetPixels32(Solid(64, new Color32(40, 80, 120, 255)));
        volume.Apply(false);
        var cubeArray = Keep(new CubemapArray(4, 2, TextureFormat.RGBA32, false) { name = "cubearray" });
        cubeArray.Apply(false);
        var mipped = Keep(new Texture2D(8, 8, TextureFormat.RGBA32, true, true) { name = "mipped" });
        mipped.Apply(true);
        var rtCube = Keep(new RenderTexture(8, 8, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear) { name = "rt.cube", dimension = TextureDimension.Cube });
        rtCube.Create();
        var rtArray = Keep(new RenderTexture(8, 4, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear) { name = "rt.array", dimension = TextureDimension.Tex2DArray, volumeDepth = 3 });
        rtArray.Create();
        var rtArrayOne = Keep(new RenderTexture(8, 4, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear) { name = "rt.array.one", dimension = TextureDimension.Tex2DArray, volumeDepth = 1 });
        rtArrayOne.Create();
        var rtVolume = Keep(new RenderTexture(4, 4, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear) { name = "rt.volume", dimension = TextureDimension.Tex3D, volumeDepth = 4 });
        rtVolume.Create();
        var msaa = Keep(new RenderTexture(8, 8, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear) { name = "rt.msaa", antiAliasing = 4, filterMode = FilterMode.Point });
        msaa.Create();
        var msaaColor = Keep(new RenderTexture(8, 8, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear) { name = "rt.msaa.color", antiAliasing = 2 });
        msaaColor.Create();
        // The default read-write mode: sRGB in this project.
        var msaaSrgb = Keep(new RenderTexture(8, 8, 0, RenderTextureFormat.ARGB32) { name = "rt.msaa.srgb", antiAliasing = 4, filterMode = FilterMode.Point });
        msaaSrgb.Create();
        var msaaBound = Keep(new RenderTexture(8, 8, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear) { name = "rt.msaa.bound", antiAliasing = 4, bindTextureMS = true });
        msaaBound.Create();

        layeredCube = rtCube;
        layeredArray = rtArray;
        Shape("cube", cube.GetNativeTexturePtr());
        Shape("cube.half", cubeHalf.GetNativeTexturePtr());
        Shape("array", array.GetNativeTexturePtr());
        Shape("array.linear", arrayLinear.GetNativeTexturePtr());
        Shape("array.six", arraySix.GetNativeTexturePtr());
        Shape("array.one", arrayOne.GetNativeTexturePtr());
        Shape("volume", volume.GetNativeTexturePtr());
        Shape("cubearray", cubeArray.GetNativeTexturePtr());
        Shape("mipped", mipped.GetNativeTexturePtr());
        Shape("rt.cube", rtCube.GetNativeTexturePtr());
        Shape("rt.cube.depth", rtCube.GetNativeDepthBufferPtr());
        Shape("rt.array", rtArray.GetNativeTexturePtr());
        Shape("rt.array.depth", rtArray.GetNativeDepthBufferPtr());
        Shape("rt.array.one", rtArrayOne.GetNativeTexturePtr());
        Shape("rt.volume", rtVolume.GetNativeTexturePtr());
        Shape("rt.msaa", msaa.GetNativeTexturePtr());
        Shape("rt.msaa.depth", msaa.GetNativeDepthBufferPtr());
        Shape("rt.msaa.color", msaaColor.GetNativeTexturePtr());
        Shape("rt.msaa.color.depth", msaaColor.GetNativeDepthBufferPtr());
        Shape("rt.msaa.srgb", msaaSrgb.GetNativeTexturePtr());
        Shape("rt.msaa.srgb.depth", msaaSrgb.GetNativeDepthBufferPtr());
        Shape("rt.msaa.bound", msaaBound.GetNativeTexturePtr());
        Shape("rt.msaa.bound.depth", msaaBound.GetNativeDepthBufferPtr());
        Value("shape.formats", L(new Texture[] { cube, cubeHalf, array, arrayLinear, arraySix, arrayOne, volume, cubeArray, mipped, rtCube, rtArray, rtVolume, msaa, msaaSrgb }
            .Select(t => t.name + "=" + t.graphicsFormat + "/" + t.mipmapCount)));
        Logged("shape", () =>
        {
            foreach (var line in TextureEvent(0))
            {
                // "shape <label> <what GL says of the name>"
                var parts = line.Split(new[] { ' ' }, 3);
                if (parts.Length == 3 && parts[0] == "shape") Value("shape." + parts[1], Q(parts[2]));
                else Value("shape.note", Q(line));
            }
        });

        // The plugin writes through the names: the textures' own texels.
        Logged("shape.write", () =>
        {
            var notes = TextureEvent(2);
            if (notes.Count > 0) Value("shape.write.notes", L(notes));
            Value("shape.cube.written", ElementPixels(cube, 0, 8, 8, false));
            Value("shape.cube.other", ElementPixels(cube, 1, 8, 8, false));
            Value("shape.array.written", ElementPixels(array, 1, 8, 4, false));
            Value("shape.array.other", ElementPixels(array, 0, 8, 4, false));
            Value("shape.volume.written", ElementPixels(volume, 2, 4, 4, true));
            Value("shape.volume.other", ElementPixels(volume, 1, 4, 4, true));
        });

        // A callback while a multisampled target is bound, and what it drew after the resolve.
        Logged("shape.msaa.target", () =>
        {
            using var cmd = new CommandBuffer { name = "nativegfx.msaa" };
            cmd.SetRenderTarget(msaa);
            cmd.ClearRenderTarget(true, true, Color.black);
            cmd.IssuePluginEvent(NativeGfx_TextureEvent(), 3);
            Graphics.ExecuteCommandBuffer(cmd);
            BoundTarget("shape.msaa.target");
            Value("shape.msaa.pixels", Pixels(msaa, 8, 8));
        });
        // What the next callback, one that does nothing, is blamed for.
        Logged("shape.msaa.after", () => TextureEvent(9));
        // An sRGB multisampled target: half grey written with sRGB encoding enabled reads back
        // as half grey when its samples are sRGB texels.
        Logged("shape.msaa.srgb.target", () =>
        {
            using var cmd = new CommandBuffer { name = "nativegfx.msaa.srgb" };
            cmd.SetRenderTarget(msaaSrgb);
            cmd.ClearRenderTarget(true, true, Color.black);
            cmd.IssuePluginEvent(NativeGfx_TextureEvent(), 4);
            Graphics.ExecuteCommandBuffer(cmd);
            BoundTarget("shape.msaa.srgb.target");
            Value("shape.msaa.srgb.pixels", Pixels(msaaSrgb, 8, 8));
        });
        Logged("shape.msaa.srgb.after", () => TextureEvent(9));
        // The same after a buffer that only cleared a multisampled target.
        Logged("shape.msaa.plain", () =>
        {
            using var cmd = new CommandBuffer { name = "nativegfx.msaa.plain" };
            cmd.SetRenderTarget(msaaColor);
            cmd.ClearRenderTarget(true, true, Color.black);
            Graphics.ExecuteCommandBuffer(cmd);
            TextureEvent(9);
        });
    }

    /// <summary>What the texture callback found bound (its event 3): the framebuffer and the
    /// viewport (<paramref name="key"/>), the color and the depth attachment
    /// (<c>.color</c>, <c>.depth</c>).</summary>
    void BoundTarget(string key)
    {
        var buffer = new byte[1 << 14];
        int length = NativeGfx_TextureNotes(buffer, buffer.Length);
        foreach (var line in Encoding.UTF8.GetString(buffer, 0, Math.Min(length, buffer.Length - 1)).Split('\n').Where(s => s.Length > 0))
        {
            int space = line.IndexOf(' ');
            string kind = line.Substring(0, space);
            Value(key + (kind == "bound" ? "" : "." + kind), Q(line.Substring(space + 1)));
        }
    }

    /// <summary>A callback while one image of a cube or an array render texture is the target:
    /// what is attached, and that the plugin draws into that image alone (every image is
    /// cleared first).</summary>
    void LayeredTarget(string key, RenderTexture target, int images, int image)
    {
        bool cube = target.dimension == TextureDimension.Cube;
        Logged("shape." + key + ".target", () =>
        {
            using var cmd = new CommandBuffer { name = "nativegfx." + key };
            for (int i = 0; i <= images; i++)
            {
                // Every image, then the one the callback finds.
                int at = i < images ? i : image;
                cmd.SetRenderTarget(target, 0, cube ? (CubemapFace)at : CubemapFace.Unknown, cube ? 0 : at);
                if (i < images) cmd.ClearRenderTarget(true, true, Color.black);
            }
            cmd.IssuePluginEvent(NativeGfx_TextureEvent(), 3);
            Graphics.ExecuteCommandBuffer(cmd);
            BoundTarget("shape." + key + ".target");
            Value("shape." + key + ".target.drawn", ElementPixels(target, image, target.width, target.height, true));
            Value("shape." + key + ".target.other", ElementPixels(target, image == 0 ? 1 : 0, target.width, target.height, true));
        });
    }

    void ExternalCase(string key, int made, int width, int height, TextureFormat format, bool mipChain, bool linear, params int[] sizes)
    {
        var name = NativeGfx_Made(made);
        Texture2D texture = null;
        Logged("ext." + key, () =>
        {
            texture = Texture2D.CreateExternalTexture(width, height, format, mipChain, linear, name);
            texture.filterMode = FilterMode.Point;
            Keep(texture);
            Value("ext." + key + ".ptr", B(name != IntPtr.Zero && texture.GetNativeTexturePtr() == name));
            Value("ext." + key + ".size", Q(texture.width + "x" + texture.height + " " + texture.format + " " + texture.graphicsFormat + " " + texture.mipmapCount));
            foreach (int size in sizes) Value("ext." + key + ".pixels." + size, Pixels(texture, size, size));
        });
    }

    void ExternalFormats()
    {
        LayeredTarget("cube", layeredCube, 6, (int)CubemapFace.PositiveY);
        LayeredTarget("array", layeredArray, 3, 1);
        Logged("ext.make", () =>
        {
            var notes = TextureEvent(1);
            if (notes.Count > 0) Value("ext.make.notes", L(notes));
        });
        // Mip levels of one color each: drawn at half and quarter size, a texture with a mip
        // chain shows its smaller levels.
        ExternalCase("mips", 0, 8, 8, TextureFormat.RGBA32, true, true, 8, 4, 2, 1);
        ExternalCase("mips.nochain", 1, 8, 8, TextureFormat.RGBA32, false, true, 8, 4, 2, 1);
        // sRGB texels, declared linear and not; 8-bit texels declared sRGB.
        ExternalCase("srgb.linear", 2, 4, 4, TextureFormat.RGBA32, false, true, 4);
        ExternalCase("srgb", 3, 4, 4, TextureFormat.RGBA32, false, false, 4);
        ExternalCase("rgba.srgb", 4, 4, 4, TextureFormat.RGBA32, false, false, 4);
        // Other formats, declared as they are.
        ExternalCase("r8", 5, 4, 4, TextureFormat.R8, false, true, 4);
        ExternalCase("rg8", 6, 4, 4, TextureFormat.RG16, false, true, 4);
        ExternalCase("rgb8", 7, 4, 4, TextureFormat.RGB24, false, true, 4);
        ExternalCase("half", 8, 4, 4, TextureFormat.RGBAHalf, false, true, 4);
        ExternalCase("float", 9, 4, 4, TextureFormat.RGBAFloat, false, true, 4);
        ExternalCase("dxt5", 10, 4, 4, TextureFormat.DXT5, false, true, 4);
        // One channel declared as four.
        ExternalCase("r8.rgba", 11, 4, 4, TextureFormat.RGBA32, false, true, 4);
        // Texels of an unsized format (GL_RGBA).
        ExternalCase("unsized", 12, 4, 4, TextureFormat.RGBA32, false, true, 4);
        // The plugin's textures after Unity used them.
        string[] labels = { "mips", "mips.nochain", "srgb.linear", "srgb", "rgba.srgb", "r8", "rg8", "rgb8", "half", "float", "dxt5", "r8.rgba", "unsized" };
        shapes = 0;
        for (int i = 0; i < 32; i++) NativeGfx_SetShape(i, "", IntPtr.Zero);
        for (int i = 0; i < labels.Length; i++) Shape(labels[i], NativeGfx_Made(i));
        Logged("ext.shape", () =>
        {
            foreach (var line in TextureEvent(0))
            {
                var parts = line.Split(new[] { ' ' }, 3);
                if (parts.Length == 3 && parts[0] == "shape") Value("ext." + parts[1] + ".shape", Q(parts[2]));
            }
        });
    }
}
