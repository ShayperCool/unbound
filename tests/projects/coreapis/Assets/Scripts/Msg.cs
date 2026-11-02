using UnityEngine;

/// <summary>Records its lifecycle messages under the object's scene, path and sibling index.</summary>
public class Msg : MonoBehaviour
{
    public Object target;
    /// <summary>Deactivated from this object's OnDisable (a reentrant change inside a batch).</summary>
    public GameObject deactivateOnDisable;

    public string Who
    {
        get
        {
            string path = name + "[" + transform.GetSiblingIndex() + "]";
            for (var t = transform.parent; t != null; t = t.parent) path = t.name + "[" + t.GetSiblingIndex() + "]/" + path;
            return gameObject.scene.name + ":" + path;
        }
    }

    // The scene's own load (its Awake/Start order) is not what the probe measures.
    static bool On => CoreProbe.Frame >= 1 && !CoreProbe.Finished;

    void Awake() { if (On) CoreProbe.Record(Who, "Awake active=" + P.B(gameObject.activeInHierarchy)); }
    void OnEnable() { if (On) CoreProbe.Record(Who, "OnEnable"); }
    void Start() { if (On) CoreProbe.Record(Who, "Start"); }

    void OnDisable()
    {
        if (!On) return;
        CoreProbe.Record(Who, "OnDisable");
        if (deactivateOnDisable != null)
        {
            CoreProbe.Record(Who, "deactivates " + deactivateOnDisable.name);
            deactivateOnDisable.SetActive(false);
        }
    }

    void OnDestroy()
    {
        if (On) CoreProbe.Record(Who, "OnDestroy");
    }
}
