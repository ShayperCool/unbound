using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

// Fields of the built-in structs at the root of a script object, in a nested class, and in
// collections.
public class BuiltinHolder : ScriptableObject
{
    public RenderingLayerMask mask;
    public SphericalHarmonicsL2 sh;
    public BuiltinNested nested = new BuiltinNested();
    public RenderingLayerMask[] masks = new RenderingLayerMask[0];
    public List<SphericalHarmonicsL2> shs = new List<SphericalHarmonicsL2>();
    public int after;
}

[Serializable]
public class BuiltinNested
{
    public RenderingLayerMask mask;
    public SphericalHarmonicsL2 sh;
    public int after;
}

[Serializable]
public class BuiltinWrapper
{
    public RenderingLayerMask mask;
    public SphericalHarmonicsL2 sh;
    public BuiltinNested nested = new BuiltinNested();
    public List<RenderingLayerMask> masks = new List<RenderingLayerMask>();
    public SphericalHarmonicsL2[] shs = new SphericalHarmonicsL2[0];
}
