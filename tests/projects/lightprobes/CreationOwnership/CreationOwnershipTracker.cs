using UnityEngine;

/// <summary>
/// Records one object's message order for <see cref="SceneCreationOwnership"/>: Awake, OnEnable,
/// Start, the first Update of every witness epoch, OnDisable and OnDestroy, with the object's
/// scene and the active scene at that moment. The alias is the stable name the controller gave
/// before the object was made (never a handle or instance id). With <see cref="createOnDestroy"/>
/// set, its OnDestroy makes a new root (control (iv)).
///
/// Every message returns before touching the engine once the witness has closed its output, so
/// nothing is queried or written while the Editor shuts down.
/// </summary>
public class CreationOwnershipTracker : MonoBehaviour
{
    public bool createOnDestroy;

    [System.NonSerialized] public string alias;
    [System.NonSerialized] public string status = "";
    [System.NonSerialized] public int updateEpoch = -1;

    void Note(string message)
    {
        if (!SceneCreationOwnership.Open) return;
        status = status.Length == 0 ? message : status + " " + message;
        SceneCreationOwnership.Lifecycle(this, message);
    }

    void Awake()
    {
        if (!SceneCreationOwnership.Open) return;
        alias = SceneCreationOwnership.TakeAlias(gameObject);
        Note("Awake");
    }

    void OnEnable() => Note("OnEnable");

    void Start() => Note("Start");

    void Update()
    {
        if (!SceneCreationOwnership.Open || updateEpoch == SceneCreationOwnership.Epoch) return;
        updateEpoch = SceneCreationOwnership.Epoch;
        SceneCreationOwnership.Lifecycle(this, "Update");
    }

    void OnDisable() => Note("OnDisable");

    void OnDestroy()
    {
        Note("OnDestroy");
        if (createOnDestroy && SceneCreationOwnership.Open)
            SceneCreationOwnership.CreateInOnDestroy();
    }
}
