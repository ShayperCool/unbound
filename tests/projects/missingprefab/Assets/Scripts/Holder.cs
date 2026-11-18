using UnityEngine;

// Serialized references into a nested prefab instance; Awake records what they read then.
public class Holder : MonoBehaviour
{
    public GameObject[] objects;
    public Transform childTransform;
    public Marker marker;
    public BoxCollider box;
    public SphereCollider sphere;
    public GameObject spawn;
    public Component[] parts;

    void Awake()
    {
        ProbeLog.Refs("awake/" + name + "#" + ProbeLog.Next("awake/" + name), this);
    }
}
