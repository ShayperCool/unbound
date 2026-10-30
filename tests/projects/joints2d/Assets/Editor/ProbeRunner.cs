using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
public static class ProbeRunner
{
    public static void Create()
    {
        var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
        EditorSceneManager.SaveScene(scene,"Assets/Scenes/Probe.unity");EditorBuildSettings.scenes=new[]{new EditorBuildSettingsScene("Assets/Scenes/Probe.unity",true)};
        foreach(bool spring in new[]{false,true})
        {
            var root=new GameObject("Setup");var a=new GameObject("Body");a.transform.SetParent(root.transform,false);a.transform.localPosition=new Vector3(1,-2,0);a.AddComponent<Rigidbody2D>().mass=2;
            var b=new GameObject("Anchor");b.transform.SetParent(root.transform,false);b.transform.position=new Vector3(-1,2,0);var rb=b.AddComponent<Rigidbody2D>();rb.bodyType=RigidbodyType2D.Kinematic;
            var j=(AnchoredJoint2D)a.AddComponent(spring?typeof(SpringJoint2D):typeof(DistanceJoint2D));j.connectedBody=rb;j.autoConfigureConnectedAnchor=false;j.anchor=new Vector2(.2f,-.3f);j.connectedAnchor=new Vector2(.1f,.4f);j.enableCollision=true;j.breakForce=100;j.breakTorque=200;j.breakAction=JointBreakAction2D.CallbackOnly;
            if(j is DistanceJoint2D d){d.autoConfigureDistance=false;d.distance=2.5f;d.maxDistanceOnly=true;}
            if(j is SpringJoint2D s){s.autoConfigureDistance=false;s.distance=2.5f;s.frequency=3;s.dampingRatio=.65f;}
            PrefabUtility.SaveAsPrefabAsset(root,"Assets/Resources/"+(spring?"Spring":"Distance")+".prefab");Object.DestroyImmediate(root);
        }
        AssetDatabase.SaveAssets();
    }
    public static void Record(){EditorSceneManager.OpenScene("Assets/Scenes/Probe.unity");EditorApplication.EnterPlaymode();}
}
