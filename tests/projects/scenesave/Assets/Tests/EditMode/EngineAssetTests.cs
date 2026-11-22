using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using NUnit.Framework;
using Unity.Collections;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

/// <summary>
/// Edit mode: engine objects made by script and made assets by AssetDatabase.CreateAsset, in the
/// layouts their files can take: meshes (every common vertex channel, two submeshes and 32-bit
/// indices, a blend shape, bone weights and bind poses, a custom two-stream layout with half
/// floats, lines), animation clips (rotation, scale, script float, GameObject and object reference
/// curves, an event, clip settings, a frame rate and wrap mode; a legacy clip), textures (mipmaps
/// and sampler settings, a linear float format, Alpha8), then a mesh and a texture changed and
/// saved by SaveAssets. Each row records a return value, a flag or a file's hash; run.py compares
/// the files on disk after the run.
/// </summary>
public class EngineAssetTests
{
    const string Folder = "Assets/Engine";

    static string Name(Object o) => o == null ? "null" : o.GetType().Name + ":" + o.name;

    static void Create(string key, Object asset, string path)
    {
        ProbeLog.Try(key + ".result", () => { AssetDatabase.CreateAsset(asset, path); return Name(asset); });
        ProbeLog.Row(key + ".path", AssetDatabase.GetAssetPath(asset));
        ProbeLog.Row(key + ".dirty", EditorUtility.IsDirty(asset));
        ProbeLog.Snapshot(key + ".file", path);
        ProbeLog.Snapshot(key + ".meta", path + ".meta");
    }

    [StructLayout(LayoutKind.Sequential)]
    struct HalfPosition
    {
        public ushort x, y, z, w;
    }

    static ushort Half(float f) => Mathf.FloatToHalf(f);

