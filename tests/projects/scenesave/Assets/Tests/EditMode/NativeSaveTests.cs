using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

/// <summary>
/// Edit mode: native assets read from their files, changed through their APIs and saved by
/// AssetDatabase.SaveAssets: a mesh, a texture, a physics material, an animation clip (a curve
/// added), a mesh of an imported model (with the messages the save logs), and a mesh asset left
/// untouched. Each row records a flag or a file's hash; run.py compares the files on disk after
/// the run.
/// </summary>
public class NativeSaveTests
{
    const string Folder = "Assets/Native";

    [Test]
    public void NativeSaves()
    {
        var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(Folder + "/Tri.asset");
        ProbeLog.Row("mesh.readable", mesh.isReadable);
        mesh.vertices = new[] { Vector3.zero, Vector3.right * 2f, Vector3.up * 2f };
        mesh.RecalculateBounds();
        ProbeLog.Row("mesh.dirty", EditorUtility.IsDirty(mesh));

        var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(Folder + "/Alpha.asset");
        texture.SetPixel(1, 1, new Color(0f, 0f, 0f, 0.5f));
        ProbeLog.Row("texture.dirtySetPixel", EditorUtility.IsDirty(texture));
        texture.Apply();
        ProbeLog.Row("texture.dirty", EditorUtility.IsDirty(texture));

        var physic = AssetDatabase.LoadAssetAtPath<PhysicsMaterial>(Folder + "/Bouncy.physicMaterial");
        physic.bounciness = 0.9f;
        ProbeLog.Row("physic.dirty", EditorUtility.IsDirty(physic));

        var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(Folder + "/Move.anim");
        clip.SetCurve("", typeof(Transform), "localScale.y", AnimationCurve.Linear(0f, 1f, 1f, 2f));
        ProbeLog.Row("clip.dirty", EditorUtility.IsDirty(clip));

        var modelMesh = AssetDatabase.LoadAssetAtPath<Mesh>(Folder + "/Triangle.obj");
        ProbeLog.Row("model.readable", modelMesh != null && modelMesh.isReadable);
        Application.LogCallback log = (message, stack, type) => ProbeLog.Row("save.log", type + ": " + message);
        Application.logMessageReceived += log;
        LogAssert.ignoreFailingMessages = true;
        if (modelMesh != null && modelMesh.isReadable)
        {
            modelMesh.vertices = new[] { Vector3.zero, Vector3.right * 3f, Vector3.up * 3f };
            ProbeLog.Row("model.dirtyVertices", EditorUtility.IsDirty(modelMesh));
            EditorUtility.SetDirty(modelMesh);
            ProbeLog.Row("model.dirty", EditorUtility.IsDirty(modelMesh));
        }
        AssetDatabase.SaveAssets();
        LogAssert.ignoreFailingMessages = false;
        Application.logMessageReceived -= log;

        ProbeLog.Snapshot("mesh.file", Folder + "/Tri.asset");
        ProbeLog.Snapshot("texture.file", Folder + "/Alpha.asset");
        ProbeLog.Snapshot("physic.file", Folder + "/Bouncy.physicMaterial");
        ProbeLog.Snapshot("clip.file", Folder + "/Move.anim");
        ProbeLog.Row("model.file", ProbeLog.Hash(Folder + "/Triangle.obj"));
        ProbeLog.Row("untouched.file", ProbeLog.Hash(Folder + "/Untouched.asset"));
        ProbeLog.Row("mesh.dirtyAfter", EditorUtility.IsDirty(mesh));
        if (modelMesh != null) ProbeLog.Row("model.dirtyAfter", EditorUtility.IsDirty(modelMesh));
    }
}
