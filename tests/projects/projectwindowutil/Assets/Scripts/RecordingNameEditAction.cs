using System.Collections.Generic;
using UnityEditor.ProjectWindowCallback;

/// <summary>An end action that writes down what it is called with, and nothing else.</summary>
public class RecordingNameEditAction : EndNameEditAction
{
    public static readonly List<string> Calls = new List<string>();

    public override void Action(int instanceId, string pathName, string resourceFile) =>
        Calls.Add("Action(" + instanceId + "," + pathName + "," + (resourceFile ?? "<null>") + ")");

    public override void Cancelled(int instanceId, string pathName, string resourceFile) =>
        Calls.Add("Cancelled(" + instanceId + "," + pathName + "," + (resourceFile ?? "<null>") + ")");
}
