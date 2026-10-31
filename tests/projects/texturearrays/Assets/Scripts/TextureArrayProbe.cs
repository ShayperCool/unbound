using System;
using System.Globalization;
using System.IO;
using System.Text;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Records what layered textures give a game: each texture's type, size, depth, mip count,
/// formats, dimension, readability and sampler state, and for every slice (array element, cube
/// face) and mip level <c>GetPixels32</c> (RGBA bytes bottom row first), <c>GetPixels</c>
/// (little-endian 32-bit floats) and <c>GetPixelData&lt;byte&gt;</c> (the stored bytes), base64.
/// A failing call records its exception instead. <see cref="samples"/> also draws slices through
/// the GPU (point-sampled at texel centres, also outside the volume to see wrapW) and reads them
/// back. Writes <c>TEXTURE_ARRAY_PROBE_OUT</c> (one {"k","v"} JSON line per value) and quits, with
/// exit code 1 when the probe itself fails.
/// </summary>
public class TextureArrayProbe : MonoBehaviour
{
    public Texture[] textures;
    public Material arrayMaterial;
    public Material volumeMaterial;
    /// <summary>"name slice": draws slice (array element, or volume depth in texels: slice + 0.5
    /// is sampled, so slices beyond the depth show the W wrap mode).</summary>
    public string[] samples;

    readonly StringBuilder m_Out = new StringBuilder();

    void Add(string key, string value)
    {
        m_Out.Append("{\"k\":\"").Append(key).Append("\",\"v\":\"").Append(value.Replace("\\", "\\\\").Replace("\"", "\\\"")).Append("\"}\n");
    }

    static string Floats(Color[] colors)
    {
        var bytes = new byte[colors.Length * 16];
        for (int i = 0; i < colors.Length; i++)
            for (int c = 0; c < 4; c++)
                BitConverter.GetBytes(colors[i][c]).CopyTo(bytes, i * 16 + c * 4);
        return Convert.ToBase64String(bytes);
    }

    static string Bytes(Color32[] pixels)
    {
        var bytes = new byte[pixels.Length * 4];
        for (int i = 0; i < pixels.Length; i++)
        {
            bytes[i * 4] = pixels[i].r;
            bytes[i * 4 + 1] = pixels[i].g;
            bytes[i * 4 + 2] = pixels[i].b;
            bytes[i * 4 + 3] = pixels[i].a;
        }
        return Convert.ToBase64String(bytes);
    }

    static string Raw(NativeArray<byte> data) => Convert.ToBase64String(data.ToArray());

    void Value(string key, string size, Func<string> read)
    {
        string value;
        try { value = size + ":" + read(); }
        catch (Exception e) { value = "error " + e.GetType().Name + ": " + e.Message; }
        Add(key, value);
    }

    static int Depth(Texture t) => t switch
    {
        Texture2DArray a => a.depth,
        Texture3D v => v.depth,
        CubemapArray c => c.cubemapCount,
        _ => 1,
    };

    static TextureFormat? Format(Texture t) => t switch
    {
        Texture2D a => a.format,
        Texture2DArray a => a.format,
        Texture3D a => a.format,
        Cubemap a => a.format,
        CubemapArray a => a.format,
        _ => null,
    };

