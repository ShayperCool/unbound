using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Animations;
public static class ProbeRunner
{
    public static void Create()
    {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var root = new GameObject("Serialized");
        var source = new GameObject("Source"); source.transform.SetParent(root.transform, false);
        source.transform.localPosition = new Vector3(3,4,5);
        source.transform.localRotation = Quaternion.Euler(20,40,10);
        source.transform.localScale = new Vector3(2,3,4);
        for(int i=0;i<ConstraintProbe.Types.Length;i++)
        {
            var go = new GameObject("Constraint"+i); go.transform.SetParent(root.transform, false);
            var c = (IConstraint)go.AddComponent(ConstraintProbe.Types[i]);
            ConstraintProbe.Configure(c, 7, true);
            c.AddSource(new ConstraintSource { sourceTransform=source.transform, weight=0.75f });
            c.weight=0.6f; c.locked=true; c.constraintActive=true;
        }
        PrefabUtility.SaveAsPrefabAsset(root,"Assets/Resources/Constraints.prefab");
        Object.DestroyImmediate(root);
        EditorSceneManager.SaveScene(scene,"Assets/Scenes/Probe.unity");
        EditorBuildSettings.scenes=new[]{new EditorBuildSettingsScene("Assets/Scenes/Probe.unity",true)};
        AssetDatabase.SaveAssets();
    }
    public static void Record()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/Probe.unity");
        EditorApplication.EnterPlaymode();
    }
}
