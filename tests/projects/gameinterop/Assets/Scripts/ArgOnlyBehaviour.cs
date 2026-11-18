using System;
using System.Collections.Generic;

/// <summary>A behaviour whose only constructor takes an argument: what the engine creates for it,
/// and which of its initializers (they live in that constructor) run.</summary>
public class ArgOnlyBehaviour : UnityEngine.MonoBehaviour
{
    public static int Constructed;
    public int number = 5;
    public string text = "initializer";
    public List<int> list = new List<int> { 1, 2 };
    public int[] array = { 3 };
    public ArgNested nested = new ArgNested();
    [NonSerialized] public int transient = 7;
    [NonSerialized] public List<int> transientList = new List<int>();

    public ArgOnlyBehaviour(int value)
    {
        Constructed++;
        transient = value;
    }

    void Awake() => ProbeLog.Event("ArgOnlyBehaviour.Awake " + name);
    void OnEnable() => ProbeLog.Event("ArgOnlyBehaviour.OnEnable " + name);
}

[Serializable]
public class ArgNested
{
    public int a = 9;
    [NonSerialized] public int b = 4;
}
