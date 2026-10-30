using UnityEngine;
using UnityEngine.Rendering;

// Planar reflections as mirror scripts render them (a reflection camera with an oblique near plane
// and inverted culling), CalculateObliqueMatrix's values, CommandBuffer.SetInvertCulling,
// SetTargetBuffers (one target and two) and RenderDontRestore.
public partial class FxProbe
{
    static Matrix4x4 ReflectionMatrix(Vector4 p)
    {
        var m = Matrix4x4.identity;
        m.m00 = 1f - 2f * p.x * p.x; m.m01 = -2f * p.x * p.y; m.m02 = -2f * p.x * p.z; m.m03 = -2f * p.w * p.x;
        m.m10 = -2f * p.y * p.x; m.m11 = 1f - 2f * p.y * p.y; m.m12 = -2f * p.y * p.z; m.m13 = -2f * p.w * p.y;
        m.m20 = -2f * p.z * p.x; m.m21 = -2f * p.z * p.y; m.m22 = 1f - 2f * p.z * p.z; m.m23 = -2f * p.w * p.z;
        return m;
    }

    static Vector4 CameraSpacePlane(Camera camera, Vector3 position, Vector3 normal, float offset)
    {
        var m = camera.worldToCameraMatrix;
        var p = m.MultiplyPoint(position + normal * offset);
        var n = m.MultiplyVector(normal).normalized;
        return new Vector4(n.x, n.y, n.z, -Vector3.Dot(p, n));
    }

    void Mirror()
    {
        var probe = MakeCamera();
        Value("oblique.perspective", M(probe.CalculateObliqueMatrix(new Vector4(0.1f, 0.6f, -0.79f, -1.3f))));
        Value("oblique.behind", M(probe.CalculateObliqueMatrix(new Vector4(0f, -1f, 0f, 0.5f))));
        probe.orthographic = true;
        probe.orthographicSize = 3f;
        Value("oblique.orthographic", M(probe.CalculateObliqueMatrix(new Vector4(0.1f, 0.6f, -0.79f, -1.3f))));
        Value("oblique.projectionAfter", M(probe.projectionMatrix));
        GL.invertCulling = true;
        Value("gl.invertCulling.set", B(GL.invertCulling));
        GL.invertCulling = false;
        Value("gl.invertCulling.reset", B(GL.invertCulling));
        Clear();

        // A mirror at y = 0 under the stage; a cube half under the ground is cut by the near plane.
        ColorStage();
        Directional(new Vector3(50f, -30f, 0f), Color.white, 1f);
        Primitive(PrimitiveType.Cube, new Vector3(0.6f, -0.1f, -0.6f), Standard(new Color(1f, 1f, 0.2f)), 0.6f);
        var main = MakeCamera();
        var reflectionCamera = MakeCamera();
        const float offset = 0.07f;
        var normal = Vector3.up;
        var plane = new Vector4(0f, 1f, 0f, -offset);
        var reflection = ReflectionMatrix(plane);
        reflectionCamera.worldToCameraMatrix = main.worldToCameraMatrix * reflection;
        var clip = CameraSpacePlane(reflectionCamera, Vector3.zero, normal, offset);
        var oblique = main.CalculateObliqueMatrix(clip);
        Value("mirror.clipPlane", V(clip));
        Value("mirror.oblique", M(oblique));
        reflectionCamera.projectionMatrix = oblique;
        reflectionCamera.transform.position = reflection.MultiplyPoint(main.transform.position);
        var euler = main.transform.eulerAngles;
        reflectionCamera.transform.eulerAngles = new Vector3(-euler.x, euler.y, euler.z);
        GL.invertCulling = true;
        reflectionCamera.Render();
        GL.invertCulling = false;
        Save(reflectionCamera.targetTexture, "mirror");
        Capture(reflectionCamera, "mirror_no_invert");
        Capture(main, "mirror_main_after");
        Clear();

        // A command buffer inverts culling before the opaque objects and draws a sphere.
        ColorStage();
        Directional(new Vector3(50f, -30f, 0f), Color.white, 1f);
        var sphere = Primitive(PrimitiveType.Sphere, new Vector3(0f, -5f, 0f), Standard(Color.white));
        var camera = MakeCamera();
        var cmd = new CommandBuffer { name = "invert" };
        cmd.SetInvertCulling(true);
        cmd.DrawMesh(sphere.GetComponent<MeshFilter>().sharedMesh, Matrix4x4.TRS(new Vector3(0f, 1.4f, -0.5f), Quaternion.identity, Vector3.one * 0.6f), Standard(Color.yellow));
        camera.AddCommandBuffer(CameraEvent.BeforeForwardOpaque, cmd);
        Capture(camera, "cmd_invert_culling");
        Value("cmd_invert_culling.glAfter", B(GL.invertCulling));
        camera.RemoveAllCommandBuffers();
        Capture(camera, "cmd_invert_removed");
        GL.invertCulling = false;
        Clear();

        // Target buffers: one color buffer, then two with a two-output shader on the ground.
        var stage = ColorStage();
        Directional(new Vector3(50f, -30f, 0f), Color.white, 1f);
        camera = MakeCamera();
        camera.targetTexture = null;
        var color = Add(new RenderTexture(160, 120, 24, RenderTextureFormat.ARGB32));
        color.Create();
        camera.SetTargetBuffers(color.colorBuffer, color.depthBuffer);
        Value("target_buffers.targetTexture", N(camera.targetTexture));
        Value("target_buffers.pixelRect", V(new Vector4(camera.pixelRect.x, camera.pixelRect.y, camera.pixelRect.width, camera.pixelRect.height)));
        Value("target_buffers.aspect", F(camera.aspect));
        camera.Render();
        Save(color, "target_buffers");
        stage[0].GetComponent<Renderer>().sharedMaterial = Unlit(new Color(0.2f, 0.6f, 0.9f), "Probe/Mrt");
        var a = Add(new RenderTexture(160, 120, 24, RenderTextureFormat.ARGB32));
        var b = Add(new RenderTexture(160, 120, 0, RenderTextureFormat.ARGB32));
        a.Create();
        b.Create();
        camera.SetTargetBuffers(new[] { a.colorBuffer, b.colorBuffer }, a.depthBuffer);
        camera.Render();
        Save(a, "target_buffers_mrt0");
        Save(b, "target_buffers_mrt1");
        Clear();

        // RenderDontRestore leaves the camera's target active.
        ColorStage();
        camera = MakeCamera();
        camera.targetTexture.name = "camera target";
        var other = Add(new RenderTexture(8, 8, 0) { name = "other" });
        RenderTexture.active = other;
        camera.Render();
        Value("dont_restore.afterRender", N(RenderTexture.active));
        RenderTexture.active = other;
        camera.RenderDontRestore();
        Value("dont_restore.afterDontRestore", N(RenderTexture.active));
        Value("dont_restore.current", N(Camera.current));
        RenderTexture.active = null;
        Save(camera.targetTexture, "dont_restore");
        Clear();
    }
}
