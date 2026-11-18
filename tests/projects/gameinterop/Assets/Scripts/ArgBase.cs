using System;

/// <summary>A base behaviour with a parameterless constructor under a class that has none.</summary>
public class ArgBase : UnityEngine.MonoBehaviour
{
    public static int Constructed;
    public int baseNumber = 2;
    [NonSerialized] public int baseTransient = 3;

    public ArgBase() { Constructed++; }
}
