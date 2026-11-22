using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable] public class UndoNested { public float f = 0.5f; public string s = "n"; }

public class UndoTarget : ScriptableObject
{
    public int count = 3;
    public string title = "t";
    public List<int> numbers = new List<int> { 5, 6 };
    public UndoNested nested = new UndoNested();
    public int maxSpeed = 1;
    [SerializeField] int m_Level = 2;
    public int level => m_Level;
}
