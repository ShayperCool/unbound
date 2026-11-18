using System;
using System.Collections.Generic;

/// <summary>A scriptable object whose only constructor takes an argument.</summary>
public class ArgOnlyData : UnityEngine.ScriptableObject
{
    public static int Constructed;
    public int number = 5;
    public List<int> list = new List<int> { 1, 2 };
    [NonSerialized] public int transient = 7;

    public ArgOnlyData(int value)
    {
        Constructed++;
        transient = value;
    }

    void Awake() => ProbeLog.Event("ArgOnlyData.Awake " + name);
    void OnEnable() => ProbeLog.Event("ArgOnlyData.OnEnable " + name);
}
