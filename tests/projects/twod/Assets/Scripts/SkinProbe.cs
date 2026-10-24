using System.Collections;
using System.Text;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.U2D;
using UnityEngine.U2D.Animation;
using static ProbeEntry;

/// <summary>2D Animation scenarios: the skinning data a sprite imports with (bones, bind poses,
/// mesh, weights) as the engine's data access gives it, then a SpriteSkin deforming the sprite
/// as its bones turn (the deformed vertices and bounds over frames) and how it renders.</summary>
public static class SkinProbe
{
    static readonly VertexAttribute[] k_Channels =
    {
        VertexAttribute.Position, VertexAttribute.Normal, VertexAttribute.Tangent, VertexAttribute.Color,
        VertexAttribute.TexCoord0, VertexAttribute.TexCoord1, VertexAttribute.BlendWeight, VertexAttribute.BlendIndices,
    };

    static string F(Matrix4x4 m)
    {
        var b = new StringBuilder();
        for (int i = 0; i < 16; i++) b.Append(m[i % 4, i / 4].ToString("0.#####", System.Globalization.CultureInfo.InvariantCulture)).Append(' ');
        return b.ToString().TrimEnd();
    }

    public static IEnumerator Run()
    {
        Scenario("skin.data", Data);
        if (!"skin.deform".StartsWith(Only) && !Only.StartsWith("skin.deform")) yield break;
        ProbeLog.Begin("skin.deform");
        var go = GameObject.Find("Skinned");
        var skin = go.GetComponent<SpriteSkin>();
        var renderer = go.GetComponent<SpriteRenderer>();
        yield return null;
        Deformed("rest", skin, renderer);
        var upper = skin.boneTransforms[0];
        var lower = skin.boneTransforms[1];
        upper.localRotation = upper.localRotation * Quaternion.Euler(0f, 0f, -20f);
        lower.localRotation = Quaternion.Euler(0f, 0f, 45f);
        yield return null;
        Deformed("bent", skin, renderer);
        var camera = ShapeProbe.ProbeCamera(new Vector3(50f, 0.5f, -10f), 3f);
        TilemapProbe.Image("bent", camera, 160, 160);
        go.transform.localScale = new Vector3(-1f, 1f, 1f);
        lower.localRotation = Quaternion.Euler(0f, 0f, -60f);
        yield return null;
        Deformed("flipped", skin, renderer);
        TilemapProbe.Image("flipped", camera, 160, 160);
        skin.enabled = false;
        yield return null;
        ProbeLog.Value("disabled.hasDeformed", skin.HasCurrentDeformedVertices());
        Value("disabled.bounds", renderer.bounds);
        TilemapProbe.Image("disabled", camera, 160, 160);
    }

    static void Value(string key, Bounds b)
    {
        ProbeLog.Value(key + ".center", b.center);
        ProbeLog.Value(key + ".size", b.size);
    }

    static void Deformed(string key, SpriteSkin skin, SpriteRenderer renderer)
    {
        ProbeLog.Value(key + ".hasDeformed", skin.HasCurrentDeformedVertices());
        if (skin.HasCurrentDeformedVertices())
        {
            int i = 0;
            foreach (var v in skin.GetDeformedVertexPositionData()) ProbeLog.Value(key + ".v" + i++, v);
        }
        Value(key + ".bounds", renderer.bounds);
        Value(key + ".localBounds", renderer.localBounds);
    }

    static void Data()
    {
        var sprite = GameObject.Find("Skinned").GetComponent<SpriteRenderer>().sprite;
        ProbeLog.Value("vertexCount", sprite.GetVertexCount());
        foreach (var channel in k_Channels) ProbeLog.Value("has." + channel, sprite.HasVertexAttribute(channel));
        var bones = sprite.GetBones();
        ProbeLog.Value("bones", bones.Length);
        for (int i = 0; i < bones.Length; i++)
        {
            var b = bones[i];
            ProbeLog.Value("bone" + i + ".name", b.name);
            ProbeLog.Value("bone" + i + ".guid", b.guid);
            ProbeLog.Value("bone" + i + ".position", b.position);
            ProbeLog.Value("bone" + i + ".rotation", b.rotation.x + " " + b.rotation.y + " " + b.rotation.z + " " + b.rotation.w);
            ProbeLog.Value("bone" + i + ".length", b.length);
            ProbeLog.Value("bone" + i + ".parent", b.parentId);
            ProbeLog.Value("bone" + i + ".color", b.color.ToString());
        }
        var poses = sprite.GetBindPoses();
        ProbeLog.Value("bindPoses", poses.Length);
        for (int i = 0; i < poses.Length; i++) ProbeLog.Value("bindPose" + i, F(poses[i]));
        var indices = sprite.GetIndices();
        var b2 = new StringBuilder();
        foreach (var index in indices) b2.Append(index).Append(' ');
        ProbeLog.Value("indices", b2.ToString().TrimEnd());
        // Each slice is read before the next is asked for (Unity invalidates the previous one).
        var positions = sprite.GetVertexAttribute<Vector3>(VertexAttribute.Position);
        ProbeLog.Value("positions", positions.Length);
        for (int i = 0; i < positions.Length; i++) ProbeLog.Value("v" + i, positions[i]);
        var uvs = sprite.GetVertexAttribute<Vector2>(VertexAttribute.TexCoord0);
        for (int i = 0; i < uvs.Length; i++) ProbeLog.Value("uv" + i, uvs[i]);
        var tangents = sprite.GetVertexAttribute<Vector4>(VertexAttribute.Tangent);
        for (int i = 0; i < tangents.Length; i++)
            ProbeLog.Value("t" + i, tangents[i].x + " " + tangents[i].y + " " + tangents[i].z + " " + tangents[i].w);
        var weights = sprite.GetVertexAttribute<BoneWeight>(VertexAttribute.BlendWeight);
        ProbeLog.Value("weights", weights.Length);
        for (int i = 0; i < weights.Length; i++)
        {
            var w = weights[i];
            ProbeLog.Value("w" + i, w.boneIndex0 + ":" + w.weight0.ToString("0.###") + " " + w.boneIndex1 + ":" + w.weight1.ToString("0.###")
                + " " + w.boneIndex2 + ":" + w.weight2.ToString("0.###") + " " + w.boneIndex3 + ":" + w.weight3.ToString("0.###"));
        }
        ProbeLog.Value("triangles", string.Join(" ", sprite.triangles));
        var vertices = sprite.vertices;
        for (int i = 0; i < vertices.Length; i++) ProbeLog.Value("vertices" + i, vertices[i]);
        Value("bounds", sprite.bounds);
    }
}
