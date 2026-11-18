using UnityEngine;

public partial class FxProbe
{
    /// <summary>Recorded last: the stencil a deferred camera leaves for the passes after its
    /// lighting. A Standard sphere (left), a sphere whose Deferred pass writes stencil 2 (right)
    /// and the background; a transparent quad over the view draws white where the stencil, masked
    /// by `_Mask`, equals `_Ref`: each bit alone (`deferredStencil.bit&lt;n&gt;.*`), 0 and 2 under
    /// every bit (`.ref0`, `.ref2`). True where it draws.</summary>
    void DeferredStencil()
    {
        var standard = Primitive(PrimitiveType.Sphere, new Vector3(-1f, 0.5f, 0f), Standard(new Color(0.3f, 0.3f, 0.3f), 0f));
        var written = Primitive(PrimitiveType.Sphere, new Vector3(1f, 0.5f, 0f), Unlit(Color.white, "Probe/DeferredStencil"));
        Directional(new Vector3(50f, -30f, 0f), Color.white, 0.8f);
        var camera = MakeCamera();
        camera.renderingPath = RenderingPath.DeferredShading;
        camera.backgroundColor = Color.black;
        var test = Add(new Material(Shader.Find("Probe/StencilTest")));
        var quad = Primitive(PrimitiveType.Quad, camera.transform.position + camera.transform.forward, test, 4f);
        quad.transform.rotation = camera.transform.rotation;
        Vector2Int At(GameObject o)
        {
            var p = camera.WorldToScreenPoint(o.transform.position);
            return new Vector2Int((int)p.x, (int)p.y);
        }
        var points = new[] { ("standard", At(standard)), ("written", At(written)), ("background", new Vector2Int(2, Height - 3)) };
        void Probe(string name, int reference, int mask)
        {
            test.SetFloat("_Ref", reference);
            test.SetFloat("_Mask", mask);
            camera.Render();
            var t = Read(camera.targetTexture);
            foreach (var (point, at) in points)
                Value("deferredStencil." + name + "." + point, B(t.GetPixel(at.x, at.y).g > 0.99f));
            DestroyImmediate(t);
        }
        for (int bit = 0; bit < 8; bit++) Probe("bit" + bit, 1 << bit, 1 << bit);
        Probe("ref0", 0, 255);
        Probe("ref2", 2, 255);
        Value("deferredStencil.actualPath", S(camera.actualRenderingPath.ToString()));
        Capture(camera, "deferred_stencil_ref2");
    }
}