    void Record(Texture t)
    {
        int depth = Depth(t);
        Add(t.name + ".info", string.Format(CultureInfo.InvariantCulture,
            "{0} {1}x{2}x{3} mips {4} {5} {6} dim {7} readable {8} filter {9} wrap {10},{11},{12} aniso {13} bias {14}",
            t.GetType().Name, t.width, t.height, depth, t.mipmapCount, Format(t), t.graphicsFormat, t.dimension,
            t.isReadable, t.filterMode, t.wrapModeU, t.wrapModeV, t.wrapModeW, t.anisoLevel, t.mipMapBias));
        for (int mip = 0; mip < t.mipmapCount; mip++)
        {
            int w = Math.Max(1, t.width >> mip), h = Math.Max(1, t.height >> mip);
            string size = w + "x" + h;
            int m = mip;
            switch (t)
            {
                case Texture2DArray a:
                    for (int e = 0; e < depth; e++)
                    {
                        int el = e;
                        string key = t.name + ".e" + e + ".mip" + mip;
                        Value(key + ".px32", size, () => Bytes(a.GetPixels32(el, m)));
                        Value(key + ".px", size, () => Floats(a.GetPixels(el, m)));
                        Value(key + ".raw", size, () => Raw(a.GetPixelData<byte>(m, el)));
                    }
                    break;
                case Texture3D v:
                {
                    string key = t.name + ".mip" + mip;
                    string volume = size + "x" + Math.Max(1, depth >> mip);
                    Value(key + ".px32", volume, () => Bytes(v.GetPixels32(m)));
                    Value(key + ".px", volume, () => Floats(v.GetPixels(m)));
                    Value(key + ".raw", volume, () => Raw(v.GetPixelData<byte>(m)));
                    break;
                }
                case Cubemap c:
                    for (int f = 0; f < 6; f++)
                    {
                        var face = (CubemapFace)f;
                        string key = t.name + ".f" + f + ".mip" + mip;
                        Value(key + ".px", size, () => Floats(c.GetPixels(face, m)));
                        Value(key + ".raw", size, () => Raw(c.GetPixelData<byte>(m, face)));
                    }
                    break;
                case CubemapArray c:
                    for (int e = 0; e < depth; e++)
                        for (int f = 0; f < 6; f++)
                        {
                            var face = (CubemapFace)f;
                            int el = e;
                            string key = t.name + ".e" + e + ".f" + f + ".mip" + mip;
                            Value(key + ".px32", size, () => Bytes(c.GetPixels32(face, el, m)));
                            Value(key + ".px", size, () => Floats(c.GetPixels(face, el, m)));
                            Value(key + ".raw", size, () => Raw(c.GetPixelData<byte>(m, face, el)));
                        }
                    break;
                case Texture2D d:
                {
                    string key = t.name + ".mip" + mip;
                    Value(key + ".px32", size, () => Bytes(d.GetPixels32(m)));
                    Value(key + ".px", size, () => Floats(d.GetPixels(m)));
                    Value(key + ".raw", size, () => Raw(d.GetPixelData<byte>(m)));
                    break;
                }
            }
        }
    }

    void Sample(string spec)
    {
        var parts = spec.Split(' ');
        var t = Array.Find(textures, x => x.name == parts[0]);
        int slice = int.Parse(parts[1], CultureInfo.InvariantCulture);
        string key = parts[0] + ".sample" + slice;
        Value(key, t.width + "x" + t.height, () =>
        {
            Material material;
            if (t is Texture3D v)
            {
                material = volumeMaterial;
                material.SetTexture("_Vol", t);
                material.SetFloat("_Z", (slice + 0.5f) / v.depth);
            }
            else
            {
                material = arrayMaterial;
                material.SetTexture("_Arr", t);
                material.SetFloat("_Slice", slice);
            }
            var rt = RenderTexture.GetTemporary(t.width, t.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Default);
            var previous = RenderTexture.active;
            try
            {
                Graphics.Blit(null, rt, material);
                RenderTexture.active = rt;
                var read = new Texture2D(t.width, t.height, TextureFormat.RGBA32, false);
                read.ReadPixels(new Rect(0, 0, t.width, t.height), 0, 0, false);
                read.Apply(false);
                var result = Bytes(read.GetPixels32());
                Destroy(read);
                return result;
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(rt);
            }
        });
    }

    void Start()
    {
        int code = 0;
        try
        {
            foreach (var t in textures) Record(t);
            foreach (var s in samples) Sample(s);
            var path = Environment.GetEnvironmentVariable("TEXTURE_ARRAY_PROBE_OUT");
            if (!string.IsNullOrEmpty(path))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
                File.WriteAllText(path, m_Out.ToString());
            }
            else
            {
                Debug.Log(m_Out.ToString());
            }
        }
        catch (Exception e)
        {
            Debug.LogError("texture array probe failed: " + e);
            code = 1;
        }
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(code);
#else
        Application.Quit(code);
#endif
    }
}
