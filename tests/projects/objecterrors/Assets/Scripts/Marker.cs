using UnityEngine;

public interface IMarker { }

/// <summary>A plain behaviour type for fields, lookups and destroyed-object controls.</summary>
public class Marker : MonoBehaviour, IMarker
{
    public int value;
}
