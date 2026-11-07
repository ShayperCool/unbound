using System;
using UnityEngine;
using Object = UnityEngine.Object;
[Serializable] public class CloneData { public int value; public Object reference; }
public class CloneAsset : ScriptableObject
{
    public CloneData data;
    public int[] values;
    public Object self;
    public AnimationCurve curve;
}

