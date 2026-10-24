using System.Collections.Generic;
using UnityEngine;

public class ProbeAsset : ScriptableObject
{
    public int value = 42;
    public string label = "asset";
    public List<float> numbers = new List<float> { 0.5f, 1.25f };
    public ProbeNested nested = new ProbeNested();
    public Material material;
}
