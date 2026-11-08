using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.U2D;

public sealed partial class RenderApiProbe
{
    public static Action<GameObject> selectGizmoObject;

    void WriteMesh(Mesh mesh, string key, bool structured, bool commands = false)
    {
        var compute = Resources.Load<ComputeShader>("MeshWrite");
        int kernel = compute.FindKernel(structured ? "Structured" : "Raw");
        using (var vertices = mesh.GetVertexBuffer(0))
        using (var indices = mesh.GetIndexBuffer())
        {
            if (commands)
            {
                using (var cb = new CommandBuffer())
                {
                    cb.SetComputeBufferParam(compute, kernel, structured ? "StructuredVertices" : "RawVertices", vertices);
                    cb.SetComputeBufferParam(compute, kernel, "RawIndices", indices);
                    cb.SetComputeIntParam(compute, "Stride", mesh.GetVertexBufferStride(0));
                    cb.SetComputeIntParam(compute, "Count", mesh.vertexCount);
                    cb.DispatchCompute(compute, kernel, 1, 1, 1);
                    Graphics.ExecuteCommandBuffer(cb);
                }
            }
            else
            {
                compute.SetBuffer(kernel, structured ? "StructuredVertices" : "RawVertices", vertices);
                compute.SetBuffer(kernel, "RawIndices", indices);
                compute.SetInt("Stride", mesh.GetVertexBufferStride(0));
                compute.SetInt("Count", mesh.vertexCount);
                compute.Dispatch(kernel, 1, 1, 1);
            }
            Buffer(key + ".vertices", vertices);
            Buffer(key + ".indices", indices);
        }
    }

