using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unbound.Runtime;
using UnityEngine.Rendering;

namespace UnityEngine
{
    /// <summary>What RenderMesh and its kin draw with: the material, the layer and camera, the
    /// property block, shadow and probe settings.</summary>
    public partial struct RenderParams
    {
        public RenderParams(Material mat)
        {
            layer = 0;
            renderingLayerMask = RenderingLayerMask.defaultRenderingLayerMask;
            rendererPriority = 0;
            instanceID = 0;
            worldBounds = new Bounds(Vector3.zero, Vector3.zero);
            camera = null;
            motionVectorMode = MotionVectorGenerationMode.Camera;
            reflectionProbeUsage = ReflectionProbeUsage.Off;
            material = mat;
            matProps = null;
            shadowCastingMode = ShadowCastingMode.Off;
            receiveShadows = false;
            lightProbeUsage = LightProbeUsage.Off;
            overrideSceneCullingMask = false;
            sceneCullingMask = 0;
        }

        public int layer { get; set; }
        public uint renderingLayerMask { get; set; }
        public int rendererPriority { get; set; }
        public int instanceID { get; set; }
        public Bounds worldBounds { get; set; }
        public Camera camera { get; set; }
        public MotionVectorGenerationMode motionVectorMode { get; set; }
        public ReflectionProbeUsage reflectionProbeUsage { get; set; }
        public Material material { get; set; }
        public MaterialPropertyBlock matProps { get; set; }
        public ShadowCastingMode shadowCastingMode { get; set; }
        public bool receiveShadows { get; set; }
        public LightProbeUsage lightProbeUsage { get; set; }
        public bool overrideSceneCullingMask { get; set; }
        public ulong sceneCullingMask { get; set; }
    }

    /// <summary>
    /// Instanced and procedural draws queued for this frame's cameras. Unbound draws an instanced
    /// batch as one draw per instance, each with its matrix and, from the property block's arrays,
    /// its element of every array (what UNITY_ACCESS_INSTANCED_PROP reads).
    /// </summary>
    public partial class Graphics
    {
        internal static readonly int kMaxDrawMeshInstanceCount = 1023;

        /// <summary>The block's values for one instance: each array's element, the rest as set.</summary>
        static MaterialPropertyBlock Instance(MaterialPropertyBlock block, int index)
        {
            if (block == null || block.isEmpty) return null;
            var one = new MaterialPropertyBlock();
            foreach (var p in block.m_Values)
            {
                switch (p.Value)
                {
                    case float[] fs: if (index < fs.Length) one.m_Values[p.Key] = fs[index]; break;
                    case Vector4[] vs: if (index < vs.Length) one.m_Values[p.Key] = vs[index]; break;
                    case Matrix4x4[] ms: if (index < ms.Length) one.m_Values[p.Key] = ms[index]; break;
                    default: one.m_Values[p.Key] = p.Value; break;
                }
            }
            return one;
        }

        static void CheckInstanced(Mesh mesh, int submeshIndex, Material material, int count)
        {
            if (!SystemInfo.supportsInstancing) throw new InvalidOperationException("Instancing is not supported.");
            if (mesh == null) throw new ArgumentNullException(nameof(mesh));
            if (submeshIndex < 0 || submeshIndex >= mesh.subMeshCount) throw new ArgumentOutOfRangeException(nameof(submeshIndex), "submeshIndex out of range.");
            if (material == null) throw new ArgumentNullException(nameof(material));
            if (!material.enableInstancing) throw new InvalidOperationException("Material needs to enable instancing for use with DrawMeshInstanced.");
            if (count < 0 || count > kMaxDrawMeshInstanceCount) throw new ArgumentOutOfRangeException(nameof(count), $"Count must be in the range of 0 to {kMaxDrawMeshInstanceCount}.");
        }

