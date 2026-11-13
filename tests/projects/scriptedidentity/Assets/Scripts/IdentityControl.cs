using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;
using UnityEditor;
public sealed class IdentityControl : MonoBehaviour
{
    static readonly List<string> Rows=new List<string>();
    static void Put(string k,object v) => Rows.Add("{\"k\":\""+k+"\",\"v\":\""+Convert.ToString(v,CultureInfo.InvariantCulture)+"\"}");
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Begin() { if(Environment.GetEnvironmentVariable("SERIALIZED_BINDING_OUT")!=null)new GameObject("Identity control").AddComponent<IdentityControl>(); }
    void Start()
    {
        try
        {
            foreach(string label in new[]{"Empty","Named","Legacy","WrongClass","WrongName","BogusClass","LegacyDefault","LegacyV1","BothDefault","BothV1","BothV2","InternalV1","PartialBothV1","WrongModernV1","LegacyAliases"})
            {
                string path="Assets/Imports/"+label+".identityprobe";
                var main=AssetDatabase.LoadMainAssetAtPath(path);
                var all=AssetDatabase.LoadAllAssetsAtPath(path);
                Put(label+"/count",all.Length);
                foreach(var obj in all)
                {
                    string kind=obj==main?"main":obj is Texture2D?"texture":"lighting";
                    AssetDatabase.TryGetGUIDAndLocalFileIdentifier(obj,out string guid,out long id);
                    Put(label+"/"+kind+"/id",id);
                    Put(label+"/"+kind+"/type",obj.GetType().Name);
                    if(obj!=main)Put(label+"/"+kind+"/name",obj.name);
                }
            }
            Put("complete",1);
        }
        catch(Exception e) { Put("error",e.GetType().Name); Debug.LogException(e); }
        File.WriteAllLines(Environment.GetEnvironmentVariable("SERIALIZED_BINDING_OUT"),Rows); EditorApplication.Exit(0);
    }
}
