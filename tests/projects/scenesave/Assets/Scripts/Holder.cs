using System.Collections.Generic;
using UnityEngine;

/// <summary>Plain serialized fields of the kinds a scene save writes.</summary>
public class Holder : MonoBehaviour
{
    public int count = 3;
    public float ratio = 0.5f;
    public double precise = 0.25;
    public string label = "plain";
    public Vector3 offset;
    public Color tint = Color.white;
    public List<int> numbers = new List<int>();
    public List<string> words = new List<string>();
    public GameObject target;
    public Material material;
    public ProbeData data;
}
