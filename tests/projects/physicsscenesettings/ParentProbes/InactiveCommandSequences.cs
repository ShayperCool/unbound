using System;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

// Commands issued while the actor is outside the scene, followed by twenty active steps.
public static class SceneSettingsProbe
{
    static readonly StringBuilder rows = new StringBuilder();
    static readonly Vector3 target = new Vector3(2,0,0);
    static readonly Quaternion turn = new Quaternion(0,1,0,0);
    static void State(string key, Rigidbody rb)
    {
        var p=rb.position; var q=rb.rotation; var t=rb.transform.position; var r=rb.transform.rotation;
        var v=rb.linearVelocity; var w=rb.angularVelocity;
        float[] values={rb.IsSleeping()?1:0,p.x,p.y,p.z,q.x,q.y,q.z,q.w,t.x,t.y,t.z,r.x,r.y,r.z,r.w,v.x,v.y,v.z,w.x,w.y,w.z};
        rows.Append("{\"key\":\"").Append(key).Append("\",\"value\":[");
        for(int i=0;i<values.Length;i++)
        {
            if(i!=0) rows.Append(',');
            rows.Append(values[i].ToString("R",CultureInfo.InvariantCulture));
        }
        rows.Append("]}\n");
    }
    static readonly Vector3 start = new Vector3(3,4,5), other = new Vector3(7,8,9);
    static readonly Quaternion initial = new Quaternion(1,0,0,0), final = new Quaternion(0,0,1,0);
    static void Command(string cmd, Rigidbody rb)
    {
        switch(cmd)
        {
            case "mp": rb.MovePosition(target); break;
            case "mr": rb.MoveRotation(turn); break;
            case "m": rb.Move(target,turn); break;
            case "p": rb.position=other; break;
            case "r": rb.rotation=final; break;
            case "t": rb.transform.SetPositionAndRotation(target,turn); break;
            case "s": rb.Sleep(); break;
            case "w": rb.WakeUp(); break;
            case "v": rb.linearVelocity=new Vector3(1,2,3); rb.angularVelocity=new Vector3(0,1,0); break;
            case "sync": Physics.SyncTransforms(); break;
            case "pub": rb.PublishTransform(); break;
        }
    }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Run()
    {
        var output=Environment.GetEnvironmentVariable("SCENE_SETTINGS_OUT");
        if(string.IsNullOrEmpty(output)) return;
        foreach(var body in UnityEngine.Object.FindObjectsByType<Rigidbody>(FindObjectsInactive.Include,FindObjectsSortMode.None)) body.gameObject.SetActive(false);
        Physics.simulationMode=SimulationMode.Script; Physics.gravity=Vector3.zero;
        foreach(bool kinematic in new[]{true,false})
        foreach(string life in new[]{"initial-before-add","initial-after-add","deactivate-awake","deactivate-sleep","clone-initial","clone-deactivated"})
        foreach(string sequence in new[]{"mp,mr,m","p,r","r,p","p,mp,mr","mp,mr,p,r","t,p,r","p,r,t","t,sync,p","p,r,pub","s,w","v","p,v,s","t,v","p,r,sync,t,sync"})
        {
            var go=new GameObject("inactive-sequences"); go.SetActive(false);
            if(life!="initial-after-add") go.transform.SetPositionAndRotation(start,initial);
            go.AddComponent<BoxCollider>(); var rb=go.AddComponent<Rigidbody>();
            rb.useGravity=false; rb.isKinematic=kinematic; rb.linearDamping=0; rb.angularDamping=0;
            if(life=="initial-after-add") go.transform.SetPositionAndRotation(start,initial);
            GameObject original=null;
            if(life.StartsWith("deactivate") || life=="clone-deactivated")
            {
                go.SetActive(true); Physics.SyncTransforms();
                if(!kinematic) { rb.linearVelocity=new Vector3(1,0,0); rb.angularVelocity=Vector3.zero; }
                Physics.Simulate(.02f);
                if(life=="deactivate-sleep") rb.Sleep();
                go.SetActive(false);
            }
            if(life.StartsWith("clone")) { original=go; go=UnityEngine.Object.Instantiate(go); rb=go.GetComponent<Rigidbody>(); }
            string key=(kinematic?"kinematic":"dynamic")+"/"+life+"/"+sequence;
            State(key+"/before",rb);
            int n=0;
            foreach(var cmd in sequence.Split(',')) { Command(cmd,rb); State(key+"/cmd"+(n++)+"-"+cmd,rb); }
            Physics.SyncTransforms(); State(key+"/inactive-sync",rb);
            go.SetActive(true); State(key+"/activated",rb); Physics.SyncTransforms(); State(key+"/active-sync",rb);
            for(int i=0;i<20;i++) { Physics.Simulate(.02f); State(key+"/step"+i,rb); }
            go.SetActive(false); State(key+"/deactivated-again",rb);
            UnityEngine.Object.DestroyImmediate(go);
            if(original!=null) UnityEngine.Object.DestroyImmediate(original);
            Physics.SyncTransforms();
        }
        File.WriteAllText(output,rows.ToString());
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(0);
#else
        Application.Quit();
#endif
    }
}
