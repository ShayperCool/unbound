using UnityEngine;

/// <summary>A script that receives only OnBecameInvisible (the drawer refuses its renderer).</summary>
public class BecameInvisible : MonoBehaviour
{
    public int calls;

    void OnBecameInvisible() => calls++;
}