        public static void DrawMeshInstanced(Mesh mesh, int submeshIndex, Material material, Matrix4x4[] matrices, int count, MaterialPropertyBlock properties,
            ShadowCastingMode castShadows, bool receiveShadows, int layer, Camera camera, LightProbeUsage lightProbeUsage)
        {
            if (matrices == null) throw new ArgumentNullException(nameof(matrices));
            count = Math.Min(count, matrices.Length);
            CheckInstanced(mesh, submeshIndex, material, count);
            var lighting = ImmediateLighting.Of(castShadows, receiveShadows, lightProbeUsage);
            for (int i = 0; i < count; i++)
                Immediate.Queue(mesh, matrices[i], material, layer, camera, submeshIndex, Instance(properties, i), lighting);
        }

        public static void DrawMeshInstanced(Mesh mesh, int submeshIndex, Material material, Matrix4x4[] matrices, int count, MaterialPropertyBlock properties,
            ShadowCastingMode castShadows, bool receiveShadows, int layer, Camera camera) =>
            DrawMeshInstanced(mesh, submeshIndex, material, matrices, count, properties, castShadows, receiveShadows, layer, camera, LightProbeUsage.BlendProbes);
        public static void DrawMeshInstanced(Mesh mesh, int submeshIndex, Material material, Matrix4x4[] matrices, int count, MaterialPropertyBlock properties,
            ShadowCastingMode castShadows, bool receiveShadows, int layer) =>
            DrawMeshInstanced(mesh, submeshIndex, material, matrices, count, properties, castShadows, receiveShadows, layer, null);
        public static void DrawMeshInstanced(Mesh mesh, int submeshIndex, Material material, Matrix4x4[] matrices, int count, MaterialPropertyBlock properties,
            ShadowCastingMode castShadows, bool receiveShadows) =>
            DrawMeshInstanced(mesh, submeshIndex, material, matrices, count, properties, castShadows, receiveShadows, 0);
        public static void DrawMeshInstanced(Mesh mesh, int submeshIndex, Material material, Matrix4x4[] matrices, int count, MaterialPropertyBlock properties,
            ShadowCastingMode castShadows) => DrawMeshInstanced(mesh, submeshIndex, material, matrices, count, properties, castShadows, true);
        public static void DrawMeshInstanced(Mesh mesh, int submeshIndex, Material material, Matrix4x4[] matrices, int count, MaterialPropertyBlock properties) =>
            DrawMeshInstanced(mesh, submeshIndex, material, matrices, count, properties, ShadowCastingMode.On);
        public static void DrawMeshInstanced(Mesh mesh, int submeshIndex, Material material, Matrix4x4[] matrices, int count) =>
            DrawMeshInstanced(mesh, submeshIndex, material, matrices, count, null);
        public static void DrawMeshInstanced(Mesh mesh, int submeshIndex, Material material, Matrix4x4[] matrices) =>
            DrawMeshInstanced(mesh, submeshIndex, material, matrices, matrices?.Length ?? 0);

        public static void DrawMeshInstanced(Mesh mesh, int submeshIndex, Material material, List<Matrix4x4> matrices, MaterialPropertyBlock properties,
            ShadowCastingMode castShadows, bool receiveShadows, int layer, Camera camera, LightProbeUsage lightProbeUsage)
        {
            if (matrices == null) throw new ArgumentNullException(nameof(matrices));
            DrawMeshInstanced(mesh, submeshIndex, material, matrices.ToArray(), matrices.Count, properties, castShadows, receiveShadows, layer, camera, lightProbeUsage);
        }

