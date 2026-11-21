using System;
using Unity.Properties;
using UnityEngine;

public class QuatCharTarget : ScriptableObject
{
    public char charVal = 'A';
    public char multiSameChar = 'M';
    public char multiDiffChar = 'A';
    public string strVal = "hello";
    public Quaternion quatVal = new Quaternion(0.1f, 0.2f, 0.3f, 0.9f);
    public Quaternion multiQuat = new Quaternion(0.1f, 0.2f, 0.3f, 0.9f);
    public Vector4 vec4Val = new Vector4(1f, 2f, 3f, 4f);
}

public class QuatCharRuntimeModel
{
    [CreateProperty]
    public string tip { get; set; } = "rt-qc-0";
}
