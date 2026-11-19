using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>[SerializeReference] fields: a polymorphic graph with a shared node, a null and a
/// namespaced class.</summary>
public class Graph : MonoBehaviour
{
    [SerializeReference] public Shape main;
    [SerializeReference] public List<Shape> shapes = new List<Shape>();
    [SerializeReference] public Shape empty;
    public int after = 1;
}

[Serializable]
public class Shape
{
    public string name;
}

[Serializable]
public class Circle : Shape
{
    public float radius;
}

namespace Probe.Shapes
{
    [Serializable]
    public class Box : Shape
    {
        public Vector2 size;
        [SerializeReference] public Shape inner;
    }
}
