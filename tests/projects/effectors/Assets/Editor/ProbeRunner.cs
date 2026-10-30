using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
public static class ProbeRunner
{
    public static void Create()
    {
        var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);EditorSceneManager.SaveScene(scene,"Assets/Scenes/Probe.unity");EditorBuildSettings.scenes=new[]{new EditorBuildSettingsScene("Assets/Scenes/Probe.unity",true)};
        foreach(var type in EffectorProbe.Types)
        {
            var root=new GameObject("Effector");var collider=root.AddComponent<BoxCollider2D>();collider.size=new Vector2(8,.4f);collider.usedByEffector=true;var eff=(Effector2D)root.AddComponent(type);eff.useColliderMask=true;eff.colliderMask=3;
            if(eff is PlatformEffector2D p){p.surfaceArc=150;p.sideArc=30;p.rotationalOffset=15;p.useOneWay=true;p.useOneWayGrouping=true;p.useSideFriction=true;p.useSideBounce=true;}
            if(eff is SurfaceEffector2D s){s.speed=3;s.speedVariation=0;s.forceScale=.5f;s.useContactForce=true;s.useFriction=false;s.useBounce=false;}
            PrefabUtility.SaveAsPrefabAsset(root,"Assets/Resources/"+(eff is PlatformEffector2D?"platform":"surface")+".prefab");Object.DestroyImmediate(root);
        }
        foreach(var type in VolumeEffectorProbe.Types)
        {
            var root=new GameObject("Effector");var collider=root.AddComponent<BoxCollider2D>();collider.size=new Vector2(10,10);collider.isTrigger=true;collider.usedByEffector=true;var eff=(Effector2D)root.AddComponent(type);eff.useColliderMask=true;eff.colliderMask=3;
            VolumeEffectorProbe.Configure(eff);PrefabUtility.SaveAsPrefabAsset(root,"Assets/Resources/"+VolumeEffectorProbe.Kind(type)+".prefab");Object.DestroyImmediate(root);
        }
        AssetDatabase.SaveAssets();
    }
    public static void Record(){EditorSceneManager.OpenScene("Assets/Scenes/Probe.unity");EditorApplication.EnterPlaymode();}
}
