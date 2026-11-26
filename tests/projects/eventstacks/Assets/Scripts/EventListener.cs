using System;
using UnityEngine;

/// <summary>The listeners: methods that throw, directly or in a method they call, and one that
/// counts its calls.</summary>
public class EventListener : MonoBehaviour
{
    public static int Ran;
    public int count;

    public void Throw() => throw new InvalidOperationException("From a listener.");

    public void ThrowInt(int value) => throw new ArgumentOutOfRangeException("value", value, "From a listener.");

    public void ThrowDeep() => Helper(2);

    static void Helper(int depth)
    {
        if (depth == 0) throw new NullReferenceException("Deep.");
        Helper(depth - 1);
    }

    public void Count()
    {
        count++;
        Ran++;
    }
}
