using UnityEngine;

/// <summary>A scriptable object type for fields and destroyed-object controls.</summary>
public class Data : ScriptableObject
{
    public int value;
}

/// <summary>An abstract scriptable object type: CreateInstance refuses it.</summary>
public abstract class AbstractData : ScriptableObject { }
