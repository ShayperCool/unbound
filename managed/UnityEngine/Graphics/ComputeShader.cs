using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.RegularExpressions;
using Unbound.Runtime;
using UnityEngine.Rendering;

namespace UnityEngine
{
    /// <summary>
    /// A <c>.compute</c> asset: its kernels compile from its source on the native side when first
    /// dispatched. Values set here are the shader's (all kernels); textures and buffers per kernel.
    /// </summary>
    [ClassId(72)]
    public sealed partial class ComputeShader : Object
    {
        /// <summary>The file, as the engine's working directory sees it.</summary>
        internal string m_File;
        internal string[] m_Kernels = Array.Empty<string>();
        readonly Dictionary<int, object> m_Values = new Dictionary<int, object>();
        readonly Dictionary<(int Kernel, int Name), object> m_Bound = new Dictionary<(int, int), object>();
        readonly HashSet<string> m_Keywords = new HashSet<string>();
        readonly Dictionary<(int, string), (uint, uint, uint)> m_Threads = new Dictionary<(int, string), (uint, uint, uint)>();

        ComputeShader() { }

        static readonly Regex s_Kernel = new Regex(@"^[ \t]*#[ \t]*pragma[ \t]+kernel[ \t]+(\w+)", RegexOptions.Multiline);

        internal static ComputeShader FromFile(string file, string name, string source)
        {
            ComputeShader shader;
            using (ObjectRegistry.AsAssets())
                shader = new ComputeShader { m_Name = name, m_File = file, IsPersistentAsset = true };
            var kernels = new List<string>();
            foreach (Match m in s_Kernel.Matches(source))
                if (!kernels.Contains(m.Groups[1].Value)) kernels.Add(m.Groups[1].Value);
            shader.m_Kernels = kernels.ToArray();
            return shader;
        }

        public int FindKernel(string name)
        {
            int i = Array.IndexOf(m_Kernels, name);
            if (i < 0) throw new ArgumentException($"Kernel '{name}' not found.");
            return i;
        }

        public bool HasKernel(string name) => Array.IndexOf(m_Kernels, name) >= 0;

        public bool IsSupported(int kernelIndex) => kernelIndex >= 0 && kernelIndex < m_Kernels.Length;

        string Kernel(int index)
        {
            if (index < 0 || index >= m_Kernels.Length)
                throw new ArgumentException($"Kernel index ({index}) out of range");
            return m_Kernels[index];
        }

        // ------------------------------------------------------------ values

        public void SetFloat(int nameID, float val) => m_Values[nameID] = val;
        public void SetFloat(string name, float val) => SetFloat(Shader.PropertyToID(name), val);
        public void SetInt(int nameID, int val) => m_Values[nameID] = val;
        public void SetInt(string name, int val) => SetInt(Shader.PropertyToID(name), val);
        public void SetBool(int nameID, bool val) => m_Values[nameID] = val ? 1 : 0;
        public void SetBool(string name, bool val) => SetBool(Shader.PropertyToID(name), val);
        public void SetVector(int nameID, Vector4 val) => m_Values[nameID] = val;
        public void SetVector(string name, Vector4 val) => SetVector(Shader.PropertyToID(name), val);
        public void SetMatrix(int nameID, Matrix4x4 val) => m_Values[nameID] = val;
        public void SetMatrix(string name, Matrix4x4 val) => SetMatrix(Shader.PropertyToID(name), val);
        public void SetVectorArray(int nameID, Vector4[] values) => m_Values[nameID] = values;
        public void SetVectorArray(string name, Vector4[] values) => SetVectorArray(Shader.PropertyToID(name), values);
        public void SetMatrixArray(int nameID, Matrix4x4[] values) => m_Values[nameID] = values;
        public void SetMatrixArray(string name, Matrix4x4[] values) => SetMatrixArray(Shader.PropertyToID(name), values);
        /// <summary>Consecutive floats from the variable's offset (an HLSL float array's elements are
        /// 16 bytes apart: pass 4 floats per element).</summary>
        public void SetFloats(int nameID, params float[] values) => m_Values[nameID] = values;
        public void SetFloats(string name, params float[] values) => SetFloats(Shader.PropertyToID(name), values);
        public void SetInts(int nameID, params int[] values) => m_Values[nameID] = values;
        public void SetInts(string name, params int[] values) => SetInts(Shader.PropertyToID(name), values);

