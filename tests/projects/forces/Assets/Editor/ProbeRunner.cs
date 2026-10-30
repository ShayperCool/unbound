using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
public static class ProbeRunner
{
    public static void Create()
    {
        var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
        EditorSceneManager.SaveScene(scene,"Assets/Scenes/Probe.unity");EditorBuildSettings.scenes=new[]{new EditorBuildSettingsScene("Assets/Scenes/Probe.unity",true)};
        var root=new GameObject("Forces");var three=new GameObject("Three");three.transform.SetParent(root.transform,false);
        var a=three.AddComponent<ConstantForce>();a.force=new Vector3(4,2,-3);a.relativeForce=new Vector3(1,2,3);a.torque=new Vector3(.5f,1,.25f);a.relativeTorque=new Vector3(.3f,.2f,.1f);three.GetComponent<Rigidbody>().useGravity=false;
        var two=new GameObject("Two");two.transform.SetParent(root.transform,false);var b=two.AddComponent<ConstantForce2D>();b.force=new Vector2(4,2);b.relativeForce=new Vector2(1,2);b.torque=.5f;two.GetComponent<Rigidbody2D>().gravityScale=0;
        PrefabUtility.SaveAsPrefabAsset(root,"Assets/Resources/Forces.prefab");Object.DestroyImmediate(root);AssetDatabase.SaveAssets();
    }
    public static void Record(){EditorSceneManager.OpenScene("Assets/Scenes/Probe.unity");EditorApplication.EnterPlaymode();}
}
