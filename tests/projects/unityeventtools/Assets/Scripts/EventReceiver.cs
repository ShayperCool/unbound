using System.Collections.Generic;
using UnityEngine;

/// <summary>The methods persistent listeners call; each call is written down.</summary>
public class EventReceiver : MonoBehaviour
{
    public static readonly List<string> Calls = new List<string>();

    public void Ping() => Calls.Add(name + ".Ping");
    public void TakeInt(int value) => Calls.Add(name + ".TakeInt(" + value + ")");
    public void TakeFloat(float value) => Calls.Add(name + ".TakeFloat(" + value.ToString(System.Globalization.CultureInfo.InvariantCulture) + ")");
    public void TakeBool(bool value) => Calls.Add(name + ".TakeBool(" + value + ")");
    public void TakeString(string value) => Calls.Add(name + ".TakeString(" + (value ?? "<null>") + ")");
    public void TakeObject(Object value) => Calls.Add(name + ".TakeObject(" + (value != null ? value.name : "<null>") + ")");
    public void TakeTransform(Transform value) => Calls.Add(name + ".TakeTransform(" + (value != null ? value.name : "<null>") + ")");
    public void Pair(int a, string b) => Calls.Add(name + ".Pair(" + a + "," + b + ")");
    public static void StaticPing() => Calls.Add("static.StaticPing");
}
