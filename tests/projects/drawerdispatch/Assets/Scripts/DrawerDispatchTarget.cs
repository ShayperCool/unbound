using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable] public class CustomShape { public int size = 3; }
[Serializable] public class BaseNo { public int n = 1; }
[Serializable] public class DerivedNo : BaseNo { public int extra = 2; }
[Serializable] public class BaseYes { public int y = 1; }
[Serializable] public class DerivedYes : BaseYes { public int extra = 2; }
public interface IMarker { }
[Serializable] public class MarkedClass : IMarker { public int m = 5; }
[Serializable] public class Wrapper<T> { public T value; }
[Serializable] public class ImguiOnly { public int i = 7; }
[Serializable] public class Counted { public int c = 1; }
[Serializable] public class Composite { public int first = 1; public string second = "two"; }

public class TaggedAttribute : PropertyAttribute
{
    public readonly string tag;
    public TaggedAttribute(string tag) { this.tag = tag; }
}

public class OuterAttribute : PropertyAttribute { }
public class InnerAttribute : PropertyAttribute { }

public class WholeListAttribute : PropertyAttribute
{
    public WholeListAttribute() : base(true) { }
}

public class BannerAttribute : PropertyAttribute
{
    public readonly string text;
    public BannerAttribute(string text) { this.text = text; }
}

public class ImguiBannerAttribute : PropertyAttribute { }

public class DrawerDispatchTarget : ScriptableObject
{
    public CustomShape shape = new CustomShape();
    public DerivedNo derivedNo = new DerivedNo();
    public DerivedYes derivedYes = new DerivedYes();
    public MarkedClass marked = new MarkedClass();
    public Wrapper<int> wrapped = new Wrapper<int> { value = 4 };
    [Tagged("alpha")] public int tagged = 11;
    [Outer(order = 2)] [Inner(order = 1)] public int ordered = 12;
    public ImguiOnly imgui = new ImguiOnly();
    [Tagged("each")] public int[] taggedArray = { 1, 2 };
    [WholeList] public List<int> wholeList = new List<int> { 3 };
    [Header("Head")] [Space(12)] public int decorated = 13;
    [Banner("flag")] [ImguiBanner] public int customDecorated = 14;
    [Header("ListHead")] public int[] headedArray = { 5 };
    [Range(0f, 1f)] public float rangeFloat = 0.25f;
    [Range(0, 10)] public int rangeInt = 4;
    [Range(0, 1)] public string rangeString = "s";
    [Min(2f)] public float minFloat = 3f;
    [Delayed] public string delayedString = "d";
    [Multiline(3)] public string multiline = "m";
    [TextArea(2, 4)] public string textArea = "t";
    public Counted counted = new Counted();
    public Composite composite = new Composite();
}
