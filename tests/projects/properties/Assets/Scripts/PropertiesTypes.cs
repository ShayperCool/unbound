using System;
using System.Collections.Generic;
using Unity.Properties;
using UnityEngine;

/// <summary>Bags made by reflection: which members become properties, in which order.</summary>
public class ReflectedBase
{
    public int baseValue = 1;
    [SerializeField] string m_BaseHidden = "hidden";
    public static int staticValue = 9;
}

public class ReflectedThing : ReflectedBase
{
    public float speed = 2.5f;
    [SerializeField] int m_Private = 3;
    int m_NotSerialized = 4;
    [NonSerialized] public int skipped = 5;
    [DontCreateProperty] public int dontCreate = 6;
    public readonly int readOnlyField = 7;
    [CreateProperty] public int Computed => speed > 0 ? 10 : 0;
    [CreateProperty] public string Named { get; set; } = "named";
    [CreateProperty(ReadOnly = true)] public int locked = 8;
    [CreateProperty] int m_CreatedPrivate = 11;
    public Nested nested = new Nested();
    public List<int> list = new List<int> { 1, 2, 3 };
    public int[] array = { 4, 5 };
    public Dictionary<string, int> dict = new Dictionary<string, int> { { "a", 1 }, { "b", 2 } };
    public HashSet<string> set = new HashSet<string> { "x" };
    public Vector3 position = new Vector3(1, 2, 3);
    public Color color = Color.green;
    public Rect rect = new Rect(1, 2, 3, 4);
    public KindOfThing kind = KindOfThing.Second;
    public Nested missing;
}

public enum KindOfThing { First, Second, Third }

[Serializable]
public class Nested
{
    public int value = 42;
    public string text = "deep";
    public List<Nested> children = new List<Nested>();
}

public struct ValueThing
{
    public int x;
    public float y;
    [CreateProperty] public int Sum => x + (int)y;
}

/// <summary>A bag the source generator writes.</summary>
[GeneratePropertyBag]
public partial class GeneratedThing
{
    public int number = 12;
    [CreateProperty] public string Label { get; set; } = "generated";
    public List<float> values = new List<float> { 0.5f };
}

/// <summary>What the serialization package writes and reads.</summary>
[Serializable]
public class SerializedThing
{
    public int number = 3;
    public string text = "text";
    public float ratio = 0.25f;
    public bool flag = true;
    public KindOfThing kind = KindOfThing.Third;
    public Vector2 point = new Vector2(1.5f, -2);
    public List<int> numbers = new List<int> { 7, 8 };
    public Dictionary<string, float> weights = new Dictionary<string, float> { { "w", 0.5f } };
    public Nested nested = new Nested { value = 5, children = { new Nested { value = 6 } } };
    public int[] empty = new int[0];
}
