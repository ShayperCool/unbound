using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

/// <summary>Receives the imported clips' animation events.</summary>
public class ClipReceiver : MonoBehaviour
{
    public readonly List<string> Received = new List<string>();
    static string F(float f) => f.ToString("R", CultureInfo.InvariantCulture);

    public void Hit(float value) => Received.Add("[\"Hit\"," + F(value) + "]");

    public void Ping(AnimationEvent e) =>
        Received.Add("[\"Ping\"," + F(e.time) + "," + F(e.floatParameter) + "," + e.intParameter + ",\"" + e.stringParameter + "\"]");
}