        // ------------------------------------------------------------ resources

        public void SetTexture(int kernelIndex, int nameID, Texture texture, int mipLevel)
        {
            Kernel(kernelIndex);
            m_Bound[(kernelIndex, nameID)] = (texture, mipLevel);
        }

        public void SetTexture(int kernelIndex, int nameID, Texture texture) => SetTexture(kernelIndex, nameID, texture, 0);
        public void SetTexture(int kernelIndex, string name, Texture texture) => SetTexture(kernelIndex, Shader.PropertyToID(name), texture, 0);
        public void SetTexture(int kernelIndex, string name, Texture texture, int mipLevel) => SetTexture(kernelIndex, Shader.PropertyToID(name), texture, mipLevel);
        public void SetTexture(int kernelIndex, int nameID, RenderTexture texture, int mipLevel, RenderTextureSubElement element) => SetTexture(kernelIndex, nameID, texture, mipLevel);
        public void SetTexture(int kernelIndex, string name, RenderTexture texture, int mipLevel, RenderTextureSubElement element) => SetTexture(kernelIndex, Shader.PropertyToID(name), texture, mipLevel);

        public void SetTextureFromGlobal(int kernelIndex, int nameID, int globalTextureNameID)
        {
            Kernel(kernelIndex);
            m_Bound[(kernelIndex, nameID)] = globalTextureNameID;
        }

        public void SetTextureFromGlobal(int kernelIndex, string name, string globalTextureName) =>
            SetTextureFromGlobal(kernelIndex, Shader.PropertyToID(name), Shader.PropertyToID(globalTextureName));

        public void SetBuffer(int kernelIndex, int nameID, ComputeBuffer buffer)
        {
            Kernel(kernelIndex);
            m_Bound[(kernelIndex, nameID)] = buffer;
        }

        public void SetBuffer(int kernelIndex, int nameID, GraphicsBuffer buffer)
        {
            Kernel(kernelIndex);
            m_Bound[(kernelIndex, nameID)] = buffer;
        }

        public void SetBuffer(int kernelIndex, string name, ComputeBuffer buffer) => SetBuffer(kernelIndex, Shader.PropertyToID(name), buffer);
        public void SetBuffer(int kernelIndex, string name, GraphicsBuffer buffer) => SetBuffer(kernelIndex, Shader.PropertyToID(name), buffer);

        // ------------------------------------------------------------ keywords

        public void EnableKeyword(string keyword) => m_Keywords.Add(keyword);
        public void DisableKeyword(string keyword) => m_Keywords.Remove(keyword);
        public bool IsKeywordEnabled(string keyword) => m_Keywords.Contains(keyword);

        public string[] shaderKeywords
        {
            get => new List<string>(m_Keywords).ToArray();
            set
            {
                m_Keywords.Clear();
                if (value != null) foreach (var k in value) m_Keywords.Add(k);
            }
        }

        // ------------------------------------------------------------ dispatch

        public void GetKernelThreadGroupSizes(int kernelIndex, out uint x, out uint y, out uint z)
        {
            var kernel = Kernel(kernelIndex);
            var keywords = string.Join(" ", m_Keywords);
            if (!m_Threads.TryGetValue((kernelIndex, keywords), out var threads))
            {
                var json = Native.Call("compute-kernel-threads", JsonSerializer.Serialize(new { file = m_File, kernel, keywords = new List<string>(m_Keywords) }));
                var root = JsonDocument.Parse(json).RootElement;
                threads = (root.GetProperty("x").GetUInt32(), root.GetProperty("y").GetUInt32(), root.GetProperty("z").GetUInt32());
                m_Threads[(kernelIndex, keywords)] = threads;
            }
            (x, y, z) = threads;
        }

