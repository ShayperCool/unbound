using UnityEngine;

/// <summary>A rig script that edits its own hierarchy from OnValidate when the probe asks
/// (<see cref="Mode"/>), as a loaded prefab asset receives OnValidate in the Editor.</summary>
public class LimbRig : MonoBehaviour
{
    public static int Validations;
    public static string Mode = "";
    public Transform limb;
    public Transform newParent;

    void OnValidate()
    {
        if (!ProbeLog.Active) return;
        Validations++;
        ProbeLog.Event("LimbRig.OnValidate " + name + " " + Mode);
        if (Mode == "reparent" && limb != null && newParent != null && limb.parent != newParent)
            limb.SetParent(newParent, false);
        else if (Mode == "add" && limb != null && limb.Find("Added") == null)
            new GameObject("Added").transform.SetParent(limb, false);
    }
}
