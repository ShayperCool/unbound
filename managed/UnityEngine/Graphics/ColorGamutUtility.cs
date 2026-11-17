namespace UnityEngine
{
    /// <summary>The primaries, white point and transfer function of each color gamut (measured:
    /// tests/projects/runtimeapis gamut.*).</summary>
    public partial class ColorGamutUtility
    {
        public ColorGamutUtility() { }

        public static ColorPrimaries GetColorPrimaries(ColorGamut gamut) => gamut switch
        {
            ColorGamut.sRGB or ColorGamut.Rec709 => ColorPrimaries.Rec709,
            ColorGamut.Rec2020 or ColorGamut.HDR10 or ColorGamut.DolbyHDR => ColorPrimaries.Rec2020,
            ColorGamut.DisplayP3 or ColorGamut.P3D65G22 => ColorPrimaries.P3,
            _ => ColorPrimaries.Unknown,
        };

        public static WhitePoint GetWhitePoint(ColorGamut gamut) =>
            gamut >= ColorGamut.sRGB && gamut <= ColorGamut.P3D65G22 ? WhitePoint.D65 : WhitePoint.Unknown;

        public static TransferFunction GetTransferFunction(ColorGamut gamut) => gamut switch
        {
            ColorGamut.sRGB or ColorGamut.DisplayP3 => TransferFunction.sRGB,
            ColorGamut.Rec709 or ColorGamut.Rec2020 => TransferFunction.Linear,
            ColorGamut.HDR10 or ColorGamut.DolbyHDR => TransferFunction.PQ,
            ColorGamut.P3D65G22 => TransferFunction.Gamma22,
            _ => TransferFunction.Unknown,
        };
    }
}
