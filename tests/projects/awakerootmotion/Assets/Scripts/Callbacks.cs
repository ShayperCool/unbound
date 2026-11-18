using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>The callbacks the probe's scripts received, in order: "object.Class.Callback".</summary>
public static class Calls
{
    public static readonly List<string> Log = new List<string>();

    public static void Add(MonoBehaviour b, string callback) => Log.Add(b.gameObject.name + "." + b.GetType().Name + "." + callback);
}

/// <summary>Logs its callbacks; <see cref="Throws"/> names the one that throws.</summary>
public abstract class Logged : MonoBehaviour
{
    protected virtual string Throws => null;

    void Call(string callback)
    {
        Calls.Add(this, callback);
        if (callback == Throws) throw new InvalidOperationException(GetType().Name + " " + callback + " fails");
    }

    void Awake() => Call("Awake");
    void OnEnable() => Call("OnEnable");
    void Start() => Call("Start");
    void Update() { if (!Calls.Log.Contains(gameObject.name + "." + GetType().Name + ".Update")) Call("Update"); }
    void OnDisable() => Call("OnDisable");
    void OnDestroy() => Call("OnDestroy");
}

public class Neighbour : Logged { }
public class ThrowAwake : Logged { protected override string Throws => "Awake"; }
public class ThrowEnable : Logged { protected override string Throws => "OnEnable"; }
public class ThrowStart : Logged { protected override string Throws => "Start"; }
