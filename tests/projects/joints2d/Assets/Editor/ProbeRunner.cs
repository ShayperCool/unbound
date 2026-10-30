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
        foreach(var type in JointFamilyProbe.Types)
        {
            var root=new GameObject("Setup");var a=new GameObject("Body");a.transform.SetParent(root.transform,false);a.transform.localPosition=new Vector3(1,-2,0);a.transform.localRotation=Quaternion.Euler(0,0,30);a.AddComponent<Rigidbody2D>().mass=2;
            var b=new GameObject("Anchor");b.transform.SetParent(root.transform,false);b.transform.position=new Vector3(-1,2,0);b.transform.rotation=Quaternion.Euler(0,0,-20);var rb=b.AddComponent<Rigidbody2D>();rb.bodyType=RigidbodyType2D.Kinematic;
            var j=(AnchoredJoint2D)a.AddComponent(type);j.connectedBody=rb;j.autoConfigureConnectedAnchor=false;j.anchor=new Vector2(.2f,-.3f);j.connectedAnchor=new Vector2(.1f,.4f);j.enableCollision=true;j.breakForce=100;j.breakTorque=200;j.breakAction=JointBreakAction2D.CallbackOnly;
            if(j is HingeJoint2D h){h.useMotor=true;h.motor=new JointMotor2D{motorSpeed=45,maxMotorTorque=2};h.useLimits=true;h.limits=new JointAngleLimits2D{min=-20,max=35};h.useConnectedAnchor=false;}
            if(j is FixedJoint2D x){x.frequency=3;x.dampingRatio=.65f;}
            if(j is FrictionJoint2D f){f.maxForce=4;f.maxTorque=2;}
            if(j is WheelJoint2D w){w.suspension=new JointSuspension2D{frequency=3,dampingRatio=.65f,angle=25};w.motor=new JointMotor2D{motorSpeed=45,maxMotorTorque=2};}
            if(j is SliderJoint2D sl){sl.autoConfigureAngle=false;sl.angle=35;sl.motor=new JointMotor2D{motorSpeed=2,maxMotorTorque=4};sl.limits=new JointTranslationLimits2D{min=-.3f,max=.5f};}
            PrefabUtility.SaveAsPrefabAsset(root,"Assets/Resources/"+type.Name.Replace("Joint2D","")+".prefab");Object.DestroyImmediate(root);
        }
        AssetDatabase.SaveAssets();
    }
    public static void Record(){EditorSceneManager.OpenScene("Assets/Scenes/Probe.unity");EditorApplication.EnterPlaymode();}
}
