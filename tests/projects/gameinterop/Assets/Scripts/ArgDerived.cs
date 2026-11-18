using System;

public class ArgDerived : ArgBase
{
    public static new int Constructed;
    public int derivedNumber = 6;
    [NonSerialized] public int derivedTransient = 4;

    public ArgDerived(string label) { Constructed++; }
}
