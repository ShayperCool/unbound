using System;
using UnityEngine;
public sealed class LoadingMarker : MonoBehaviour
{
    [NonSerialized] public string Field = Read();
    [NonSerialized] public string Constructor;
    public LoadingMarker() { Constructor = Read(); }
    public static string Read()
    {
        try { var value = Time.time; return "ok"; }
        catch (Exception e) { return "throws " + e.GetType().FullName + ": " + e.Message; }
    }
}
