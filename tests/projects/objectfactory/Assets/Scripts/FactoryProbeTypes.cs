using UnityEngine;

/// <summary>A component that requires another (ObjectFactory adds what it requires).</summary>
[RequireComponent(typeof(Rigidbody))]
public class NeedsBody : MonoBehaviour
{
    public int value = 3;
    void Reset() { value = 7; }
}

/// <summary>A component a GameObject may hold one of.</summary>
[DisallowMultipleComponent]
public class OnlyOne : MonoBehaviour { }

/// <summary>A ScriptableObject made by ObjectFactory.CreateInstance.</summary>
public class FactoryAsset : ScriptableObject
{
    public int value = 5;
    void Reset() { value = 9; }
}
