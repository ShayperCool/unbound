using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>[SerializeReference] fields holding objects of generic classes: one and two type
/// arguments, a nested generic argument, a project class and a list as arguments.</summary>
public class GenericRefs : ScriptableObject
{
    [SerializeReference] public object one;
    [SerializeReference] public object two;
    [SerializeReference] public List<object> many = new List<object>();
}

[Serializable]
public class Box<T>
{
    public T value;
}

[Serializable]
public class Pair<TFirst, TSecond>
{
    public TFirst first;
    public TSecond second;
}

namespace Probe.Generic
{
    [Serializable]
    public class Tagged<T>
    {
        public string tag;
        public T item;
    }
}
