using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public struct ArrayItemStruct
{
    public string title;
    public int value;
}

[Serializable]
public struct PlainStructForListTest
{
    public int a;
}

public class ArrayListTarget : ScriptableObject
{
    public int[] intArray = new[] { 10, 20, 30 };
    public List<string> stringList = new List<string> { "one", "two" };
    public ArrayItemStruct[] structArray = new[]
    {
        new ArrayItemStruct { title = "First", value = 1 },
        new ArrayItemStruct { title = "Second", value = 2 }
    };
    public int[] multiSame = new[] { 5, 6 };
    public int[] multiDiff = new[] { 100, 200, 300 };
    public PlainStructForListTest plainStruct = new PlainStructForListTest { a = 7 };
    public int scalarInt = 42;
}