    IEnumerator FollowupCases()
    {
        // Every new record and frame uses its own prefix; earlier controls remain unchanged.
        foreach (float spacing in new[] { 0f, -.25f, float.NaN, float.PositiveInfinity, float.NegativeInfinity })
        {
            string key = "followup.pixel." + F(spacing).Replace("\"", "");
            var sprite = new GameObject("nonfinite sprite").AddComponent<SpriteRenderer>();
            var texture = Texture2D.whiteTexture;
            sprite.sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(.5f,.5f), texture.width);
            sprite.transform.position = new Vector3(.17f, -.19f, 0);
            PixelPerfectRendering.pixelSnapSpacing = spacing;
            Image(key);
            Value(key + ".position", V(sprite.transform.position));
            DestroyImmediate(sprite.gameObject);
        }
        PixelPerfectRendering.pixelSnapSpacing = 0;
        foreach (bool structured in new[] { false, true })
        foreach (bool unreadable in new[] { false, true })
        {
            string key = "followup.mesh." + structured + "." + unreadable;
            var mesh = Make();
            mesh.triangles = new[] { 0, 2, 1 };
            mesh.indexFormat = IndexFormat.UInt32;
            mesh.triangles = new[] { 0, 2, 1 };
            mesh.vertexBufferTarget |= structured ? GraphicsBuffer.Target.Structured : GraphicsBuffer.Target.Raw;
            mesh.indexBufferTarget |= GraphicsBuffer.Target.Raw;
            mesh.UploadMeshData(unreadable);
            var go = new GameObject("GPU mesh");
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            var material = new Material(Shader.Find("Unlit/Color"));
            material.color = Color.green;
            renderer.sharedMaterial = material;
            Image(key + ".before");
            Try(key + ".write", () => WriteMesh(mesh, key, structured));
            Image(key + ".after");
            Bool(key + ".readable", mesh.isReadable);
            if (!unreadable)
            {
                Value(key + ".cpu", V(mesh.vertices[0]));
                mesh.UploadMeshData(false);
                Image(key + ".upload");
                mesh.SetVertices(Vertices);
                Image(key + ".setVertices");
                Try(key + ".command", () => WriteMesh(mesh, key + ".command", structured, true));
                Image(key + ".commandDraw");
                using (var old = mesh.GetVertexBuffer(0))
                {
                    mesh.normals = new[] { Vector3.forward, Vector3.forward, Vector3.forward };
                    mesh.UploadMeshData(false);
                    Bool(key + ".invalidated", old.IsValid());
                    using (var current = mesh.GetVertexBuffer(0))
                        Bool(key + ".replaced", current.GetNativeBufferPtr() != old.GetNativeBufferPtr());
                    Image(key + ".layout");
                }
            }
            DestroyImmediate(go); DestroyImmediate(material); DestroyImmediate(mesh);
        }
        var skinObject = new GameObject("skin buffer");
        var skin = skinObject.AddComponent<SkinnedMeshRenderer>();
        var skinMesh = Make();
        skinMesh.triangles = new[] { 0, 2, 1 };
        skinMesh.normals = new[] { Vector3.forward, Vector3.forward, Vector3.forward };
        skinMesh.boneWeights = new[] { new BoneWeight { boneIndex0=0, weight0=1 }, new BoneWeight { boneIndex0=0, weight0=1 }, new BoneWeight { boneIndex0=0, weight0=1 } };
        skinMesh.bindposes = new[] { Matrix4x4.identity };
        skin.sharedMesh = skinMesh; skin.bones = new[] { skinObject.transform }; skin.rootBone = skinObject.transform;
        skin.updateWhenOffscreen = true;
        var skinMaterial = new Material(Shader.Find("Unlit/Color")); skinMaterial.color = Color.green;
        skin.sharedMaterial = skinMaterial;
        Value("followup.skin.defaultTarget", ((int)skin.vertexBufferTarget).ToString());
        skin.vertexBufferTarget |= GraphicsBuffer.Target.Raw;
        Value("followup.skin.target", ((int)skin.vertexBufferTarget).ToString());
        yield return null;
        Image("followup.skin.before");
        Try("followup.skin.buffer", () =>
        {
            using (var buffer = skin.GetVertexBuffer())
            {
                Buffer("followup.skin.current", buffer, false);
                if (buffer == null) return;
                var first = new float[buffer.stride / 4]; buffer.GetData(first);
                Value("followup.skin.first", V(new Vector3(first[0], first[1], first[2])));
                var compute = Resources.Load<ComputeShader>("MeshWrite");
                int kernel = compute.FindKernel("Skin");
                compute.SetBuffer(kernel, "RawVertices", buffer);
                compute.SetInt("Stride", buffer.stride); compute.SetInt("Count", skinMesh.vertexCount);
                compute.Dispatch(kernel, 1, 1, 1);
                buffer.GetData(first); Value("followup.skin.written", V(new Vector3(first[0], first[1], first[2])));
            }
        });
        Image("followup.skin.after");
        yield return null;
        Image("followup.skin.nextFrame");
        DestroyImmediate(skinObject); DestroyImmediate(skinMesh); DestroyImmediate(skinMaterial);
        var gizmoObject = new GameObject("selected gizmos");
        var gizmo = gizmoObject.AddComponent<RenderApiGizmoControls>();
        foreach (bool enabled in new[] { false, true })
        foreach (bool selected in new[] { false, true })
        {
            selectGizmoObject?.Invoke(selected ? gizmoObject : null);
            setGameGizmos?.Invoke(enabled); gizmo.drew = gizmo.drewSelected = false;
            yield return null; yield return new WaitForEndOfFrame();
            string key = "followup.gizmos." + enabled + "." + selected;
            var screen = ScreenCapture.CaptureScreenshotAsTexture(); SaveImage(key, screen); DestroyImmediate(screen);
            Bool(key + ".callback", gizmo.drew); Bool(key + ".selectedCallback", gizmo.drewSelected);
        }
        selectGizmoObject?.Invoke(null); setGameGizmos?.Invoke(false); DestroyImmediate(gizmoObject);
    }
}

public sealed class RenderApiGizmoControls : MonoBehaviour
{
    public bool drew, drewSelected;
    void OnDrawGizmos()
    {
        drew = true;
        Gizmos.matrix = Matrix4x4.Translate(new Vector3(-1.2f, 0, 0));
        Gizmos.color = Color.green; Gizmos.DrawWireCube(Vector3.zero, Vector3.one);
        Gizmos.matrix = Matrix4x4.identity;
        Gizmos.color = Color.blue; Gizmos.DrawSphere(new Vector3(1.2f, 0, 0), .4f);
        Gizmos.color = Color.yellow; Gizmos.DrawLine(new Vector3(-2,-1,0),new Vector3(2,-1,0));
    }
    void OnDrawGizmosSelected()
    {
        drewSelected = true;
        Gizmos.color = Color.magenta; Gizmos.DrawCube(new Vector3(0,1,0), new Vector3(.5f,.5f,.5f));
    }
}
