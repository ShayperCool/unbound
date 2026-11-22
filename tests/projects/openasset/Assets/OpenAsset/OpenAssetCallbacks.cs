using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>
/// The probe's [OnOpenAsset] methods: each writes down its call and answers what the probe set
/// for it. Orders -5, -3, 0, the default, 2, 3 and 100 with one, two and three parameters, two
/// validators, and methods of shapes the attribute does not name.
/// </summary>
public static class OpenAssetCallbacks
{
    public static readonly List<string> Calls = new List<string>();
    /// <summary>The names of the methods that answer true.</summary>
    public static readonly HashSet<string> Handle = new HashSet<string>();
    /// <summary>The name of the method that throws.</summary>
    public static string Throw;
    public static readonly Dictionary<int, string> Names = new Dictionary<int, string>();

    public static void Reset()
    {
        Calls.Clear();
        Handle.Clear();
        Throw = null;
    }

    /// <summary>An instance ID as the asset path (and the object's name for a sub-asset), the
    /// name the probe gave the ID, or a scene object's name.</summary>
    public static string Describe(int id)
    {
        if (Names.TryGetValue(id, out var known)) return known;
        var o = EditorUtility.InstanceIDToObject(id);
        if (o == null) return "<no object " + (id == 0 ? "0" : "?") + ">";
        var path = AssetDatabase.GetAssetPath(o);
        if (string.IsNullOrEmpty(path)) return "<" + o.name + ">";
        return AssetDatabase.IsMainAsset(o) ? path : path + "#" + o.GetType().Name + ":" + o.name;
    }

    static bool Call(string name, int id, string rest)
    {
        Calls.Add(name + "(" + Describe(id) + rest + ")");
        if (Throw == name) throw new InvalidOperationException("probe " + name);
        return Handle.Contains(name);
    }

    [OnOpenAsset(-5)]
    static bool Negative(int id) => Call("Negative", id, "");

    [OnOpenAsset(-3)]
    static bool MinusThree(int id, int line, int column) => Call("MinusThree", id, "," + line + "," + column);

    [OnOpenAsset(0)]
    public static bool Zero(int id, int line) => Call("Zero", id, "," + line);

    [OnOpenAsset]
    static bool Default(int id, int line, int column) => Call("Default", id, "," + line + "," + column);

    [OnOpenAsset(2)]
    static bool Two(int id, int line) => Call("Two", id, "," + line);

    [OnOpenAsset(3)]
    static bool Three(int id) => Call("Three", id, "");

    [OnOpenAsset(100)]
    public static bool Hundred(int id, int line, int column) => Call("Hundred", id, "," + line + "," + column);

    [OnOpenAsset(OnOpenAssetAttributeMode.Validate)]
    static bool Validate(int id) => Call("Validate", id, "");

    [OnOpenAsset(-7, OnOpenAssetAttributeMode.Validate)]
    static bool ValidateEarly(int id) => Call("ValidateEarly", id, "");

    // Shapes the attribute does not name.
    [OnOpenAsset(-9)]
    static void WrongReturn(int id) => Call("WrongReturn", id, "");

    [OnOpenAsset(-9)]
    static bool WrongParameter(string id) { Calls.Add("WrongParameter(" + id + ")"); return Handle.Contains("WrongParameter"); }

    [OnOpenAsset(-9)]
    static bool NoParameters() { Calls.Add("NoParameters()"); return Handle.Contains("NoParameters"); }

    [OnOpenAsset(-9)]
    static bool FourParameters(int id, int line, int column, int more) => Call("FourParameters", id, "," + line + "," + column + "," + more);

    [OnOpenAsset(-9, OnOpenAssetAttributeMode.Validate)]
    static void ValidateWrongReturn(int id) => Call("ValidateWrongReturn", id, "");

    [OnOpenAsset(-9, OnOpenAssetAttributeMode.Validate)]
    static bool ValidateTwoParameters(int id, int line) => Call("ValidateTwoParameters", id, "," + line);
}

public class OpenAssetInstanceCallbacks
{
    [OnOpenAsset(-9)]
    bool Instance(int id)
    {
        OpenAssetCallbacks.Calls.Add("Instance(" + OpenAssetCallbacks.Describe(id) + ")");
        return OpenAssetCallbacks.Handle.Contains("Instance");
    }
}
