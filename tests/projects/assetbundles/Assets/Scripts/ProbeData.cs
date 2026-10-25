using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>A ScriptableObject asset in the bundle.</summary>
public class ProbeData : ScriptableObject
{
    public int level = 7;
    public string title = "data";
    public Vector2 range = new Vector2(-3f, 9.5f);
    public List<string> tags = new List<string> { "a", "bb", "ccc" };
}
