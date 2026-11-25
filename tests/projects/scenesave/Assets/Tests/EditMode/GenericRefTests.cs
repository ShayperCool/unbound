using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Edit mode: a scriptable object whose [SerializeReference] fields hold objects of generic classes,
/// made an asset by CreateAsset and saved by SaveAssets after a change; run.py compares the file.
/// </summary>
public class GenericRefTests
{
    const string Path = "Assets/Data/GenericRefs.asset";

    [Test]
    public void GenericRefs()
    {
        var refs = ScriptableObject.CreateInstance<GenericRefs>();
        refs.one = new Box<int> { value = 3 };
        refs.two = new Pair<string, Vector3> { first = "a", second = new Vector3(1, 2, 3) };
        refs.many.Add(new Box<Box<float>> { value = new Box<float> { value = 0.5f } });
        refs.many.Add(new Box<List<int>> { value = new List<int> { 1, 2 } });
        refs.many.Add(new Probe.Generic.Tagged<Circle> { tag = "t", item = new Circle() });
        refs.many.Add(refs.one);
        AssetDatabase.CreateAsset(refs, Path);
        ProbeLog.Snapshot("created.file", Path);
        refs.many.Add(new Box<string> { value = "later" });
        EditorUtility.SetDirty(refs);
        AssetDatabase.SaveAssets();
        ProbeLog.Snapshot("saved.file", Path);
    }
}
