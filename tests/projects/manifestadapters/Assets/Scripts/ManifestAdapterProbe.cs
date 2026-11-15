using System;
using System.Globalization;
using System.IO;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

public class ManifestAdapterProbe : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod] static void Install() { new GameObject("ManifestAdapterProbe").AddComponent<ManifestAdapterProbe>(); }
    [Serializable] class Row { public string key; public string value; }
    StreamWriter output;
    void Put(string key, object value) => output.WriteLine(JsonUtility.ToJson(new Row { key=key, value=Convert.ToString(value,CultureInfo.InvariantCulture) }));
    void Info(string key, AssetBundleManifest manifest)
    {
        Put(key+".name",manifest.name);
        Put(key+".bundles",string.Join("|",manifest.GetAllAssetBundles()));
        Put(key+".variants",string.Join("|",manifest.GetAllAssetBundlesWithVariant()));
        foreach(var name in new[]{"scene","things","missing","SCENE","ScEnE","THINGS"," scene","scene ","scene/","scene\\","ſcene","thıngs","THİNGS","",null})
        {
            Put(key+"."+(name??"<null>")+".hash",manifest.GetAssetBundleHash(name).ToString());
            Put(key+"."+(name??"<null>")+".direct",string.Join("|",manifest.GetDirectDependencies(name)));
            Put(key+"."+(name??"<null>")+".all",string.Join("|",manifest.GetAllDependencies(name)));
        }
    }
    void Record()
    {
        string root=Path.GetFullPath(Path.Combine(Application.dataPath,"..","Reference","bundles"));
        foreach(string variant in new[]{"lz4","lzma","none"})
        {
            var bundle=AssetBundle.LoadFromFile(Path.Combine(root,variant,variant));
            var source=bundle.LoadAsset<AssetBundleManifest>("AssetBundleManifest");
            Put(variant+".loaded",source!=null);Info(variant+".source",source);
            var names=source.GetAllAssetBundles();names[0]="changed";
            var deps=source.GetDirectDependencies("scene");deps[0]="changed";
            var all=source.GetAllDependencies("scene");all[0]="changed";
            Info(variant+".afterArrays",source);
            foreach(var flags in new[]{HideFlags.None,HideFlags.HideInHierarchy,HideFlags.DontSave,HideFlags.HideAndDontSave})
            {
                source.hideFlags=flags;
                var flagClone=Object.Instantiate(source);
                Put(variant+".flags."+(int)flags+".source",(int)source.hideFlags);
                Put(variant+".flags."+(int)flags+".clone",(int)flagClone.hideFlags);
                Object.DestroyImmediate(flagClone);
            }
            source.hideFlags=HideFlags.None;
            AssetBundleManifest clone=null;
            try { clone=Object.Instantiate(source);Put(variant+".cloneResult","ok"); }
            catch(Exception e) { Put(variant+".cloneResult",e.GetType().Name+":"+e.Message); }
            if(clone!=null)
            {
                Put(variant+".distinct",source.GetInstanceID()!=clone.GetInstanceID());
                clone.name="independent";Put(variant+".sourceName",source.name);
                Info(variant+".clone",clone);
            }
            bundle.Unload(false);Put(variant+".surviveFalse",source!=null);
            Info(variant+".retained",source);
            var reopened=AssetBundle.LoadFromFile(Path.Combine(root,variant,variant));
            var reloaded=reopened.LoadAsset<AssetBundleManifest>("AssetBundleManifest");
            Put(variant+".reloadDistinct",source!=reloaded);Info(variant+".reloaded",reloaded);
            reopened.Unload(true);Put(variant+".reloadDestroyed",reloaded==null);
            Put(variant+".oldSurvives",source!=null);
            if(clone!=null){Info(variant+".survivor",clone);Object.DestroyImmediate(clone);}
            Object.DestroyImmediate(source);
        }
    }
    void Start()
    {
        string path=Environment.GetEnvironmentVariable("MANIFEST_ADAPTER_OUT");if(string.IsNullOrEmpty(path))return;
        try { using(output=new StreamWriter(path)){Record();Put("complete",1);}EditorApplication.Exit(0); }
        catch(Exception e){Debug.LogException(e);output?.Dispose();EditorApplication.Exit(1);}
    }
}
