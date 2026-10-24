using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>A plain serializable class with every common kind of field.</summary>
[Serializable]
public class ProbeData
{
    public int number = 7;
    public float ratio = 0.1f;
    public string text = "hi \"there\"";
    public bool flag = true;
    public List<int> list = new List<int> { 1, 2 };
    public int[] array = { 3 };
    public Vector3 position = new Vector3(1, 2, 3);
    public Color color = Color.red;
    public ProbeNested nested = new ProbeNested();
    public ProbeAsset asset;
    public GameObject sceneObject;
    public Material material;
    [SerializeField] long m_Private = 5;
}

[Serializable]
public class ProbeNested
{
    public string name = "nested";
    public List<string> tags = new List<string>();
}

/// <summary>Fields left without initializers: what deserialization leaves in them.</summary>
[Serializable]
public class ProbeEmpty
{
    public List<int> list;
    public int[] array;
    public string text;
    public ProbeNested nested;
    public int number;
}

/// <summary>A struct whose callback sets a field (the callback must reach the stored copy).</summary>
[Serializable]
public struct ProbeCallbackStruct : ISerializationCallbackReceiver
{
    public string id;
    [NonSerialized] public string resolved;

    public void OnBeforeSerialize() { }

    public void OnAfterDeserialize() => resolved = "resolved:" + id;
}

[Serializable]
public class ProbeCallbackHolder
{
    public ProbeCallbackStruct single;
    public List<ProbeCallbackStruct> many = new List<ProbeCallbackStruct>();
}
