using UnityEngine;

/// <summary>An asset referenced only lazily: records when it is loaded (OnEnable).</summary>
public class LazyData : ScriptableObject
{
    public int value;

    void OnEnable()
    {
        if (Application.isPlaying) CoreProbe.Record("LazyData", "OnEnable value=" + value);
    }
}
