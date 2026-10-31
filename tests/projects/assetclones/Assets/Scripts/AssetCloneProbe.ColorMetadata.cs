using System;
using System.IO;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using Object = UnityEngine.Object;

public partial class AssetCloneProbe
{
    void ColorPair(string key, Texture2D original)
    {
        original.SetPixels(new[]{Color.white,Color.white,Color.white,Color.white}); original.Apply(false);
        V(key+".original.graphics",original.graphicsFormat); V(key+".original.data_srgb",original.isDataSRGB); V(key+".original.format",original.format);
        Texture2D clone=null; Try(key+".instantiate",()=>clone=Object.Instantiate(original));
        if(clone!=null) { V(key+".clone.graphics",clone.graphicsFormat); V(key+".clone.data_srgb",clone.isDataSRGB); V(key+".clone.format",clone.format); Object.DestroyImmediate(clone); }
        Object.DestroyImmediate(original);
    }
    void ColorMetadata()
    {
        V("profile",QualitySettings.activeColorSpace);
        foreach(var format in new[]{TextureFormat.RGBA32,TextureFormat.RGB24,TextureFormat.Alpha8,TextureFormat.RGBAHalf,TextureFormat.RFloat})
        foreach(bool linear in new[]{false,true}) ColorPair("format."+format+"."+linear,new Texture2D(2,2,format,false,linear));
        foreach(var format in new[]{GraphicsFormat.R8G8B8A8_SRGB,GraphicsFormat.R8G8B8A8_UNorm,GraphicsFormat.R16G16B16A16_SFloat,GraphicsFormat.R32_SFloat})
            ColorPair("graphics."+format,new Texture2D(2,2,format,TextureCreationFlags.None));
        foreach(var name in new[]{"Readable","Unreadable"})
        {
            var t=Resources.Load<Texture2D>(name); V("import."+name+".graphics",t.graphicsFormat); V("import."+name+".data_srgb",t.isDataSRGB);
            Try("import."+name+".instantiate",()=>{var c=Object.Instantiate(t);V("import."+name+".clone.graphics",c.graphicsFormat);V("import."+name+".clone.data_srgb",c.isDataSRGB);Object.DestroyImmediate(c);});
        }
        var path=Environment.GetEnvironmentVariable("ASSET_CLONE_OUT");Directory.CreateDirectory(Path.GetDirectoryName(path));File.WriteAllText(path,output.ToString());
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(0);
#else
        Application.Quit();
#endif
    }
}
