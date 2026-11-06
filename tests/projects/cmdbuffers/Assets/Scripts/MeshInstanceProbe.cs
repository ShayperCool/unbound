using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;

public partial class CmdBuffersProbe
{
    // Extra members are ignored by Unity. Only objectToWorld is used in these cases.
    struct MeshInstanceData { public Matrix4x4 objectToWorld; public float ignored; }

    IEnumerator MeshInstanceFrames()
    {
        var grid = Keep(new RenderTexture(16, 4, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear));
        grid.Create();
        var cam = Keep(new GameObject("MeshInstanceCamera")).AddComponent<Camera>();
        cam.transform.position = new Vector3(8, 2, -10);
        cam.enabled = false;
        cam.orthographic = true;
        cam.orthographicSize = 2;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = Color.clear;
        cam.targetTexture = grid;
        cam.allowHDR = false;
        cam.allowMSAA = false;
        var mesh = Keep(new Mesh());
        mesh.vertices = new[] { Vector3.zero, Vector3.right, new Vector3(1,1,0), Vector3.up };
        mesh.triangles = new[] { 0,1,2,0,2,3 };
        var mat = Keep(new Material(Resources.Load<Shader>("MeshInstanceIds")) { enableInstancing = true });
        mat.SetMatrix("_Warp", Matrix4x4.identity);
        var matrices = Enumerable.Range(0, 8).Select(i => Matrix4x4.Translate(new Vector3(i,0,0))).ToArray();
        var colors = Enumerable.Range(0, 8).Select(i => Rgb(20 + i * 25, 180 - i * 15, 30 + i)).ToArray();
        var cases = new List<(string key, Action draw)>();
        void Case(string key, Action draw) => cases.Add(("graphics.meshinstances." + key, draw));
        void Draw(Matrix4x4[] data, int count, MaterialPropertyBlock block = null) =>
            Graphics.DrawMeshInstanced(mesh, 0, mat, data, count, block, ShadowCastingMode.Off, false, 0, cam, LightProbeUsage.Off);
        RenderParams Params(MaterialPropertyBlock block = null) => new RenderParams(mat) { camera = cam, matProps = block };
        void Render(Matrix4x4[] data, int count = -1, int start = 0, MaterialPropertyBlock block = null) =>
            Graphics.RenderMeshInstanced(Params(block), mesh, 0, data, count, start);
        MaterialPropertyBlock Arrays(float mode)
        {
            var b = new MaterialPropertyBlock();
            b.SetFloat("_Mode", mode);
            b.SetVectorArray("_Tint", colors);
            b.SetFloatArray("_Weight", Enumerable.Repeat(1f, 8).ToArray());
            b.SetVectorArray("_Palette", colors.Reverse().ToArray());
            b.SetMatrixArray("_Warp", Enumerable.Range(0, 8).Select(i => Matrix4x4.Translate(new Vector3(i % 2,0,0))).ToArray());
            return b;
        }
        foreach (int count in new[] { 2, 3, 7 })
        {
            int n = count;
            Case("draw.ids." + n, () => Draw(matrices, n));
            Case("render.ids." + n, () => Render(matrices, n));
        }
        Case("draw.list", () => Graphics.DrawMeshInstanced(mesh, 0, mat, matrices.Take(3).ToList(), null,
            ShadowCastingMode.Off, false, 0, cam, LightProbeUsage.Off));
        Case("render.list", () => Graphics.RenderMeshInstanced(Params(), mesh, 0, matrices.ToList(), 3, 2));
        Case("render.native", () =>
        {
            using (var data = new NativeArray<Matrix4x4>(matrices, Allocator.Temp))
                Graphics.RenderMeshInstanced(Params(), mesh, 0, data, 3, 2);
        });
        Case("render.struct", () => Graphics.RenderMeshInstanced(Params(), mesh, 0,
            matrices.Select(m => new MeshInstanceData { objectToWorld = m, ignored = 99 }).ToArray(), 3, 2));
        Case("render.start", () => Render(matrices, 3, 2));
        Case("render.start.remaining", () => Render(matrices, -1, 3));
        Case("draw.groups", () => { Draw(matrices, 2); Draw(matrices.Skip(3).ToArray(), 3); });
        Case("render.groups", () => { Render(matrices, 2); Render(matrices, 3, 3); });
        foreach (bool render in new[] { false, true })
        {
            string api = render ? "render" : "draw";
            void Submit(Matrix4x4[] data, MaterialPropertyBlock b) { if (render) Render(data, 3, 2, b); else Draw(data, 3, b); }
            Case(api + ".arrays", () => Submit(matrices, Arrays(1)));
            Case(api + ".uniformarray", () => Submit(matrices, Arrays(2)));
            Case(api + ".matrixarray", () => Submit(matrices, Arrays(3)));
            Case(api + ".scalar", () =>
            {
                var b = new MaterialPropertyBlock();
                b.SetFloat("_Mode", 1);
                b.SetVector("_Tint", Rgb(7,77,177));
                b.SetFloat("_Weight", 0.5f);
                Submit(matrices, b);
            });
            Case(api + ".snapshot", () =>
            {
                var data = (Matrix4x4[])matrices.Clone();
                var b = Arrays(3);
                Submit(data, b);
                for (int i = 0; i < data.Length; i++) data[i] = Matrix4x4.Translate(Vector3.one * 1000);
                b.SetVectorArray("_Tint", Enumerable.Repeat(Vector4.zero, 8).ToArray());
                b.SetFloatArray("_Weight", new float[8]);
                b.SetMatrixArray("_Warp", Enumerable.Repeat(Matrix4x4.Translate(Vector3.one * 1000), 8).ToArray());
                b.SetVectorArray("_Palette", Enumerable.Repeat(Vector4.zero, 8).ToArray());
            });
            foreach (int count in new[] { 513, 1023 })
            {
                int n = count;
                Case(api + ".batch." + n, () =>
                {
                    int[] samples = { 0,1,2,499,500,501,509,510,511,512,999,1000,1021,1022 };
                    var data = Enumerable.Range(0, n).Select(i => Matrix4x4.Translate(new Vector3(
                        Array.IndexOf(samples, i) is int x && x >= 0 ? x : 10000, 0, 0))).ToArray();
                    if (render) Render(data); else Draw(data, n);
                });
            }
        }
        var defaultMat = mat;
        var uniformMat = Keep(new Material(Resources.Load<Shader>("MeshInstanceUniform")) { enableInstancing = true });
        var fixedMat = Keep(new Material(Resources.Load<Shader>("MeshInstanceFixed")) { enableInstancing = true });
        foreach (bool render in new[] { false, true })
        {
            string api = render ? "render" : "draw";
            Case(api + ".fixedbatch", () =>
            {
                mat = fixedMat;
                if (render) Render(matrices, 7); else Draw(matrices, 7);
                mat = defaultMat;
            });
            var data = Enumerable.Range(0, 1023).Select(i => Matrix4x4.Translate(new Vector3(
                i >= 508 && i <= 513 ? i - 508 : i == 1021 ? 7 : i == 1022 ? 8 : 10000, 0, 0))).ToArray();
            Case(api + ".batch.arrays", () =>
            {
                var b = new MaterialPropertyBlock();
                b.SetFloat("_Mode", 1);
                b.SetVectorArray("_Tint", Enumerable.Range(0, 1023).Select(i => Rgb(i % 251, i % 241, i / 241)).ToArray());
                b.SetFloatArray("_Weight", Enumerable.Repeat(1f, 1023).ToArray());
                if (render) Render(data, -1, 0, b); else Draw(data, data.Length, b);
            });
            Case(api + ".batch.scalar", () =>
            {
                var b = new MaterialPropertyBlock();
                b.SetFloat("_Mode", 1);
                b.SetVector("_Tint", Rgb(7,77,177));
                if (render) Render(data, -1, 0, b); else Draw(data, data.Length, b);
            });
            Case(api + ".batch.uniformscaling", () =>
            {
                mat = uniformMat;
                if (render) Render(data); else Draw(data, data.Length);
                mat = defaultMat;
            });
        }
        foreach (var c in cases)
        {
            Try(c.key, c.draw);
            cam.Render();
            Value(c.key, Read(grid));
            yield return null;
        }
    }
}
