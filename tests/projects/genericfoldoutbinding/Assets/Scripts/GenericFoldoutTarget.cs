using System;
using UnityEngine;

[Serializable]
public struct BasicInlineData
{
    public int count;
    public string label;
    public bool flag;
}

[Serializable]
public struct SmallInlineData
{
    public float weight;
}

[Serializable]
public struct EmptyInlineData
{
}

[Serializable]
public struct HiddenOnlyInlineData
{
    [HideInInspector] public int secret;
}

[Serializable]
public struct MixedVisibilityInlineData
{
    public int visibleInt;
    [HideInInspector] public int hiddenInt;
    public string visibleName;
}

[Serializable]
public struct InnerInlineData
{
    public int code;
    public string tag;
}

[Serializable]
public class OuterInlineClass
{
    public string title;
    public InnerInlineData inner;
    public Vector3 offset;
    public Color tint;
}

[Serializable]
public struct StringFirstInlineData
{
    public string name;
    public int level;
}

public class GenericFoldoutTarget : ScriptableObject
{
    public BasicInlineData basic = new BasicInlineData { count = 7, label = "alpha", flag = true };
    public BasicInlineData preExpanded = new BasicInlineData { count = 19, label = "beta", flag = false };
    public SmallInlineData small = new SmallInlineData { weight = 2.5f };
    public EmptyInlineData emptyStruct;
    public HiddenOnlyInlineData hiddenOnly = new HiddenOnlyInlineData { secret = 42 };
    public MixedVisibilityInlineData mixedVis = new MixedVisibilityInlineData { visibleInt = 11, hiddenInt = 99, visibleName = "shown" };
    public OuterInlineClass outer = new OuterInlineClass
    {
        title = "root-title",
        inner = new InnerInlineData { code = 101, tag = "in-tag" },
        offset = new Vector3(1f, 2f, 3f),
        tint = new Color(0.25f, 0.5f, 0.75f, 1f)
    };
    public StringFirstInlineData stringFirst = new StringFirstInlineData { name = "Hero", level = 3 };
    public StringFirstInlineData[] stringFirstArray = new[]
    {
        new StringFirstInlineData { name = "Knight", level = 5 },
        new StringFirstInlineData { name = "", level = 1 }
    };
    public int topInt = 55;
}
