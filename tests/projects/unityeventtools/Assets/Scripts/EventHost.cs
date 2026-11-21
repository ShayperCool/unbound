using System;
using UnityEngine;
using UnityEngine.Events;

/// <summary>Holds one event of each listener shape.</summary>
public class EventHost : MonoBehaviour
{
    [Serializable] public class IntEvent : UnityEvent<int> { }
    [Serializable] public class PairEvent : UnityEvent<int, string> { }

    public UnityEvent plain = new UnityEvent();
    public IntEvent number = new IntEvent();
    public PairEvent pair = new PairEvent();
}