    [Test]
    public void EngineAssets()
    {
        AssetDatabase.CreateFolder("Assets", "Engine");

        var rich = new Mesh { indexFormat = IndexFormat.UInt32 };
        rich.vertices = new[] { new Vector3(0f, 0f, 0f), new Vector3(1f, 0f, 0f), new Vector3(1f, 1f, 0f), new Vector3(0f, 1f, 0f) };
        rich.normals = Enumerable.Repeat(Vector3.back, 4).ToArray();
        rich.tangents = Enumerable.Repeat(new Vector4(1f, 0f, 0f, -1f), 4).ToArray();
        rich.colors32 = new[] { new Color32(255, 0, 0, 255), new Color32(0, 255, 0, 255), new Color32(0, 0, 255, 255), new Color32(255, 255, 255, 128) };
        rich.uv = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f) };
        rich.SetUVs(1, new List<Vector4> { new Vector4(0f, 0f, 1f, 2f), new Vector4(1f, 0f, 1f, 2f), new Vector4(1f, 1f, 1f, 2f), new Vector4(0f, 1f, 1f, 2f) });
        rich.subMeshCount = 2;
        rich.SetTriangles(new[] { 0, 1, 2 }, 0);
        rich.SetTriangles(new[] { 0, 2, 3 }, 1);
        rich.RecalculateBounds();
        Create("rich", rich, Folder + "/Rich.asset");

        var shaped = new Mesh();
        shaped.vertices = new[] { Vector3.zero, Vector3.right, Vector3.up };
        shaped.normals = Enumerable.Repeat(Vector3.back, 3).ToArray();
        shaped.triangles = new[] { 0, 1, 2 };
        shaped.AddBlendShapeFrame("Up", 50f, new[] { Vector3.up * 0.5f, Vector3.up * 0.5f, Vector3.zero }, null, null);
        shaped.AddBlendShapeFrame("Up", 100f, new[] { Vector3.up, Vector3.up, Vector3.zero }, null, null);
        shaped.AddBlendShapeFrame("Out", 100f, new[] { Vector3.zero, Vector3.zero, Vector3.forward }, new[] { Vector3.up, Vector3.zero, Vector3.zero }, null);
        shaped.RecalculateBounds();
        Create("shaped", shaped, Folder + "/Shaped.asset");

        var skinned = new Mesh();
        skinned.vertices = new[] { Vector3.zero, Vector3.right, Vector3.up };
        skinned.triangles = new[] { 0, 1, 2 };
        skinned.boneWeights = new[]
        {
            new BoneWeight { boneIndex0 = 0, weight0 = 1f },
            new BoneWeight { boneIndex0 = 0, weight0 = 0.5f, boneIndex1 = 1, weight1 = 0.5f },
            new BoneWeight { boneIndex0 = 1, weight0 = 0.75f, boneIndex1 = 0, weight1 = 0.25f },
        };
        skinned.bindposes = new[] { Matrix4x4.identity, Matrix4x4.Translate(new Vector3(0f, -1f, 0f)) };
        skinned.RecalculateBounds();
        Create("skinned", skinned, Folder + "/Skinned.asset");

        var custom = new Mesh();
        custom.SetVertexBufferParams(3,
            new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float16, 4, 0),
            new VertexAttributeDescriptor(VertexAttribute.Normal, VertexAttributeFormat.Float32, 3, 1));
        using (var positions = new NativeArray<HalfPosition>(new[]
        {
            new HalfPosition { x = Half(0f), y = Half(0f), z = Half(0f), w = Half(1f) },
            new HalfPosition { x = Half(1f), y = Half(0f), z = Half(0f), w = Half(1f) },
            new HalfPosition { x = Half(0f), y = Half(1f), z = Half(0f), w = Half(1f) },
        }, Allocator.Temp))
            custom.SetVertexBufferData(positions, 0, 0, 3, 0);
        using (var normals = new NativeArray<Vector3>(new[] { Vector3.back, Vector3.back, Vector3.back }, Allocator.Temp))
            custom.SetVertexBufferData(normals, 0, 0, 3, 1);
        custom.SetIndexBufferParams(3, IndexFormat.UInt16);
        using (var indices = new NativeArray<ushort>(new ushort[] { 0, 1, 2 }, Allocator.Temp))
            custom.SetIndexBufferData(indices, 0, 0, 3);
        custom.subMeshCount = 1;
        custom.SetSubMesh(0, new SubMeshDescriptor(0, 3, MeshTopology.Triangles));
        custom.RecalculateBounds();
        Create("custom", custom, Folder + "/Custom.asset");

        var lines = new Mesh();
        lines.vertices = new[] { Vector3.zero, Vector3.right, Vector3.up };
        lines.SetIndices(new[] { 0, 1, 1, 2 }, MeshTopology.Lines, 0);
        Create("lines", lines, Folder + "/Lines.asset");

        var matA = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/MatEdit.mat");
        var matB = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/MatQuit.mat");
        var clip = new AnimationClip { frameRate = 30f, wrapMode = WrapMode.Loop };
        var rotation = Quaternion.Euler(0f, 90f, 0f);
        clip.SetCurve("Child", typeof(Transform), "localRotation.x", AnimationCurve.Linear(0f, 0f, 1f, rotation.x));
        clip.SetCurve("Child", typeof(Transform), "localRotation.y", AnimationCurve.Linear(0f, 0f, 1f, rotation.y));
        clip.SetCurve("Child", typeof(Transform), "localRotation.z", AnimationCurve.Linear(0f, 0f, 1f, rotation.z));
        clip.SetCurve("Child", typeof(Transform), "localRotation.w", AnimationCurve.Linear(0f, 1f, 1f, rotation.w));
        clip.SetCurve("", typeof(Transform), "localScale.y", AnimationCurve.EaseInOut(0f, 1f, 2f, 3f));
        var weighted = new AnimationCurve(new Keyframe(0f, 0.5f, 0f, 1f, 0f, 0.25f) { weightedMode = WeightedMode.Out }, new Keyframe(1f, 1f));
        clip.SetCurve("", typeof(Holder), "ratio", weighted);
        clip.SetCurve("", typeof(GameObject), "m_IsActive", AnimationCurve.Constant(0f, 1f, 1f));
        AnimationUtility.SetObjectReferenceCurve(clip, EditorCurveBinding.PPtrCurve("", typeof(MeshRenderer), "m_Materials.Array.data[0]"),
            new[] { new ObjectReferenceKeyframe { time = 0f, value = matA }, new ObjectReferenceKeyframe { time = 1f, value = matB } });
        AnimationUtility.SetAnimationEvents(clip, new[]
        {
            new AnimationEvent { time = 0.5f, functionName = "OnStep", floatParameter = 1.5f, intParameter = 3, stringParameter = "step", objectReferenceParameter = matA },
        });
        var settings = AnimationUtility.GetAnimationClipSettings(clip);
        settings.loopTime = true;
        settings.mirror = true;
        AnimationUtility.SetAnimationClipSettings(clip, settings);
        Create("clip", clip, Folder + "/Rich.anim");

        var legacy = new AnimationClip { legacy = true };
        legacy.SetCurve("", typeof(Transform), "localPosition.y", AnimationCurve.Linear(0f, 0f, 1f, 1f));
        Create("legacy", legacy, Folder + "/Legacy.anim");

        var mipped = new Texture2D(4, 4, TextureFormat.RGB24, true)
        {
            filterMode = FilterMode.Point, wrapModeU = TextureWrapMode.Clamp, wrapModeV = TextureWrapMode.Mirror, anisoLevel = 4, mipMapBias = 0.5f,
        };
        mipped.SetPixels32(Enumerable.Range(0, 16).Select(i => new Color32((byte)(i * 16), (byte)(255 - i * 16), (byte)(i * 8), 255)).ToArray());
        mipped.Apply(true);
        Create("mipped", mipped, Folder + "/Mipped.asset");

        var linear = new Texture2D(2, 1, TextureFormat.RGBAFloat, false, true);
        linear.SetPixels(new[] { new Color(0.5f, 0.25f, 2f, 1f), new Color(-1f, 0f, 0.125f, 0.5f) });
        linear.Apply();
        Create("linear", linear, Folder + "/Linear.asset");

        var alpha = new Texture2D(2, 2, TextureFormat.Alpha8, false);
        alpha.SetPixels32(new[] { new Color32(0, 0, 0, 0), new Color32(0, 0, 0, 64), new Color32(0, 0, 0, 128), new Color32(0, 0, 0, 255) });
        alpha.Apply();
        Create("alpha", alpha, Folder + "/Alpha.asset");

        // Changed after they were made, then saved.
        rich.vertices = new[] { new Vector3(0f, 0f, 0f), new Vector3(2f, 0f, 0f), new Vector3(2f, 2f, 0f), new Vector3(0f, 2f, 0f) };
        rich.RecalculateBounds();
        ProbeLog.Row("richEdit.dirty", EditorUtility.IsDirty(rich));
        EditorUtility.SetDirty(rich);
        alpha.SetPixel(0, 0, new Color(0f, 0f, 0f, 1f));
        alpha.Apply();
        ProbeLog.Row("alphaEdit.dirty", EditorUtility.IsDirty(alpha));
        EditorUtility.SetDirty(alpha);
        AssetDatabase.SaveAssets();
        ProbeLog.Snapshot("richEdit.file", Folder + "/Rich.asset");
        ProbeLog.Snapshot("alphaEdit.file", Folder + "/Alpha.asset");
    }
}
