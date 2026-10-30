using UnityEngine;
public partial class AnimatorProbe
{
    void ReflectionMotion()
    {
        foreach(bool apply in new[]{false,true})
        foreach(bool nested in new[]{false,true})
        {
            var top=new GameObject("Top");top.transform.rotation=Quaternion.Euler(10,40,20);top.transform.localScale=new Vector3(2,1,3);
            var parent=new GameObject("Parent");if(nested)parent.transform.SetParent(top.transform,false);
            parent.transform.rotation=Quaternion.Euler(20,30,10);parent.transform.localScale=new Vector3(-2,3,1.5f);
            var root=new GameObject("Root");root.transform.SetParent(parent.transform,false);root.transform.localRotation=Quaternion.Euler(15,25,35);
            new GameObject("Child").transform.SetParent(root.transform,false);
            var a=root.AddComponent<Animator>();a.applyRootMotion=apply;a.runtimeAnimatorController=Resources.Load<RuntimeAnimatorController>("RootMotionPlain");a.Update(0);
            for(int i=0;i<5;i++)
            {
                string key="root.reflection."+B(apply)+"."+B(nested)+"."+i;
                a.Update(.2f);Value(key+".eval",Snapshot(a));Value(key+".local",Q(root.transform.localRotation));Value(key+".scale",V(root.transform.lossyScale));
                if(i==1)root.transform.localRotation=root.transform.localRotation;
                if(i==2)root.transform.rotation=root.transform.rotation;
                if(i==3)root.transform.localPosition=root.transform.localPosition;
                if(i==4)parent.transform.localScale=new Vector3(-3,2,1);
                Value(key+".reset",Snapshot(a));Value(key+".reset_local",Q(root.transform.localRotation));Value(key+".reset_scale",V(root.transform.lossyScale));
            }
            DestroyImmediate(parent);DestroyImmediate(top);
        }
    }
}
