using System.Collections.Generic;
using UnityEngine;

/// <summary>The ScriptableObject the probe creates as an asset and as a sub-asset.</summary>
public class ProjectWritesData : ScriptableObject
{
    public int number = 7;
    public string text = "made";
    public List<int> list = new List<int> { 1, 2 };
    public ProjectWritesData other;
}
