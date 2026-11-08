using System;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

public sealed partial class RenderApiProbe
{
    void ComputeColors(string key, GraphicsBuffer buffer)
    {
        var values = new Vector4[buffer.count];
        buffer.GetData(values);
        var rows = new string[values.Length];
        for (int i = 0; i < values.Length; i++)
        {
            var v = values[i];
            rows[i] = "[" + F(v.x) + "," + F(v.y) + "," + F(v.z) + "," + F(v.w) + "]";
        }
        Value(key, "[" + string.Join(",", rows) + "]");
    }

    void VolumeComputeCases()
    {
        Value("volumeCompute.limits", "[" + SystemInfo.maxComputeWorkGroupSize + "," + SystemInfo.maxComputeWorkGroupSizeX + "," + SystemInfo.maxComputeWorkGroupSizeY + "," + SystemInfo.maxComputeWorkGroupSizeZ + "," + SystemInfo.maxTextureArraySlices + "]");
        var compute = Resources.Load<ComputeShader>("VolumeCompute");
        var saved = RenderTexture.active;
        foreach (int depth in new[] { 1, 4 })
        foreach (var format in new[] { RenderTextureFormat.ARGB32, RenderTextureFormat.ARGBFloat, RenderTextureFormat.RFloat })
        {
            string key = "volumeCompute." + format + "." + depth;
            var descriptor = new RenderTextureDescriptor(4, 2, format, 0)
            { dimension = TextureDimension.Tex3D, volumeDepth = depth, enableRandomWrite = true, useMipMap = true, autoGenerateMips = false };
            var volume = new RenderTexture(descriptor) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            Bool(key + ".create", volume.Create());
            int fill = compute.FindKernel("FillVolume"), load = compute.FindKernel("LoadVolume"), sample = compute.FindKernel("SampleVolume");
            using (var read = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 8 * depth, 16))
            {
                compute.SetInts("_Size", 4, 2, depth); compute.SetFloat("_Bias", 0);
                compute.SetTexture(fill, "_Target", volume); compute.Dispatch(fill, 2, 1, (depth + 1) / 2);
                VolumeRead(key + ".written", volume);
                compute.SetInt("_ReadMip", 0);
                compute.SetTexture(load, "_Source", volume); compute.SetBuffer(load, "_Read", read);
                compute.Dispatch(load, 2, 1, (depth + 1) / 2); ComputeColors(key + ".load", read);
                compute.SetTexture(sample, "_Source", volume); compute.SetBuffer(sample, "_Read", read);
                compute.Dispatch(sample, 1, 1, 1); ComputeColors(key + ".sample", read);
                volume.filterMode = FilterMode.Point;
                compute.Dispatch(sample, 1, 1, 1); ComputeColors(key + ".point", read);
                volume.filterMode = FilterMode.Bilinear;
                // Queue distinct uniform values before one readback.
                using (var firstRead = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 8 * depth, 16))
                {
                    compute.SetFloat("_Bias", .125f); compute.Dispatch(fill, 2, 1, (depth + 1) / 2);
                    compute.SetBuffer(load, "_Read", firstRead); compute.Dispatch(load, 2, 1, (depth + 1) / 2);
                    compute.SetFloat("_Bias", .25f); compute.Dispatch(fill, 2, 1, (depth + 1) / 2);
                    compute.SetBuffer(load, "_Read", read); compute.Dispatch(load, 2, 1, (depth + 1) / 2);
                    ComputeColors(key + ".repeat.first", firstRead); ComputeColors(key + ".repeat", read);
                }
                compute.SetInts("_Size", 2, 1, Math.Max(1, depth >> 1)); compute.SetFloat("_Bias", .5f);
                compute.SetTexture(fill, "_Target", volume, 1); compute.Dispatch(fill, 1, 1, (Math.Max(1, depth >> 1) + 1) / 2);
                VolumeRead(key + ".mipWritten", volume, 1);
                compute.SetInt("_ReadMip", 1); compute.Dispatch(sample, 1, 1, 1); ComputeColors(key + ".mipSample", read);
                // A sampled binding keeps every mip; only an RW binding selects one mip.
                compute.SetTexture(sample, "_Source", volume, 1); compute.SetInt("_ReadMip", 0);
                compute.Dispatch(sample, 1, 1, 1); ComputeColors("volumeGuarantee.sampleBindingMip." + format + "." + depth, read);
            }
            RenderTexture.active = saved;
            DestroyImmediate(volume);
        }
        var odd = new RenderTexture(new RenderTextureDescriptor(3, 5, RenderTextureFormat.ARGBFloat, 0)
        { dimension = TextureDimension.Tex3D, volumeDepth = 3, enableRandomWrite = true });
        odd.Create();
        compute.SetInts("_Size", 3, 5, 3); compute.SetFloat("_Bias", 0);
        int oddFill = compute.FindKernel("FillVolume"); compute.SetTexture(oddFill, "_Target", odd); compute.Dispatch(oddFill, 2, 3, 2);
        VolumeRead("volumeCompute.odd", odd);
        var flat = new RenderTexture(3, 5, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear) { enableRandomWrite = true };
        flat.Create(); int fill2D = compute.FindKernel("Fill2D"); compute.SetTexture(fill2D, "_Control", flat); compute.Dispatch(fill2D, 2, 3, 1);
        VolumeRead("volumeCompute.control2D", flat);
        var unsigned = new RenderTexture(new RenderTextureDescriptor(4, 2, GraphicsFormat.R32_UInt, 0)
        { dimension = TextureDimension.Tex3D, volumeDepth = 3, enableRandomWrite = true });
        unsigned.Create();
        using (var read = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 24, 16))
        {
            compute.SetInts("_Size", 4, 2, 3);
            int fill = compute.FindKernel("FillUint"), load = compute.FindKernel("LoadUint");
            compute.SetTexture(fill, "_UintTarget", unsigned); compute.Dispatch(fill, 2, 1, 2);
            compute.SetTexture(load, "_UintSource", unsigned); compute.SetBuffer(load, "_Read", read); compute.Dispatch(load, 2, 1, 2);
            ComputeColors("volumeCompute.uint", read);
        }
        using (var group = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 1, 4))
        {
            group.SetData(new uint[] { 0 });
            int kernel = compute.FindKernel("WideGroup"); compute.SetBuffer(kernel, "_Group", group); compute.Dispatch(kernel, 1, 1, 1);
            var value = new uint[1]; group.GetData(value); Value("volumeCompute.wideGroup", value[0].ToString());
        }
        RenderTexture.active = saved;
        DestroyImmediate(odd); DestroyImmediate(flat); DestroyImmediate(unsigned);
        VolumeReadbackCases();
    }
}
