using System;
using UnityEngine;
public sealed class LoadingAsset : ScriptableObject
{
    [NonSerialized] public string Field = LoadingMarker.Read();
    [NonSerialized] public string Constructor;
    public LoadingAsset() { Constructor = LoadingMarker.Read(); }
}
