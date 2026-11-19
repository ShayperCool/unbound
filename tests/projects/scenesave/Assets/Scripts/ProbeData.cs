using System.Collections.Generic;
using UnityEngine;

/// <summary>A ScriptableObject asset edited in Edit and Play mode.</summary>
public class ProbeData : ScriptableObject
{
    public int count = 1;
    public float ratio = 1f;
    public string label = "authored";
    public List<string> words = new List<string>();
}