        public void Dispatch(int kernelIndex, int threadGroupsX, int threadGroupsY, int threadGroupsZ)
        {
            var kernel = Kernel(kernelIndex);
            if (threadGroupsX <= 0 || threadGroupsY <= 0 || threadGroupsZ <= 0) return;
            var properties = Properties(kernelIndex, null);
            GfxStream.Dispatch(m_File, kernel, string.Join(" ", m_Keywords), threadGroupsX, threadGroupsY, threadGroupsZ, properties);
        }

        static readonly List<GfxStream.DispatchProperty> s_Properties = new List<GfxStream.DispatchProperty>();

        /// <summary>The dispatch's properties, handles resolved (resolving can upload, which must
        /// come before the command).</summary>
        internal List<GfxStream.DispatchProperty> Properties(int kernelIndex, Dictionary<int, object> overrides)
        {
            s_Properties.Clear();
            void Add(int id, object value)
            {
                var name = Shader.IdToName(id);
                if (name == null) return;
                var p = new GfxStream.DispatchProperty { Name = name };
                switch (value)
                {
                    case float f: p.Kind = GfxStream.FloatKind; p.Vector = new Vector4(f, 0, 0, 0); break;
                    case int i: p.Kind = GfxStream.IntKind; p.Int = i; break;
                    case Vector4 v: p.Kind = GfxStream.VectorKind; p.Vector = v; break;
                    case Matrix4x4 m: p.Kind = GfxStream.MatrixKind; p.Matrix = m; break;
                    // SetFloats/SetInts: consecutive values from the variable's offset.
                    case float[] fs: p.Kind = GfxStream.ArrayKind; p.Components = 0; p.Array = fs; break;
                    case int[] ints:
                        p.Kind = GfxStream.ArrayKind;
                        p.Components = 0;
                        p.Array = Array.ConvertAll(ints, i => (float)i);
                        break;
                    case Vector4[] vs:
                        p.Kind = GfxStream.ArrayKind;
                        p.Components = 4;
                        p.Array = new float[vs.Length * 4];
                        for (int i = 0; i < vs.Length; i++) { p.Array[i * 4] = vs[i].x; p.Array[i * 4 + 1] = vs[i].y; p.Array[i * 4 + 2] = vs[i].z; p.Array[i * 4 + 3] = vs[i].w; }
                        break;
                    case Matrix4x4[] ms:
                        p.Kind = GfxStream.ArrayKind;
                        p.Components = 16;
                        p.Array = new float[ms.Length * 16];
                        for (int i = 0; i < ms.Length; i++) for (int k = 0; k < 16; k++) p.Array[i * 16 + k] = ms[i][k];
                        break;
                    case ValueTuple<Texture, int> t:
                        if (t.Item1 is RenderTexture rt) { p.Kind = GfxStream.ImageKind; p.Handle = rt.EnsureCreated(); p.Int = t.Item2; }
                        else { p.Kind = GfxStream.TextureKind; p.Handle = TextureHandles.Of(t.Item1); }
                        break;
                    case ComputeBuffer cb: p.Kind = GfxStream.BufferKind; p.Handle = cb.Handle; break;
                    case GraphicsBuffer gb: p.Kind = GfxStream.BufferKind; p.Handle = gb.Handle; break;
                    default: return;
                }
                s_Properties.Add(p);
            }
            foreach (var v in m_Values) if (overrides == null || !overrides.ContainsKey(v.Key)) Add(v.Key, v.Value);
            if (overrides != null) foreach (var v in overrides) Add(v.Key, v.Value);
            foreach (var b in m_Bound)
            {
                if (b.Key.Kernel != kernelIndex) continue;
                var value = b.Value is int globalId ? (Shader.GetGlobalTexture(globalId), 0) : b.Value;
                Add(b.Key.Name, value);
            }
            return s_Properties;
        }
    }
}

namespace Unbound.Runtime
{
    internal static class ComputeShaderImporter
    {
        internal const long FileId = 7200000;

        internal static LoadedFile Import(AssetDatabaseRuntime.AssetInfo info)
        {
            var source = System.IO.File.ReadAllText(info.File);
            var shader = UnityEngine.ComputeShader.FromFile(info.File, System.IO.Path.GetFileNameWithoutExtension(info.Path), source);
            var file = new LoadedFile { MainObject = shader };
            file.Objects[FileId] = shader;
            return file;
        }
    }
}
