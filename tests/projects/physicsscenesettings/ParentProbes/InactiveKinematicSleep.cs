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
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Run()
    {
        var output=Environment.GetEnvironmentVariable("SCENE_SETTINGS_OUT");
        if(string.IsNullOrEmpty(output)) return;
        foreach(var rb in UnityEngine.Object.FindObjectsByType<Rigidbody>(FindObjectsInactive.Include,FindObjectsSortMode.None)) rb.gameObject.SetActive(false);
        Physics.simulationMode=SimulationMode.Script; Physics.gravity=Vector3.zero;
        foreach(bool kinematic in new[]{true,false})
        foreach(string life in new[]{"initial-inactive","deactivate-settled","clone-inactive"})
        foreach(string command in new[]{"still","move-p","move-r","move-pr","position","rotation","transform"})
        {
            var go=new GameObject("inactive-control"); go.SetActive(false);
            go.AddComponent<BoxCollider>(); var rb=go.AddComponent<Rigidbody>();
            rb.useGravity=false; rb.isKinematic=kinematic;
            GameObject original=null;
            if(life=="deactivate-settled")
            {
                go.SetActive(true); Physics.SyncTransforms();
                for(int i=0;i<20;i++) Physics.Simulate(.02f);
                go.SetActive(false);
            }
            if(life=="clone-inactive") { original=go; go=UnityEngine.Object.Instantiate(go); rb=go.GetComponent<Rigidbody>(); }
            string key=(kinematic?"kinematic":"dynamic")+"/"+life+"/"+command;
            State(key+"/before",rb);
            switch(command)
            {
                case "move-p": rb.MovePosition(target); break;
                case "move-r": rb.MoveRotation(turn); break;
                case "move-pr": rb.MovePosition(target); rb.MoveRotation(turn); break;
                case "position": rb.position=target; break;
                case "rotation": rb.rotation=turn; break;
                case "transform": go.transform.SetPositionAndRotation(target,turn); break;
            }
            State(key+"/command",rb); Physics.SyncTransforms(); State(key+"/inactive-sync",rb);
            go.SetActive(true); State(key+"/activated",rb); Physics.SyncTransforms(); State(key+"/active-sync",rb);
            for(int i=0;i<20;i++) { Physics.Simulate(.02f); State(key+"/step"+i,rb); }
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
