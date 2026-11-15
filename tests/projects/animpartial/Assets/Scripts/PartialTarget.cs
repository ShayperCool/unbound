using System.Collections.Generic;
using UnityEngine;

public class PartialTarget : MonoBehaviour
{
    public float f = 0.5f;
    public float g = 3f;
    public bool b = true;
    public int i = 7;

    /// <summary>The animation events received (their string parameters), in order.</summary>
    public readonly List<string> Hits = new List<string>();

    public void Hit(string name) => Hits.Add(name);
}
