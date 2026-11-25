using Unbound.SpeedTree;

namespace UnityEngine
{
    /// <summary>
    /// The wind of a SpeedTree 7 or 8 tree as its wind asset keeps it (the asset's <c>m_Config8</c>):
    /// the file's wind with its heights and distances in metres. Matches the wind assets the Unity
    /// 6000.0.71f1 Editor imports from SpeedTree 8 files (SpeedTreeWindConfig8Tests): the global
    /// height, the global, branch, leaf ripple and frond ripple distance curves and the first branch
    /// level's maximum length are scaled by the import scale; the branch wind anchor is kept as the
    /// file has it. The leaf roll settings and the rolling noise are not in a SpeedTree 8 file: the
    /// asset has zero roll and the rolling defaults.
    /// </summary>
    internal sealed class SpeedTreeWindConfig8
    {
        internal float StrengthResponse, DirectionResponse, AnchorOffset, AnchorDistanceScale,
            GustFrequency, GustStrengthMin, GustStrengthMax, GustDurationMin, GustDurationMax, GustRiseScalar, GustFallScalar;
        internal float[][] Oscillation;
        internal float GlobalHeight, GlobalHeightExponent;
        internal float[] GlobalDistance, GlobalDirectionAdherence;
        internal Branch[] BranchLevels;
        internal Leaf[] LeafGroups;
        internal float[] FrondRippleDistance;
        internal float FrondRippleTile, FrondRippleLightingScalar;
        // The rolling noise of the wind asset of a tree whose file has none.
        internal float RollingNoiseSize = 0.005f, RollingNoiseTwist = 9f, RollingNoiseTurbulence = 32f, RollingNoisePeriod = 0.4f,
            RollingNoiseSpeed = 0.05f, RollingBranchFieldMin = 0.5f, RollingBranchLightingAdjust = 0.5f,
            RollingBranchVerticalOffset = -0.5f, RollingLeafRippleMin = 0.5f, RollingLeafTumbleMin = 0.5f;
        internal Vector3 BranchWindAnchor;
        internal float MaxBranchLevel1Length;
        /// <summary>The options, in <see cref="SpeedTree8File.WindOption"/> order.</summary>
        internal bool[] Options;

        internal sealed class Branch
        {
            internal float[] Distance, DirectionAdherence, Whip;
            internal float Turbulence, Twitch, TwitchFreqScale;
        }

        internal sealed class Leaf
        {
            internal float[] RippleDistance, TumbleFlip, TumbleTwist, TumbleDirectionAdherence, TwitchThrow;
            internal float TwitchSharpness, RollMaxScale, RollMinScale, RollSpeed, RollSeparation, LeewardScalar;
        }

        /// <summary>The wind asset's configuration of a SpeedTree 8 file's wind at the import's
        /// <paramref name="scale"/>.</summary>
        internal static SpeedTreeWindConfig8 From(SpeedTree8File.Wind wind, float scale)
        {
            var config = new SpeedTreeWindConfig8
            {
                StrengthResponse = wind.StrengthResponse,
                DirectionResponse = wind.DirectionResponse,
                AnchorOffset = wind.AnchorOffset,
                AnchorDistanceScale = wind.AnchorDistanceScale,
                GustFrequency = wind.GustFrequency,
                GustStrengthMin = wind.GustStrengthMin,
                GustStrengthMax = wind.GustStrengthMax,
                GustDurationMin = wind.GustDurationMin,
                GustDurationMax = wind.GustDurationMax,
                GustRiseScalar = wind.GustRiseScalar,
                GustFallScalar = wind.GustFallScalar,
                Oscillation = new float[wind.Oscillation.Length][],
                GlobalHeight = wind.GlobalHeight * scale,
                GlobalHeightExponent = wind.GlobalHeightExponent,
                GlobalDistance = Scaled(wind.GlobalDistance, scale),
                GlobalDirectionAdherence = (float[])wind.GlobalDirectionAdherence.Clone(),
                BranchLevels = new Branch[wind.BranchLevels.Length],
                LeafGroups = new Leaf[wind.LeafGroups.Length],
                FrondRippleDistance = Scaled(wind.FrondRippleDistance, scale),
                FrondRippleTile = wind.FrondRippleTile,
                FrondRippleLightingScalar = wind.FrondRippleLightingScalar,
                BranchWindAnchor = new Vector3(wind.BranchWindAnchor.X, wind.BranchWindAnchor.Y, wind.BranchWindAnchor.Z),
                MaxBranchLevel1Length = wind.MaxBranchLevel1Length * scale,
                Options = (bool[])wind.Options.Clone(),
            };
            for (int i = 0; i < config.Oscillation.Length; i++) config.Oscillation[i] = (float[])wind.Oscillation[i].Clone();
            for (int i = 0; i < config.BranchLevels.Length; i++)
            {
                var b = wind.BranchLevels[i];
                config.BranchLevels[i] = new Branch
                {
                    Distance = Scaled(b.Distance, scale),
                    DirectionAdherence = (float[])b.DirectionAdherence.Clone(),
                    Whip = (float[])b.Whip.Clone(),
                    Turbulence = b.Turbulence,
                    Twitch = b.Twitch,
                    TwitchFreqScale = b.TwitchFreqScale,
                };
            }
            for (int i = 0; i < config.LeafGroups.Length; i++)
            {
                var l = wind.LeafGroups[i];
                config.LeafGroups[i] = new Leaf
                {
                    RippleDistance = Scaled(l.RippleDistance, scale),
                    TumbleFlip = (float[])l.TumbleFlip.Clone(),
                    TumbleTwist = (float[])l.TumbleTwist.Clone(),
                    TumbleDirectionAdherence = (float[])l.TumbleDirectionAdherence.Clone(),
                    TwitchThrow = (float[])l.TwitchThrow.Clone(),
                    TwitchSharpness = l.TwitchSharpness,
                    LeewardScalar = l.LeewardScalar,
                };
            }
            return config;
        }

        static float[] Scaled(float[] curve, float scale)
        {
            var scaled = new float[curve.Length];
            for (int i = 0; i < scaled.Length; i++) scaled[i] = curve[i] * scale;
            return scaled;
        }
    }
}
