using UnityEngine;

/// <summary>On the instantiated prefab (root and child) and the scene templates: records its messages
/// with the frame, the player-loop phase and the state the object is in when it gets them.</summary>
public class ItemProbe : MonoBehaviour
{
    public static int Awakened;
    public static int Alive;
    bool m_Updated;

    string Who => (transform.parent != null ? transform.parent.name + "/" : "") + name;

    string Where()
    {
        var root = transform.root;
        return $"active={P.B(gameObject.activeInHierarchy)} root={root.name} scene={gameObject.scene.name} " +
               $"pos={P.V(transform.position)} rot={P.Q(transform.rotation)}";
    }

    void Awake()
    {
        Awakened++;
        Alive++;
        if (!AsyncProbe.Quiet) AsyncProbe.Record(Who, "Awake " + Where());
    }

    void OnEnable()
    {
        if (!AsyncProbe.Quiet) AsyncProbe.Record(Who, "OnEnable");
    }

    void Start()
    {
        if (!AsyncProbe.Quiet) AsyncProbe.Record(Who, "Start");
    }

    void Update()
    {
        if (m_Updated) return;
        m_Updated = true;
        if (!AsyncProbe.Quiet) AsyncProbe.Record(Who, "first Update");
    }

    void OnDisable()
    {
        if (!AsyncProbe.Quiet && !AsyncProbe.Finished) AsyncProbe.Record(Who, "OnDisable");
    }

    void OnDestroy()
    {
        Alive--;
        if (!AsyncProbe.Quiet && !AsyncProbe.Finished) AsyncProbe.Record(Who, "OnDestroy");
    }
}
