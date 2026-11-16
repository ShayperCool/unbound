using UnityEngine;

/// <summary>A scriptable object with unassigned references: an asset and one made at runtime.</summary>
public class DataHolder : ScriptableObject
{
    public GameObject target;
    public GameObject[] items;
    public Nested nested;
}