        public static void DrawMeshInstanced(Mesh mesh, int submeshIndex, Material material, List<Matrix4x4> matrices, MaterialPropertyBlock properties,
            ShadowCastingMode castShadows, bool receiveShadows, int layer, Camera camera) =>
            DrawMeshInstanced(mesh, submeshIndex, material, matrices, properties, castShadows, receiveShadows, layer, camera, LightProbeUsage.BlendProbes);
        public static void DrawMeshInstanced(Mesh mesh, int submeshIndex, Material material, List<Matrix4x4> matrices, MaterialPropertyBlock properties,
            ShadowCastingMode castShadows, bool receiveShadows, int layer) =>
            DrawMeshInstanced(mesh, submeshIndex, material, matrices, properties, castShadows, receiveShadows, layer, null);
        public static void DrawMeshInstanced(Mesh mesh, int submeshIndex, Material material, List<Matrix4x4> matrices, MaterialPropertyBlock properties,
            ShadowCastingMode castShadows, bool receiveShadows) =>
            DrawMeshInstanced(mesh, submeshIndex, material, matrices, properties, castShadows, receiveShadows, 0);
        public static void DrawMeshInstanced(Mesh mesh, int submeshIndex, Material material, List<Matrix4x4> matrices, MaterialPropertyBlock properties,
            ShadowCastingMode castShadows) => DrawMeshInstanced(mesh, submeshIndex, material, matrices, properties, castShadows, true);
        public static void DrawMeshInstanced(Mesh mesh, int submeshIndex, Material material, List<Matrix4x4> matrices, MaterialPropertyBlock properties) =>
            DrawMeshInstanced(mesh, submeshIndex, material, matrices, properties, ShadowCastingMode.On);
        public static void DrawMeshInstanced(Mesh mesh, int submeshIndex, Material material, List<Matrix4x4> matrices) =>
            DrawMeshInstanced(mesh, submeshIndex, material, matrices, null);

        // ------------------------------------------------------------------ RenderMesh

        public static void RenderMesh(in RenderParams rparams, Mesh mesh, int submeshIndex, Matrix4x4 objectToWorld, Matrix4x4? prevObjectToWorld = null)
        {
            if (mesh == null) throw new ArgumentNullException(nameof(mesh));
            if (rparams.material == null) throw new ArgumentNullException("rparams.material");
            Immediate.Queue(mesh, objectToWorld, rparams.material, rparams.layer, rparams.camera, submeshIndex, rparams.matProps, Lighting(rparams));
        }

        static ImmediateLighting Lighting(in RenderParams rparams) =>
            ImmediateLighting.Of(rparams.shadowCastingMode, rparams.receiveShadows, rparams.lightProbeUsage, null, rparams.reflectionProbeUsage);

        /// <summary>Instances whose data starts with their object-to-world matrix (Matrix4x4 or a
        /// struct led by one).</summary>
        static void RenderInstances(in RenderParams rparams, Mesh mesh, int submeshIndex, ReadOnlySpan<byte> data, int stride, int instanceCount, int startInstance)
        {
            if (mesh == null) throw new ArgumentNullException(nameof(mesh));
            if (rparams.material == null) throw new ArgumentNullException("rparams.material");
            if (!rparams.material.enableInstancing) throw new InvalidOperationException("Material needs to enable instancing for use with RenderMeshInstanced.");
            if (stride < 64) throw new InvalidOperationException("The instance data must start with an object-to-world Matrix4x4.");
            int available = data.Length / stride;
            if (instanceCount < 0) instanceCount = available - startInstance;
            if (startInstance < 0 || startInstance + instanceCount > available) throw new ArgumentOutOfRangeException(nameof(startInstance), "instance range out of the data's bounds");
            var lighting = Lighting(rparams);
            for (int i = 0; i < instanceCount; i++)
            {
                var matrix = System.Runtime.InteropServices.MemoryMarshal.Read<Matrix4x4>(data.Slice((startInstance + i) * stride, 64));
                Immediate.Queue(mesh, matrix, rparams.material, rparams.layer, rparams.camera, submeshIndex, Instance(rparams.matProps, startInstance + i), lighting);
            }
        }

        public static void RenderMeshInstanced<T>(in RenderParams rparams, Mesh mesh, int submeshIndex, T[] instanceData, int instanceCount = -1, int startInstance = 0) where T : unmanaged
        {
            if (instanceData == null) throw new ArgumentNullException(nameof(instanceData));
            RenderInstances(rparams, mesh, submeshIndex, System.Runtime.InteropServices.MemoryMarshal.AsBytes(instanceData.AsSpan()), UnsafeUtility.SizeOf<T>(), instanceCount, startInstance);
        }

        public static void RenderMeshInstanced<T>(in RenderParams rparams, Mesh mesh, int submeshIndex, List<T> instanceData, int instanceCount = -1, int startInstance = 0) where T : unmanaged
        {
            if (instanceData == null) throw new ArgumentNullException(nameof(instanceData));
            RenderInstances(rparams, mesh, submeshIndex, System.Runtime.InteropServices.MemoryMarshal.AsBytes(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(instanceData)), UnsafeUtility.SizeOf<T>(), instanceCount, startInstance);
        }

