using System;

/// <summary>Control: a private parameterless constructor is the one the engine runs.</summary>
public class PrivateCtorBehaviour : UnityEngine.MonoBehaviour
{
    public static int Constructed;
    public int number = 5;
    [NonSerialized] public int transient = 8;

    PrivateCtorBehaviour() { Constructed++; }
}
