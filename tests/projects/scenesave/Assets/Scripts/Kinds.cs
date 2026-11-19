using System;
using System.Collections.Generic;
using UnityEngine;

public enum Mood { Calm, Busy = 5, Late = -3 }

[Serializable]
public struct Pair
{
    public int key;
    public float value;
}

[Serializable]
public class Nested
{
    public string title;
    public List<Pair> pairs = new List<Pair>();
}

/// <summary>The value kinds of Unity's text serialization: primitive arrays, structs, nested classes
/// and the built-in structs and classes with their own layouts.</summary>
public class Kinds : MonoBehaviour
{
    public List<float> floats = new List<float>();
    public bool[] flags = Array.Empty<bool>();
    public List<Mood> moods = new List<Mood>();
    public long[] longs = Array.Empty<long>();
    public byte[] bytes = Array.Empty<byte>();
    public List<double> doubles = new List<double>();
    public List<Vector3> points = new List<Vector3>();
    public List<Pair> pairs = new List<Pair>();
    public Nested nested = new Nested();
    public List<Nested> nestedList = new List<Nested>();
    public Mood mood;
    public uint unsigned;
    public short small;
    public char letter = 'a';
    public Matrix4x4 matrix = Matrix4x4.identity;
    public AnimationCurve curve = new AnimationCurve();
    public Gradient gradient = new Gradient();
    public RectOffset padding = new RectOffset();
    public Rect area;
    public LayerMask layers;
    public Bounds bounds;
    public Quaternion turn = Quaternion.identity;
    public Vector2Int cell;
    public Hash128 hash;
    public Color32 shade;
    public string multiline;
    public string empty = "";
    public List<string> texts = new List<string>();
}
