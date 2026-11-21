using System.Collections.Generic;
using UnityEngine;

public enum NonAscendingChoice { Bravo = 5, Alpha = 1, Charlie = 3 }
public enum AliasChoice { Zeta = 5, Alpha = 5, Mid = 3 }
public enum HighUnsignedChoice : uint { Low = 1, High = 0x80000000u, Mid = 7 }
public enum SignedChoice { Pos = 2, Neg = -1, Zero = 0 }
public enum ShapeNameChoice { Matrix4x4, Grid2x3, Size4x, Uv2, Rgba32Bit }

public class BindingControlsTarget : ScriptableObject
{
    public NonAscendingChoice nonAscending = NonAscendingChoice.Charlie;
    public AliasChoice alias = AliasChoice.Zeta;
    public HighUnsignedChoice highUnsigned = HighUnsignedChoice.High;
    public SignedChoice signed = SignedChoice.Neg;
    public ShapeNameChoice shapeName = ShapeNameChoice.Grid2x3;
    public Vector4 pos4 = new Vector4(1f, 10f, 100f, 100f);
    [ColorUsage(false, true)] public Color tint = Color.red;
    [ColorUsage(false, true)] public Color[] tints = { Color.red, Color.blue };
    [GradientUsage(true, ColorSpace.Linear)] public List<Gradient> ramps = new List<Gradient> { new Gradient() };
    public Color plainColor = Color.red;
}
