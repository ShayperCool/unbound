using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
#endif

/// <summary>
/// Play mode: ScriptableObject and material assets edited by script, one ScriptableObject also
/// marked dirty, then left unsaved (NoSave) or saved by AssetDatabase.SaveAssets in Play mode
/// (Save); each test runs alone (-testFilter) on its own copy of the project, and run.py compares
/// the files on disk after the Editor exits. Scene saves in Play mode are refused.
/// </summary>
public class SavePlayTests
{
#if UNITY_EDITOR
    [UnityTest]
    public IEnumerator NoSave()
    {
        yield return null;
        var data = AssetDatabase.LoadAssetAtPath<ProbeData>("Assets/Data/DataPlay.asset");
        data.count = 42;
        data.label = "play";
        var dirty = AssetDatabase.LoadAssetAtPath<ProbeData>("Assets/Data/DataPlayDirty.asset");
        dirty.count = 43;
        EditorUtility.SetDirty(dirty);
        var material = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/MatPlay.mat");
        material.color = Color.green;
        ProbeLog.Row("play.data.isDirty", EditorUtility.IsDirty(data));
        ProbeLog.Row("play.dirty.isDirty", EditorUtility.IsDirty(dirty));
        ProbeLog.Row("play.material.isDirty", EditorUtility.IsDirty(material));
        var scene = SceneManager.GetActiveScene();
        ProbeLog.Try("play.markDirty.result", () => EditorSceneManager.MarkSceneDirty(scene));
        ProbeLog.Try("play.saveScene.result", () => EditorSceneManager.SaveScene(scene));
        ProbeLog.Try("play.saveOpen.result", () => EditorSceneManager.SaveOpenScenes());
        yield return null;
        ProbeLog.Row("play.end.data", ProbeLog.Hash("Assets/Data/DataPlay.asset"));
        ProbeLog.Row("play.end.material", ProbeLog.Hash("Assets/Materials/MatPlay.mat"));
    }

    [UnityTest]
    public IEnumerator Save()
    {
        yield return null;
        var data = AssetDatabase.LoadAssetAtPath<ProbeData>("Assets/Data/DataPlaySaved.asset");
        data.count = 44;
        data.words.Add("two");
        var material = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/MatPlaySaved.mat");
        material.SetFloat("_Metallic", 0.5f);
        var dirty = AssetDatabase.LoadAssetAtPath<ProbeData>("Assets/Data/DataPlayDirty.asset");
        dirty.count = 45;
        EditorUtility.SetDirty(dirty);
        AssetDatabase.SaveAssets();
        ProbeLog.Row("playSave.after.data", ProbeLog.Hash("Assets/Data/DataPlaySaved.asset"));
        ProbeLog.Row("playSave.after.dirty", ProbeLog.Hash("Assets/Data/DataPlayDirty.asset"));
        ProbeLog.Row("playSave.after.material", ProbeLog.Hash("Assets/Materials/MatPlaySaved.mat"));
        ProbeLog.Row("playSave.after.dirty.isDirty", EditorUtility.IsDirty(dirty));
        ProbeLog.Row("playSave.after.material.isDirty", EditorUtility.IsDirty(material));
        yield return null;
    }
#endif
}
