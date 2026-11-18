using UnityEngine;
using UnityEngine.Rendering;

public partial class FxProbe
{
    /// <summary>Recorded last: the stencil a deferred camera leaves for the passes after its
    /// lighting. A Standard sphere (left), a sphere whose Deferred pass writes stencil 2 (right),
    /// the background, and above them four small spheres whose Deferred passes test or write
    /// otherwise: Ref 1 Comp Equal with Pass Keep (`equalKeep`), Ref 4 Comp Equal with Pass Replace
    /// (`equalReplace`), Ref 8 Comp Always with Pass Keep (`alwaysKeep`), Ref 16 Comp Always with
    /// Pass IncrSat (`alwaysIncr`), and above those Ref 16 and Ref 32 with Pass Replace
    /// (`replace16`, `replace32`), Ref 1 with Pass IncrSat (`incr1`) and Ref 8 with Pass Replace
    /// and WriteMask 0 (`replaceMask0`), against the cleared stencil; a transparent quad over the view
    /// draws white where the stencil, masked by `_Mask`, equals `_Ref`: each bit alone
    /// (`deferredStencil.bit&lt;n&gt;.*`), 0 and 2 under every bit (`.ref0`, `.ref2`). True where it
    /// draws.</summary>
    void DeferredStencil()
    {
        var standard = Primitive(PrimitiveType.Sphere, new Vector3(-1f, 0.5f, 0f), Standard(new Color(0.3f, 0.3f, 0.3f), 0f));
        var written = Primitive(PrimitiveType.Sphere, new Vector3(1f, 0.5f, 0f), Unlit(Color.white, "Probe/DeferredStencil"));
        GameObject Small(float x, float reference, CompareFunction comp, StencilOp pass, float y = 1.4f, float scale = 0.5f, float writeMask = 255f)
        {
            var m = Unlit(Color.white, "Probe/DeferredStencil");
            m.SetFloat("_Ref", reference);
            m.SetFloat("_Comp", (float)comp);
            m.SetFloat("_Pass", (float)pass);
            m.SetFloat("_WriteMask", writeMask);
            return Primitive(PrimitiveType.Sphere, new Vector3(x, y, 0f), m, scale);
        }
        var equalKeep = Small(-1.5f, 1, CompareFunction.Equal, StencilOp.Keep);
        var equalReplace = Small(-0.5f, 4, CompareFunction.Equal, StencilOp.Replace);
        var alwaysKeep = Small(0.5f, 8, CompareFunction.Always, StencilOp.Keep);
        var alwaysIncr = Small(1.5f, 16, CompareFunction.Always, StencilOp.IncrementSaturate);
        var replace16 = Small(-1.5f, 16, CompareFunction.Always, StencilOp.Replace, 1.9f, 0.3f);
        var replace32 = Small(-0.5f, 32, CompareFunction.Always, StencilOp.Replace, 1.9f, 0.3f);
        var incr1 = Small(0.5f, 1, CompareFunction.Always, StencilOp.IncrementSaturate, 1.9f, 0.3f);
        var replaceMask0 = Small(1.5f, 8, CompareFunction.Always, StencilOp.Replace, 1.9f, 0.3f, 0f);
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
        var points = new[]
        {
            ("standard", At(standard)), ("written", At(written)), ("background", new Vector2Int(2, Height - 3)),
            ("equalKeep", At(equalKeep)), ("equalReplace", At(equalReplace)), ("alwaysKeep", At(alwaysKeep)), ("alwaysIncr", At(alwaysIncr)),
            ("replace16", At(replace16)), ("replace32", At(replace32)), ("incr1", At(incr1)), ("replaceMask0", At(replaceMask0)),
        };
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
