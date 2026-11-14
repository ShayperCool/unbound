using System;
using System.Linq;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Rendering;

static partial class ExternalControls
{
    [DllImport("NativeGfxVulkan")] static extern int NativeGfxVk_SameImage(IntPtr first, IntPtr second);
    [DllImport("NativeGfxVulkan")] static extern void NativeGfxVk_SetTexture(int index, IntPtr pointer);
    [DllImport("NativeGfxVulkan")] static extern int NativeGfxVk_EventBase();
    [DllImport("NativeGfxVulkan")] static extern void NativeGfxVk_ExternalPrepare(int kind);
    [DllImport("NativeGfxVulkan")] static extern IntPtr NativeGfxVk_ExternalEvent();
    [DllImport("NativeGfxVulkan")] static extern IntPtr NativeGfxVk_ExternalPtr(int kind);
    [DllImport("NativeGfxVulkan")] static extern void NativeGfxVk_ExternalDestroy();
    [DllImport("NativeGfxProbe")] static extern IntPtr NativeGfx_ExternalOwnedEvent();
    [DllImport("NativeGfxProbe")] static extern IntPtr NativeGfx_ExternalOwnedPtr(int kind);
    static string Q(string s) => "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "") + "\"";
    static string B(bool value) => value ? "true" : "false";
    static string Try(Func<int> call)
    {
        try { return Q("ok:" + call()); }
        catch (Exception e) { return Q(e.GetType().Name + ":" + e.Message); }
    }
    static string Pixels(Texture source, int element, int width, int height, bool region = false, int mip = 0)
    {
        var previous = RenderTexture.active;
        var target = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear); target.Create();
        RenderTexture.active = target;
        GL.Clear(false, true, new Color(.125f, .25f, .5f, .75f));
        if (region) Graphics.CopyTexture(source, element, mip, 0, 0, width, height, target, 0, 0, 0, 0);
        else Graphics.CopyTexture(source, element, mip, target, 0, 0);
        var cpu = new Texture2D(width, height, TextureFormat.RGBA32, false, true);
        RenderTexture.active = target; cpu.ReadPixels(new Rect(0, 0, width, height), 0, 0); cpu.Apply(false);
        RenderTexture.active = previous;
        var result = Q(string.Join(",", cpu.GetPixels32().Select(c => c.r.ToString("x2") + c.g.ToString("x2") + c.b.ToString("x2") + c.a.ToString("x2"))));
        UnityEngine.Object.DestroyImmediate(cpu); UnityEngine.Object.DestroyImmediate(target);
        return result;
    }
    static string CubeSample(Cubemap cube, int mip)
    {
        var material = new Material(Shader.Find("Probe/NativeGfx/ExternalCube"));
        material.SetTexture("_Cube", cube); material.SetFloat("_Level", mip);
        var target = new RenderTexture(1, 1, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear); target.Create();
        Graphics.Blit(null, target, material);
        string result = Pixels(target, 0, 1, 1);
        UnityEngine.Object.DestroyImmediate(material); UnityEngine.Object.DestroyImmediate(target);
        return result;
    }
    static string View(Texture texture, int kind, bool metadata, int level = 0, bool dimensions = false)
    {
        var previous = RenderTexture.active;
        var material = new Material(Shader.Find("Probe/NativeGfx/ExternalView"));
        material.SetTexture(kind == 0 ? "_Image" : kind == 1 ? "_Cube" : "_Volume", texture);
        material.SetFloat("_Kind",kind);material.SetFloat("_Meta",dimensions?2:metadata?1:0);material.SetFloat("_Level",level);
        var target = new RenderTexture(1,1,0,RenderTextureFormat.ARGBFloat,RenderTextureReadWrite.Linear);target.Create();
        Graphics.Blit(null,target,material);
        var cpu = new Texture2D(1,1,TextureFormat.RGBAFloat,false,true);
        RenderTexture.active=target;cpu.ReadPixels(new Rect(0,0,1,1),0,0);
        var color=cpu.GetPixel(0,0);
        var culture=System.Globalization.CultureInfo.InvariantCulture;
        string result=Q(color.r.ToString("G9",culture)+","+color.g.ToString("G9",culture)+","+color.b.ToString("G9",culture)+","+color.a.ToString("G9",culture));
        RenderTexture.active=previous;
        UnityEngine.Object.DestroyImmediate(cpu);UnityEngine.Object.DestroyImmediate(target);UnityEngine.Object.DestroyImmediate(material);
        return result;
    }
    static void MipInfo(Action<string,string> row)
    {
        var source=new Texture2D(4,4,TextureFormat.RGBA32,true,true);
        for(int level=0;level<source.mipmapCount;level++)
        {
            int size=Math.Max(1,4>>level);
            source.SetPixels(Enumerable.Repeat(new Color(.125f+.25f*level,.25f,.5f,1),size*size).ToArray(),level);
        }
        source.Apply(false);row("externalShapes.twoD.source.info",View(source,0,true));row("externalShapes.twoD.source.mip1",View(source,0,false,1));
        UnityEngine.Object.DestroyImmediate(source);
    }
    static void Pointer(Action<string,string> row, string key, Texture external, IntPtr source)
    {
        var pointer = external.GetNativeTexturePtr();
        row(key + ".pointerSame", B(pointer == source));
        if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Vulkan)
            row(key + ".imageSame", B(NativeGfxVk_SameImage(pointer, source) != 0));
    }
    static Texture Filled(int kind, Color color)
    {
        if (kind == 0)
        {
            var texture = new Texture2D(7, 3, TextureFormat.RGBA32, false, true);
            texture.SetPixels(Enumerable.Repeat(color,21).ToArray()); texture.Apply(false); return texture;
        }
        if (kind == 1)
        {
            var texture = new Cubemap(8, TextureFormat.RGBA32, false);
            for(int face=0;face<6;face++) texture.SetPixels(Enumerable.Repeat(color,64).ToArray(),(CubemapFace)face);
            texture.Apply(false); return texture;
        }
        var volume = new Texture3D(7, 3, 2, TextureFormat.RGBA32, false);
        volume.SetPixels(Enumerable.Repeat(color,42).ToArray()); volume.Apply(false); return volume;
    }
    static void CopyBounds(Action<string,string> row)
    {
        for(int kind=0;kind<3;kind++)
        {
            var owner = Filled(kind, new Color32(111,73,199,255));
            var pointer = owner.GetNativeTexturePtr();
            Texture wrapper = kind == 0 ? Texture2D.CreateExternalTexture(7,3,TextureFormat.RGBA32,false,true,pointer)
                : kind == 1 ? (Texture)Cubemap.CreateExternalTexture(8,TextureFormat.RGBA32,false,pointer)
                : Texture3D.CreateExternalTexture(7,3,2,TextureFormat.RGBA32,false,pointer);
            int element = kind == 1 ? 3 : kind == 2 ? 1 : 0;
            int width = kind == 1 ? 8 : 7, height = kind == 1 ? 8 : 3;
            row("externalBounds."+kind+".whole",Pixels(wrapper,element,width,height));
            row("externalBounds."+kind+".region",Pixels(wrapper,element,width,height,true));
            var source = Filled(kind, Color.green);
            Graphics.CopyTexture(source,element,0,wrapper,element,0);
            row("externalBounds."+kind+".destination",Pixels(owner,element,width,height));
            UnityEngine.Object.DestroyImmediate(wrapper);UnityEngine.Object.DestroyImmediate(owner);UnityEngine.Object.DestroyImmediate(source);
        }
    }
    static void ExternalMips(Action<string,string> row)
    {
        for(int kind=0;kind<3;kind++)
        {
            Texture source = kind == 0 ? new Texture2D(8,8,TextureFormat.RGBA32,true,true)
                : kind == 1 ? (Texture)new Cubemap(8,TextureFormat.RGBA32,true)
                : new Texture3D(8,4,2,TextureFormat.RGBA32,true);
            for(int mip=0;mip<source.mipmapCount;mip++)
            {
                var color = (Color)new Color32((byte)(20+50*mip),71,91,255);
                int width=Math.Max(1,8>>mip), height=Math.Max(1,(kind==2?4:8)>>mip);
                if(source is Texture2D image) image.SetPixels(Enumerable.Repeat(color,width*height).ToArray(),mip);
                else if(source is Cubemap cube)
                { for(int face=0;face<6;face++) cube.SetPixels(Enumerable.Repeat(color,width*height).ToArray(),(CubemapFace)face,mip); }
                else ((Texture3D)source).SetPixels(Enumerable.Repeat(color,width*height*Math.Max(1,2>>mip)).ToArray(),mip);
            }
            if(source is Texture2D twoD) twoD.Apply(false);
            else if(source is Cubemap cube) cube.Apply(false);
            else ((Texture3D)source).Apply(false);
            row("externalMips."+kind+".source1",View(source,kind,false,1));
            row("externalMips."+kind+".sourceDimensions",View(source,kind,false,0,true));
            IntPtr pointer=source.GetNativeTexturePtr();
            Texture wrapper=kind==0?Texture2D.CreateExternalTexture(8,8,TextureFormat.RGBA32,true,true,pointer)
                :kind==1?(Texture)Cubemap.CreateExternalTexture(8,TextureFormat.RGBA32,true,pointer)
                :Texture3D.CreateExternalTexture(8,4,2,TextureFormat.RGBA32,true,pointer);
            row("externalMips."+kind+".sample0",View(wrapper,kind,false,0));
            row("externalMips."+kind+".sample1",View(wrapper,kind,false,1));
            int element=kind==1?3:0;
            row("externalMips."+kind+".whole1",Pixels(wrapper,element,4,kind==2?2:4,false,1));
            row("externalMips."+kind+".region1",Pixels(wrapper,element,4,kind==2?2:4,true,1));
            row("externalMips."+kind+".dimensions",View(wrapper,kind,false,0,true));
            UnityEngine.Object.DestroyImmediate(wrapper);UnityEngine.Object.DestroyImmediate(source);
        }
    }
    static void ActiveReleaseControls(Action<string,string> row)
    {
        var previous = RenderTexture.active;
        foreach (string kind in new[] { "uncreated", "inactive", "active", "release" })
        {
            var messages = new System.Collections.Generic.List<string>();
            Application.LogCallback collect = (message, stack, type) => messages.Add(type + ": " + message);
            var texture = new RenderTexture(7, 3, 0);
            RenderTexture.active = null;
            Application.logMessageReceived += collect;
            try
            {
                if (kind != "uncreated") texture.Create();
                if (kind == "active" || kind == "release") RenderTexture.active = texture;
                row("activeRelease." + kind + ".created", B(texture.IsCreated()));
                row("activeRelease." + kind + ".activeBefore", B(RenderTexture.active == texture));
                if (kind == "release") texture.Release();
                else UnityEngine.Object.DestroyImmediate(texture);
                row("activeRelease." + kind + ".activeAfter", B(RenderTexture.active == null));
                row("activeRelease." + kind + ".logs", "[" + string.Join(",", messages.Select(Q)) + "]");
            }
            finally
            {
                Application.logMessageReceived -= collect;
                RenderTexture.active = null;
                if (texture != null) UnityEngine.Object.DestroyImmediate(texture);
            }
        }
        RenderTexture.active = previous != null ? previous : null;
    }
    public static void Run(Action<string,string> row, Action<Action> isolateLogs = null)
    {
        row("externalShapes.cube.zero", Try(() => Cubemap.CreateExternalTexture(4, TextureFormat.RGBA32, false, IntPtr.Zero) == null ? 0 : 1));
        row("externalShapes.volume.zero", Try(() => Texture3D.CreateExternalTexture(4, 2, 2, TextureFormat.RGBA32, false, IntPtr.Zero) == null ? 0 : 1));
        var cube = new Cubemap(4, TextureFormat.RGBA32, true);
        for (int face = 0; face < 6; face++)
        for (int mip = 0; mip < cube.mipmapCount; mip++)
        {
            int size = Math.Max(1, 4 >> mip);
            cube.SetPixels(Enumerable.Repeat(new Color32((byte)(20 + 30 * face), (byte)(30 + 40 * mip), 60, 255), size * size).Select(c => (Color)c).ToArray(), (CubemapFace)face, mip);
        }
        cube.Apply(false);
        row("externalShapes.cube.source.sample.0", CubeSample(cube, 0));
        row("externalShapes.cube.source.sample.1", CubeSample(cube, 1));
        IntPtr nativeCube = cube.GetNativeTexturePtr();
        var externalCube = Cubemap.CreateExternalTexture(4, TextureFormat.RGBA32, true, nativeCube); externalCube.name = "external.cube";
        row("externalShapes.cube.info",View(externalCube,1,true));
        row("externalShapes.cube.meta", Q(externalCube.width + "x" + externalCube.height + ":" + externalCube.mipmapCount + ":" + externalCube.format + ":" + externalCube.graphicsFormat));
        row("externalShapes.cube.readable", B(externalCube.isReadable));
        row("externalShapes.cube.cpu", Try(() => externalCube.GetPixels(CubemapFace.PositiveX).Length));
        Pointer(row, "externalShapes.cube", externalCube, nativeCube);
        foreach (int face in new[] { 0, 3, 5 }) row("externalShapes.cube.face." + face, Pixels(externalCube, face, 4, 4));
        row("externalShapes.cube.sample.0", CubeSample(externalCube, 0));
        row("externalShapes.cube.sample.1", CubeSample(externalCube, 1));
        var singleCube = new Cubemap(4, TextureFormat.RGBA32, false);
        for (int face = 0; face < 6; face++) singleCube.SetPixels(Enumerable.Repeat(Color.yellow, 16).ToArray(), (CubemapFace)face);
        singleCube.Apply(false);
        var singleExternal = Cubemap.CreateExternalTexture(4, TextureFormat.RGBA32, false, singleCube.GetNativeTexturePtr());
        row("externalShapes.cube.single.meta", Q(singleExternal.width + ":" + singleExternal.mipmapCount));
        row("externalShapes.cube.single.copy", Pixels(singleExternal, 3, 4, 4));
        row("externalShapes.cube.single.sample", CubeSample(singleExternal, 0));
        UnityEngine.Object.DestroyImmediate(singleExternal); UnityEngine.Object.DestroyImmediate(singleCube);
        var volume = new Texture3D(4, 2, 2, TextureFormat.RGBA32, false);
        volume.SetPixels32(Enumerable.Range(0, 16).Select(i => new Color32((byte)(10 + 8 * i), 90, 130, 255)).ToArray()); volume.Apply(false);
        var twoD = new Texture2D(4, 2, TextureFormat.RGBA32, false, true);
        twoD.SetPixels(Enumerable.Repeat(Color.green, 8).ToArray()); twoD.Apply(false);
        var externalTwoD = Texture2D.CreateExternalTexture(4, 2, TextureFormat.RGBA32, false, true, twoD.GetNativeTexturePtr()); externalTwoD.name = "external.twoD";
        row("externalShapes.twoD.readable", B(externalTwoD.isReadable));
        row("externalShapes.twoD.cpu", Try(() => externalTwoD.GetPixels().Length));
        row("externalShapes.twoD.gpu", Pixels(externalTwoD, 0, 4, 2));
        UnityEngine.Object.DestroyImmediate(externalTwoD); UnityEngine.Object.DestroyImmediate(twoD);
        IntPtr nativeVolume = volume.GetNativeTexturePtr();
        var externalVolume = Texture3D.CreateExternalTexture(4, 2, 2, TextureFormat.RGBA32, false, nativeVolume); externalVolume.name = "external.volume";
        row("externalShapes.volume.info",View(externalVolume,2,true));
        row("externalShapes.volume.meta", Q(externalVolume.width + "x" + externalVolume.height + "x" + externalVolume.depth + ":" + externalVolume.mipmapCount + ":" + externalVolume.format + ":" + externalVolume.graphicsFormat));
        row("externalShapes.volume.readable", B(externalVolume.isReadable));
        row("externalShapes.volume.cpu", Try(() => externalVolume.GetPixels().Length));
        Pointer(row, "externalShapes.volume", externalVolume, nativeVolume);
        foreach (int slice in new[] { 0, 1 }) row("externalShapes.volume.slice." + slice, Pixels(externalVolume, slice, 4, 2));
        row("externalShapes.cube.sampler", Q(externalCube.filterMode + ":" + externalCube.wrapMode + ":" + externalCube.anisoLevel));
        row("externalShapes.volume.sampler", Q(externalVolume.filterMode + ":" + externalVolume.wrapMode + ":" + externalVolume.anisoLevel));
        var cubeNext = new Cubemap(4, TextureFormat.RGBA32, true);
        for (int face = 0; face < 6; face++)
        for (int mip = 0; mip < cubeNext.mipmapCount; mip++)
        {
            int size = Math.Max(1, 4 >> mip);
            cubeNext.SetPixels(Enumerable.Repeat(Color.cyan, size * size).ToArray(), (CubemapFace)face, mip);
        }
        cubeNext.Apply(false);
        externalCube.UpdateExternalTexture(cubeNext.GetNativeTexturePtr());
        Pointer(row, "externalShapes.cube.updated", externalCube, cubeNext.GetNativeTexturePtr());
        row("externalShapes.cube.updated.face", Pixels(externalCube, 3, 4, 4));
        var volumeNext = new Texture3D(4, 2, 2, TextureFormat.RGBA32, false);
        volumeNext.SetPixels(Enumerable.Repeat(Color.magenta, 16).ToArray()); volumeNext.Apply(false);
        externalVolume.UpdateExternalTexture(volumeNext.GetNativeTexturePtr());
        Pointer(row, "externalShapes.volume.updated", externalVolume, volumeNext.GetNativeTexturePtr());
        row("externalShapes.volume.updated.slice", Pixels(externalVolume, 1, 4, 2));
        externalCube.UpdateExternalTexture(nativeCube);
        externalVolume.UpdateExternalTexture(nativeVolume);
        UnityEngine.Object.DestroyImmediate(cubeNext); UnityEngine.Object.DestroyImmediate(volumeNext);
        // Destroying an external wrapper must leave the source's storage alive.
        UnityEngine.Object.DestroyImmediate(externalCube); UnityEngine.Object.DestroyImmediate(externalVolume);
        row("externalShapes.cube.ownerAfter", Pixels(cube, 3, 4, 4));
        row("externalShapes.volume.ownerAfter", Pixels(volume, 1, 4, 2));
        if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Vulkan)
        {
            NativeGfxVk_SetTexture(0, volume.GetNativeTexturePtr());
            for (int kind = 0; kind < 3; kind++)
            {
                NativeGfxVk_ExternalPrepare(kind);
                GL.IssuePluginEvent(NativeGfxVk_ExternalEvent(), NativeGfxVk_EventBase() + 1);
                // A synchronous read waits for the creation callback and its writes.
                Pixels(volume, 0, 4, 2);
                var pointer = NativeGfxVk_ExternalPtr(kind);
                row("externalOwned." + kind + ".created", B(pointer != IntPtr.Zero));
                if (pointer == IntPtr.Zero) continue;
                Texture wrapper = kind == 0 ? Texture2D.CreateExternalTexture(4, 2, TextureFormat.RGBA32, false, true, pointer)
                    : kind == 1 ? (Texture)Cubemap.CreateExternalTexture(4, TextureFormat.RGBA32, false, pointer)
                    : Texture3D.CreateExternalTexture(4, 2, 2, TextureFormat.RGBA32, false, pointer);
                Pointer(row, "externalOwned." + kind, wrapper, pointer);
                row("externalOwned." + kind + ".pixels", Pixels(wrapper, kind == 0 ? 0 : kind == 1 ? 3 : 1, 4, kind == 1 ? 4 : 2));
                row("externalOwned." + kind + ".sample", View(wrapper, kind, false));
                UnityEngine.Object.DestroyImmediate(wrapper);
                Texture again = kind == 0 ? Texture2D.CreateExternalTexture(4, 2, TextureFormat.RGBA32, false, true, pointer)
                    : kind == 1 ? (Texture)Cubemap.CreateExternalTexture(4, TextureFormat.RGBA32, false, pointer)
                    : Texture3D.CreateExternalTexture(4, 2, 2, TextureFormat.RGBA32, false, pointer);
                row("externalOwned." + kind + ".after", View(again, kind, false));
                UnityEngine.Object.DestroyImmediate(again);
            }
            NativeGfxVk_ExternalDestroy();
        }
        if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.OpenGLCore)
        {
            GL.IssuePluginEvent(NativeGfx_ExternalOwnedEvent(), 1);
            Pixels(volume, 0, 4, 2);
            for (int kind = 0; kind < 2; kind++)
            {
                IntPtr pointer = NativeGfx_ExternalOwnedPtr(kind);
                row("externalOwnedGL." + kind + ".created", B(pointer != IntPtr.Zero));
                if (pointer == IntPtr.Zero) continue;
                Texture wrapper = kind == 0 ? (Texture)Cubemap.CreateExternalTexture(4, TextureFormat.RGBA32, false, pointer)
                    : Texture3D.CreateExternalTexture(4, 2, 2, TextureFormat.RGBA32, false, pointer);
                Pointer(row, "externalOwnedGL." + kind, wrapper, pointer);
                row("externalOwnedGL." + kind + ".pixels", Pixels(wrapper, kind == 0 ? 3 : 1, 4, kind == 0 ? 4 : 2));
                row("externalOwnedGL." + kind + ".sample", View(wrapper, kind == 0 ? 1 : 2, false));
                UnityEngine.Object.DestroyImmediate(wrapper);
                Texture again = kind == 0 ? (Texture)Cubemap.CreateExternalTexture(4, TextureFormat.RGBA32, false, pointer)
                    : Texture3D.CreateExternalTexture(4, 2, 2, TextureFormat.RGBA32, false, pointer);
                row("externalOwnedGL." + kind + ".after", View(again, kind == 0 ? 1 : 2, false));
                UnityEngine.Object.DestroyImmediate(again);
            }
            GL.IssuePluginEvent(NativeGfx_ExternalOwnedEvent(), 2);
            Pixels(volume, 0, 4, 2);
        }
        if (isolateLogs == null) ActiveReleaseControls(row);
        else isolateLogs(() => ActiveReleaseControls(row));
        MipInfo(row);
        CopyBounds(row);
        ExternalMips(row);
        if (isolateLogs == null) ExtendedControls(row);
        else isolateLogs(() => ExtendedControls(row));
        UnityEngine.Object.DestroyImmediate(cube); UnityEngine.Object.DestroyImmediate(volume);
    }
}
