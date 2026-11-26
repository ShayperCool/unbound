using UnityEngine;

/// <summary>A script asset of tests/projects/urprendererwrites that marks itself dirty from one of
/// its load callbacks (Awake, OnEnable or OnValidate), with a field changed or not.</summary>
public class DirtyOnLoad : ScriptableObject
{
    public string when;
    public bool change;
    public int number;

    void Mark(string callback)
    {
#if UNITY_EDITOR
        if (callback != when) return;
        if (change) number++;
        UnityEditor.EditorUtility.SetDirty(this);
#endif
    }

    void Awake() => Mark("Awake");
    void OnEnable() => Mark("OnEnable");
    void OnValidate() => Mark("OnValidate");
}
