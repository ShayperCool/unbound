using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>A tall range: its drawer (ProbeRangeDrawer) gives fields 42 pixels.</summary>
public class ProbeRangeAttribute : PropertyAttribute { }

[Serializable]
public struct ProbePair
{
    public int a;
    public string b;
}

/// <summary>The data the probe's SerializedObjects, editors and property heights work on.</summary>
public class ProbeData : ScriptableObject
{
    public int number = 3;
    public string text = "hello";
    public Vector3 position = new Vector3(1, 2, 3);
    public ProbePair pair;
    public List<int> list = new List<int> { 1, 2 };
    [ProbeRange] public float ranged = 0.5f;
    public bool flag;
    public Color color = Color.red;
    public KeyCode key = KeyCode.A;
    public Rect rect;
    public Bounds bounds;
    public AnimationCurve curve = AnimationCurve.Linear(0, 0, 1, 1);
    [Multiline] public string notes = "a";
    [TextArea] public string area = "b";
    [Range(0, 10)] public int slider = 5;
    [Header("Header")] public int afterHeader;
    [Space(20)] public int afterSpace;
    public List<int> empty = new List<int>();
    public List<int> one = new List<int> { 7 };
    public int[] three = { 1, 2, 3 };
    public List<ProbePair> pairs = new List<ProbePair> { new ProbePair(), new ProbePair() };
    public Vector2 v2;
    public Vector4 v4;
    public Quaternion rotation = Quaternion.identity;
    public Vector3Int v3i;
    [Multiline(5)] public string five = "x";
    [Min(1)] public float min = 2;
}
