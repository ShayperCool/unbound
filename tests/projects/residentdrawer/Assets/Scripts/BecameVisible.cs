using UnityEngine;

/// <summary>A script receiving OnBecameVisible.</summary>
public class BecameVisible : MonoBehaviour
{
    public int calls;

    void OnBecameVisible() => calls++;
}