        public static unsafe void RenderMeshInstanced<T>(RenderParams rparams, Mesh mesh, int submeshIndex, NativeArray<T> instanceData, int instanceCount = -1, int startInstance = 0) where T : unmanaged
        {
            int size = UnsafeUtility.SizeOf<T>();
            var bytes = new ReadOnlySpan<byte>(NativeArrayUnsafeUtility.GetUnsafeReadOnlyPtr(instanceData), instanceData.Length * size);
            RenderInstances(rparams, mesh, submeshIndex, bytes, size, instanceCount, startInstance);
        }

        // ------------------------------------------------------------------ procedural


        public static void DrawProcedural(Material material, Bounds bounds, MeshTopology topology, int vertexCount, int instanceCount = 1, Camera camera = null,
            MaterialPropertyBlock properties = null, ShadowCastingMode castShadows = ShadowCastingMode.On, bool receiveShadows = true, int layer = 0)
        {
            if (material == null) throw new ArgumentNullException(nameof(material));
            if (vertexCount <= 0 || instanceCount <= 0) return;
            Immediate.Queue(ProceduralMeshes.Get(topology, vertexCount), Matrix4x4.identity, material, layer, camera, 0, properties,
                instanceCount > 1 ? new DrawSetup { Instances = (uint)instanceCount } : (DrawSetup?)null, ImmediateLighting.Of(castShadows, receiveShadows));
        }

        // ------------------------------------------------------------------ DrawTexture

        static Mesh s_TextureQuad;
        static Material s_TextureMaterial;
        static bool s_WarnedBorders;

        /// <summary>A textured quad over <paramref name="screenRect"/> in the current GL matrices
        /// (GL.LoadPixelMatrix's pixels): the texture's top at the rect's y (GUI's orientation,
        /// measured), <paramref name="sourceRect"/> its part, the color neutral at 0.5 (doubled).</summary>
        static void DrawTextureImpl(Rect screenRect, Texture texture, Rect sourceRect, int leftBorder, int rightBorder, int topBorder, int bottomBorder,
            Color color, Material mat, int pass)
        {
            if (texture == null) throw new ArgumentNullException(nameof(texture));
            if ((leftBorder | rightBorder | topBorder | bottomBorder) != 0 && !s_WarnedBorders)
            {
                s_WarnedBorders = true;
                Debug.LogWarning("Unbound: Graphics.DrawTexture borders are not drawn yet; the whole rect is.");
            }
            if (s_TextureQuad == null)
            {
                s_TextureQuad = new Mesh { name = "Unbound DrawTexture" };
                s_TextureQuad.vertices = new Vector3[4];
                s_TextureQuad.uv = new Vector2[4];
                s_TextureQuad.colors = new Color[4];
                s_TextureQuad.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            }
            s_TextureQuad.vertices = new[]
            {
                new Vector3(screenRect.xMin, screenRect.yMin, 0f), new Vector3(screenRect.xMin, screenRect.yMax, 0f),
                new Vector3(screenRect.xMax, screenRect.yMax, 0f), new Vector3(screenRect.xMax, screenRect.yMin, 0f),
            };
            s_TextureQuad.uv = new[]
            {
                new Vector2(sourceRect.xMin, sourceRect.yMax), new Vector2(sourceRect.xMin, sourceRect.yMin),
                new Vector2(sourceRect.xMax, sourceRect.yMin), new Vector2(sourceRect.xMax, sourceRect.yMax),
            };
            var tint = color * 2f;
            s_TextureQuad.colors = new[] { tint, tint, tint, tint };
            var material = mat;
            if (material == null)
            {
                if (s_TextureMaterial == null) s_TextureMaterial = new Material(Shader.Find("UI/Default")) { hideFlags = HideFlags.HideAndDontSave };
                material = s_TextureMaterial;
            }
            material.mainTexture = texture;
            material.SetPass(pass < 0 ? 0 : pass);
            DrawMeshNow(s_TextureQuad, Matrix4x4.identity);
        }

