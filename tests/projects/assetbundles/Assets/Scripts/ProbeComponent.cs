using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>A component with serialized fields of several kinds, in the bundled prefab.</summary>
public class ProbeComponent : MonoBehaviour
{
    public int count = 3;
    public string label = "bundled";
    public float[] weights = { 0.5f, 1.25f, -2f };
    public List<Vector3> points = new List<Vector3> { new Vector3(1, 2, 3), new Vector3(-1, 0.5f, 4) };
    public ProbeData data;
    public Nested nested = new Nested();
    public Material material;
    public GameObject child;

    [Serializable]
    public class Nested
    {
        public string name = "inner";
        public Color tint = new Color(0.2f, 0.4f, 0.6f, 1f);
        public AnimationCurve curve = AnimationCurve.Linear(0, 0, 1, 2);
    }
}
