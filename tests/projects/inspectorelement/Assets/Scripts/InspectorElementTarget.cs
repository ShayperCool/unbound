using System;
using System.Collections.Generic;
using UnityEngine;

// Inspected by a generic (default) inspector.
public class InspectorDefaultTarget : ScriptableObject
{
    public int count = 3;
    public string title = "t";
    public Vector2 offset = new Vector2(1f, 2f);
    [HideInInspector] public int secret = 4;
    public List<int> numbers = new List<int> { 5, 6 };
    [Serializable] public class Nested { public float f = 0.5f; }
    public Nested nested = new Nested();
}

// Each has a [CustomEditor] in Assets/Editor.
public class InspectorUieTarget : ScriptableObject { public int a = 1; }
public class InspectorImguiTarget : ScriptableObject { public int b = 2; }
public class InspectorNullGuiTarget : ScriptableObject { public int c = 3; }
public class InspectorNoMarginsTarget : ScriptableObject { public int d = 4; }

public class InspectorBehaviour : MonoBehaviour
{
    public int speed = 7;
    public Color tint = Color.red;
}
