using System;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>A data source asset referenced from UXML (`data-source`).</summary>
public class BindData : ScriptableObject
{
    public string message;
    public int count;
    public float width;
    public float opacity;
    public Color color;
    public DisplayStyle display;
    public BindInnerData inner = new BindInnerData();
}

[Serializable]
public class BindInnerData
{
    public string value;
}