        public static void DrawTexture(Rect screenRect, Texture texture, Rect sourceRect, int leftBorder, int rightBorder, int topBorder, int bottomBorder, Color color, Material mat, int pass) =>
            DrawTextureImpl(screenRect, texture, sourceRect, leftBorder, rightBorder, topBorder, bottomBorder, color, mat, pass);
        public static void DrawTexture(Rect screenRect, Texture texture, Rect sourceRect, int leftBorder, int rightBorder, int topBorder, int bottomBorder, Color color, Material mat) =>
            DrawTextureImpl(screenRect, texture, sourceRect, leftBorder, rightBorder, topBorder, bottomBorder, color, mat, -1);
        public static void DrawTexture(Rect screenRect, Texture texture, Rect sourceRect, int leftBorder, int rightBorder, int topBorder, int bottomBorder, Color color) =>
            DrawTextureImpl(screenRect, texture, sourceRect, leftBorder, rightBorder, topBorder, bottomBorder, color, null, -1);
        public static void DrawTexture(Rect screenRect, Texture texture, Rect sourceRect, int leftBorder, int rightBorder, int topBorder, int bottomBorder, Material mat, int pass) =>
            DrawTextureImpl(screenRect, texture, sourceRect, leftBorder, rightBorder, topBorder, bottomBorder, new Color(0.5f, 0.5f, 0.5f, 0.5f), mat, pass);
        public static void DrawTexture(Rect screenRect, Texture texture, Rect sourceRect, int leftBorder, int rightBorder, int topBorder, int bottomBorder, Material mat) =>
            DrawTextureImpl(screenRect, texture, sourceRect, leftBorder, rightBorder, topBorder, bottomBorder, new Color(0.5f, 0.5f, 0.5f, 0.5f), mat, -1);
        public static void DrawTexture(Rect screenRect, Texture texture, Rect sourceRect, int leftBorder, int rightBorder, int topBorder, int bottomBorder) =>
            DrawTextureImpl(screenRect, texture, sourceRect, leftBorder, rightBorder, topBorder, bottomBorder, new Color(0.5f, 0.5f, 0.5f, 0.5f), null, -1);
        public static void DrawTexture(Rect screenRect, Texture texture, int leftBorder, int rightBorder, int topBorder, int bottomBorder, Material mat, int pass) =>
            DrawTextureImpl(screenRect, texture, new Rect(0, 0, 1, 1), leftBorder, rightBorder, topBorder, bottomBorder, new Color(0.5f, 0.5f, 0.5f, 0.5f), mat, pass);
        public static void DrawTexture(Rect screenRect, Texture texture, int leftBorder, int rightBorder, int topBorder, int bottomBorder, Material mat) =>
            DrawTextureImpl(screenRect, texture, new Rect(0, 0, 1, 1), leftBorder, rightBorder, topBorder, bottomBorder, new Color(0.5f, 0.5f, 0.5f, 0.5f), mat, -1);
        public static void DrawTexture(Rect screenRect, Texture texture, int leftBorder, int rightBorder, int topBorder, int bottomBorder) =>
            DrawTextureImpl(screenRect, texture, new Rect(0, 0, 1, 1), leftBorder, rightBorder, topBorder, bottomBorder, new Color(0.5f, 0.5f, 0.5f, 0.5f), null, -1);
        public static void DrawTexture(Rect screenRect, Texture texture, Material mat, int pass) =>
            DrawTextureImpl(screenRect, texture, new Rect(0, 0, 1, 1), 0, 0, 0, 0, new Color(0.5f, 0.5f, 0.5f, 0.5f), mat, pass);
        public static void DrawTexture(Rect screenRect, Texture texture, Material mat) =>
            DrawTextureImpl(screenRect, texture, new Rect(0, 0, 1, 1), 0, 0, 0, 0, new Color(0.5f, 0.5f, 0.5f, 0.5f), mat, -1);
        public static void DrawTexture(Rect screenRect, Texture texture) =>
            DrawTextureImpl(screenRect, texture, new Rect(0, 0, 1, 1), 0, 0, 0, 0, new Color(0.5f, 0.5f, 0.5f, 0.5f), null, -1);
    }
}
