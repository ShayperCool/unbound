using System;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

// Independent Native controls for command timing, target replacement and eventual sleep.
public static class SceneSettingsProbe
{
    static readonly StringBuilder rows = new StringBuilder();
    static readonly Vector3 target = new Vector3(2,0,0), teleport = new Vector3(4,0,0);
    static readonly Quaternion turn = new Quaternion(0,1,0,0);
    static void Record(string key, params float[] values)
    {
        rows.Append("{\"key\":\"").Append(key).Append("\",\"value\":[");
        for (int i=0;i<values.Length;i++)
        {
            if(i!=0) rows.Append(',');
            rows.Append(values[i].ToString("R",CultureInfo.InvariantCulture));
        }
        rows.Append("]}\n");
    }
    static void State(string key, Rigidbody rb)
    {
        var p=rb.position; var q=rb.rotation; var t=rb.transform.position; var r=rb.transform.rotation;
        var v=rb.linearVelocity; var w=rb.angularVelocity;
        Record(key+"/sleep",rb.IsSleeping()?1:0);
        Record(key+"/pose",p.x,p.y,p.z,q.x,q.y,q.z,q.w,t.x,t.y,t.z,r.x,r.y,r.z,r.w);
        Record(key+"/velocity",v.x,v.y,v.z,w.x,w.y,w.z);
    }
    static void Command(string command, Rigidbody rb)
    {
        switch(command)
        {
            case "still": break;
            case "move-p": rb.MovePosition(target); break;
            case "move-r": rb.MoveRotation(turn); break;
            case "move-pr": rb.MovePosition(target); rb.MoveRotation(turn); break;
            case "move-rp": rb.MoveRotation(turn); rb.MovePosition(target); break;
            case "move-pp": rb.MovePosition(target); rb.MovePosition(teleport); break;
            case "move-same": rb.MovePosition(rb.position); break;
            case "transform-p": rb.transform.position=teleport; Physics.SyncTransforms(); break;
            case "transform-r": rb.transform.rotation=turn; Physics.SyncTransforms(); break;
            case "transform-same": rb.transform.position=rb.transform.position; Physics.SyncTransforms(); break;
            case "repeat-sync": Physics.SyncTransforms(); Physics.SyncTransforms(); break;
            case "move-transform": rb.MovePosition(target); rb.transform.position=teleport; Physics.SyncTransforms(); break;
            case "transform-move": rb.transform.position=teleport; Physics.SyncTransforms(); rb.MovePosition(target); break;
            case "move-position": rb.MovePosition(target); rb.position=teleport; break;
            case "move-rotation": rb.MovePosition(target); rb.rotation=turn; break;
            case "rotate-position": rb.MoveRotation(turn); rb.position=teleport; break;
            case "position-move": rb.position=teleport; rb.MovePosition(target); break;
            case "rotation-move": rb.rotation=turn; rb.MovePosition(target); break;
            case "position": rb.position=teleport; break;
            case "rotation": rb.rotation=turn; break;
            case "position-same": rb.position=rb.position; break;
            case "rotation-same": rb.rotation=rb.rotation; break;
            case "move-sleep": rb.MovePosition(target); rb.Sleep(); break;
            case "sleep-move": rb.Sleep(); rb.MovePosition(target); break;
            case "move-inactive": rb.MovePosition(target); rb.gameObject.SetActive(false); rb.gameObject.SetActive(true); break;
        }
    }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Run()
    {
        var output=Environment.GetEnvironmentVariable("SCENE_SETTINGS_OUT");
        if(string.IsNullOrEmpty(output)) return;
        foreach(var rb in UnityEngine.Object.FindObjectsByType<Rigidbody>(FindObjectsInactive.Include,FindObjectsSortMode.None)) rb.gameObject.SetActive(false);
        Physics.simulationMode=SimulationMode.Script; Physics.gravity=Vector3.zero;
        foreach(string type in new[]{"kinematic","dynamic","sleeping-dynamic"})
        foreach(string life in new[]{"fresh","inactive","reactivate","clone"})
        foreach(string command in new[]{"still","move-p","move-r","move-pr","move-rp","move-pp","move-same","transform-p","transform-r","transform-same","repeat-sync","move-transform","transform-move","move-position","move-rotation","rotate-position","position-move","rotation-move","position","rotation","position-same","rotation-same","move-sleep","sleep-move","move-inactive"})
        {
            var go=new GameObject("sleep-control"); go.SetActive(life!="inactive");
            go.AddComponent<BoxCollider>(); var body=go.AddComponent<Rigidbody>();
            body.useGravity=false; body.linearDamping=body.angularDamping=0; body.isKinematic=type=="kinematic";
            GameObject original=null;
            if(life=="clone") { original=go; go=UnityEngine.Object.Instantiate(go); original.SetActive(false); body=go.GetComponent<Rigidbody>(); }
            if(life=="inactive") go.SetActive(true);
            if(life=="reactivate") { go.SetActive(false); go.SetActive(true); }
            Physics.SyncTransforms();
            for(int i=0;i<20;i++) Physics.Simulate(.02f);
            if(type=="sleeping-dynamic") body.Sleep();
            else if(type=="dynamic") body.WakeUp();
            string key=type+"/"+life+"/"+command;
            State(key+"/before",body);
            Command(command,body); State(key+"/command",body);
            Physics.SyncTransforms(); State(key+"/sync",body);
            for(int i=0;i<20;i++) { Physics.Simulate(.02f); State(key+"/step"+i,body); }
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
