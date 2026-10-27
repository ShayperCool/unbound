using System.Collections.Generic;
using UnityEngine;

/// <summary>Fields clips animate, and the events they send.</summary>
public class AnimTarget : MonoBehaviour
{
    public float amount;
    public int count;
    public bool flag;
    public Vector3 offset;
    public Color tint = Color.white;

    public static readonly List<string> Events = new List<string>();

    void OnEvent(float value) => Events.Add($"{name}:OnEvent({value.ToString("R", System.Globalization.CultureInfo.InvariantCulture)})@{Time.frameCount}");
    void OnText(string value) => Events.Add($"{name}:OnText({value})@{Time.frameCount}");
}
