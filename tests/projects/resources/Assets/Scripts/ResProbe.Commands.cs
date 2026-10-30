using System.Collections;
using UnityEngine;
using UnityEngine.Rendering;

public partial class ResProbe
{
    IEnumerator CommandRenderers()
    {
        yield return CommandRendererCase(CameraEvent.BeforeForwardAlpha, "command_early");
        yield return CommandRendererCase(CameraEvent.AfterEverything, "command_late");
    }

    IEnumerator CommandRendererCase(CameraEvent cameraEvent, string name)
    {
        var camera = Camera2D(s_Bg);
        camera.cullingMask = 0; // Only the command buffer draws these renderers.
        var material = Add(new Material(Shader.Find("Unlit/Color")));
        material.color = Color.white;
        var mesh = Add(new Mesh { name = "command quad" });
        mesh.vertices = new[] { new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f),
            new Vector3(-0.5f, 0.5f, 0f), new Vector3(0.5f, 0.5f, 0f) };
        mesh.triangles = new[] { 0, 2, 1, 2, 3, 1 };
        mesh.bindposes = new[] { Matrix4x4.identity };
        var weights = new BoneWeight[4];
        for (int i = 0; i < 4; i++) weights[i] = new BoneWeight { boneIndex0 = 0, weight0 = 1f };
        mesh.boneWeights = weights;
        var delta = new[] { Vector3.right * 0.4f, Vector3.right * 0.4f, Vector3.right * 0.4f, Vector3.right * 0.4f };
        mesh.AddBlendShapeFrame("shift", 100f, delta, new Vector3[4], new Vector3[4]);
        var block = new MaterialPropertyBlock();
        var ordinary = new Renderer[2];
        for (int i = 0; i < 2; i++)
        {
            var go = Add(new GameObject("mesh" + i, typeof(MeshFilter), typeof(MeshRenderer)));
            go.transform.position = new Vector3(-1.8f + 1.8f * i, 0f, 0f);
            go.GetComponent<MeshFilter>().sharedMesh = mesh;
            ordinary[i] = go.GetComponent<Renderer>();
            ordinary[i].sharedMaterial = material;
            block.SetColor("_Color", s_Red);
            ordinary[i].SetPropertyBlock(block);
            if (i == 1)
            {
                block.SetColor("_Color", s_Green);
                ordinary[i].SetPropertyBlock(block, 0);
            }
        }
        var skinGo = Add(new GameObject("skin", typeof(SkinnedMeshRenderer)));
        skinGo.transform.position = new Vector3(1.6f, 0f, 0f);
        var skin = skinGo.GetComponent<SkinnedMeshRenderer>();
        skin.sharedMesh = mesh;
        skin.sharedMaterial = material;
        skin.updateWhenOffscreen = true;
        var bone = Add(new GameObject("bone"));
        bone.transform.SetParent(skinGo.transform, false);
        skin.bones = new[] { bone.transform };
        skin.rootBone = bone.transform;
        skin.localBounds = new Bounds(Vector3.zero, Vector3.one * 4f);
        block.SetColor("_Color", s_Blue);
        skin.SetPropertyBlock(block);
        var commands = new CommandBuffer { name = "renderer geometry and property blocks" };
        camera.AddCommandBuffer(cameraEvent, commands);
        foreach (var r in ordinary) commands.DrawRenderer(r, material, 0, 0);
        commands.DrawRenderer(skin, material, 0, 0);
        try
        {
            bone.transform.localPosition = new Vector3(0f, 0.8f, 0f);
            skin.SetBlendShapeWeight(0, 100f);
            yield return null;
            Capture(camera, name);
            // Recorded commands follow the current pose and property blocks on every execution.
            bone.transform.localPosition = new Vector3(-0.3f, -0.7f, 0f);
            skin.SetBlendShapeWeight(0, 50f);
            block.SetColor("_Color", s_Yellow);
            ordinary[0].SetPropertyBlock(block);
            skin.SetPropertyBlock(block);
            yield return null;
            Capture(camera, name + "_updated");
        }
        finally
        {
            camera.RemoveCommandBuffer(cameraEvent, commands);
            commands.Release();
        }
        Clear();
    }
}
