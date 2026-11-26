using UnityEngine;

/// <summary>A behaviour of tests/projects/urprendererwrites that marks itself dirty from OnValidate.</summary>
public class DirtyOnValidate : MonoBehaviour
{
    public int number;

#if UNITY_EDITOR
    void OnValidate()
    {
        if (!Application.isPlaying) return;
        number++;
        UnityEditor.EditorUtility.SetDirty(this);
    }
#endif
}
