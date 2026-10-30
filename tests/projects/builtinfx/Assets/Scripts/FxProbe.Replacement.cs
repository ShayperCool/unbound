using UnityEngine;

// Replacement shaders: SetReplacementShader by RenderType and with no tag, RenderWithShader once,
// ResetReplacementShader; objects whose shader lacks the tag (or has a value the replacement has no
// SubShader for) are not drawn.
public partial class FxProbe
{
    void Replacement()
    {
        Directional(new Vector3(50f, -30f, 0f), Color.white, 1f);
        var stage = Stage(Standard(new Color(0.8f, 0.8f, 0.8f)));
        stage[1].GetComponent<Renderer>().sharedMaterial = Unlit(new Color(0.2f, 0.9f, 0.9f));
        var transparent = Add(new Material(Shader.Find("Legacy Shaders/Transparent/Diffuse")));
        transparent.color = new Color(1f, 0.3f, 0.3f, 0.5f);
        Primitive(PrimitiveType.Sphere, new Vector3(-0.4f, 0.6f, -1.2f), transparent, 0.7f);
        var cutout = Add(new Material(Shader.Find("Legacy Shaders/Transparent/Cutout/Diffuse")));
        cutout.mainTexture = Checker();
        Primitive(PrimitiveType.Quad, new Vector3(0.7f, 0.6f, -1f), cutout, 0.8f);
        // A shader without a RenderType.
        Primitive(PrimitiveType.Cube, new Vector3(0f, 1.4f, 0.3f), Unlit(Color.yellow, "Probe/NoType"), 0.4f);
        var camera = MakeCamera();
        var replace = Shader.Find("Probe/Replace");

        Capture(camera, "replace_none");
        camera.SetReplacementShader(replace, "RenderType");
        Capture(camera, "replace_tag");
        camera.SetReplacementShader(replace, "");
        Capture(camera, "replace_all");
        camera.SetReplacementShader(replace, "NoSuchTag");
        Capture(camera, "replace_missing_tag");
        camera.ResetReplacementShader();
        Capture(camera, "replace_reset");
        camera.RenderWithShader(replace, "RenderType");
        Save(camera.targetTexture, "replace_once");
        Capture(camera, "replace_after_once");
        camera.RenderWithShader(replace, null);
        Save(camera.targetTexture, "replace_once_null");
        Clear();
    }
}
