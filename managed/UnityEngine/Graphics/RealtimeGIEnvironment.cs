using System;
using Unbound.Runtime;
using UnityEngine.Rendering;

namespace UnityEngine
{
    /// <summary>
    /// The environment precomputed realtime GI reads (docs/REALTIME_GI.md, Environment): a cube of
    /// six faces (+X, -X, +Y, -Y, +Z, -Z) of 8 x 8 linear RGB texels, each face row by row from
    /// its top as a cube map stores it. It is the cube <see cref="DynamicGI.SetEnvironmentData"/>
    /// sets. Left to itself it follows the scene's ambient source: the ambient color everywhere; a
    /// gradient's sky color on +Y, its ground color on -Y and its equator color on the other
    /// faces; or the skybox drawn into the faces, times the ambient intensity (a scene whose
    /// environment lighting is baked takes none of it, only a script's cube). It is made again
    /// when what it was made from changes (<see cref="EnvironmentKey"/>) and when
    /// <see cref="DynamicGI.UpdateEnvironment"/> asks. Matches the Unity 6000.0.71f1 Editor,
    /// tests/projects/realtimegi (room-env*, room-gradient*, room-sky*, room-envdata*).
    /// </summary>
    internal static partial class RealtimeGI
    {
        const int FaceSize = 8;
        const int FaceTexels = FaceSize * FaceSize;
        /// <summary>The length of <see cref="DynamicGI.SetEnvironmentData"/>'s array: RGBA per texel.</summary>
        const int EnvironmentDataLength = 6 * FaceTexels * 4;

        static readonly float[] s_Faces = new float[6 * FaceTexels * 3];
        /// <summary>The faces of a scene that takes no light from the ambient source.</summary>
        static readonly float[] s_NoEnvironment = new float[6 * FaceTexels * 3];
        /// <summary>The cube a script gave (<see cref="DynamicGI.SetEnvironmentData"/>), as a
        /// scene whose environment lighting is baked keeps it: until
        /// <see cref="DynamicGI.UpdateEnvironment"/>, whatever the ambient source does (its
        /// color, another source, a skybox's intensity: tests/projects/realtimegi
        /// roombakedenvironment-envdata-*), or a scene loaded alone (-envdata-loaded).</summary>
        static readonly float[] s_ScriptFaces = new float[6 * FaceTexels * 3];
        static bool s_ScriptFacesSet;
        /// <summary>Counts the changes of <see cref="s_Faces"/>; each native scene takes them once.</summary>
        static int s_EnvironmentVersion;

        internal static ReadOnlySpan<float> EnvironmentFaces => s_Faces;

        /// <summary>The faces a scene's realtime GI takes: with its environment lighting baked
        /// (<paramref name="bakedEnvironment"/>), nothing of what the ambient source makes, only
        /// a script's cube until DynamicGI.UpdateEnvironment (matches the Unity 6000.0.71f1
        /// Editor, tests/projects/realtimegi roombakedenvironment-*).</summary>
        internal static float[] EnvironmentOf(bool bakedEnvironment) =>
            !bakedEnvironment ? s_Faces : s_ScriptFacesSet ? s_ScriptFaces : s_NoEnvironment;
        internal static int EnvironmentVersion => s_EnvironmentVersion;
        static EnvironmentKey s_EnvironmentKey;
        /// <summary>The faces are made again whatever the key (none made yet, or
        /// <see cref="DynamicGI.UpdateEnvironment"/>).</summary>
        static bool s_EnvironmentStale = true;
        static Texture2D s_SkyReadback;

        /// <summary>What the ambient source makes the environment from. The intensity and the
        /// skybox count only with a skybox as the source; a skybox ambient mode without a skybox
        /// material reads the ambient color, as the color mode does.</summary>
        struct EnvironmentKey : IEquatable<EnvironmentKey>
        {
            internal AmbientMode Mode;
            internal Material Sky;
            internal int SkyVersion;
            internal float Intensity;
            /// <summary>The direction to the sky's sun in thousandths, so that a slowly turning
            /// sun does not draw the sky again in every frame.</summary>
            internal Vector3Int Sun;
            /// <summary>The sun's color where the sky's capture draws with it (releases without
            /// Compat.SkyAmbientWithoutLightColor), else black.</summary>
            internal Color SunColor;
            internal Color Color, Equator, Ground;

            public bool Equals(EnvironmentKey other) =>
                Mode == other.Mode && ReferenceEquals(Sky, other.Sky) && SkyVersion == other.SkyVersion
                && Intensity.Equals(other.Intensity) && Sun == other.Sun && SunColor.Equals(other.SunColor)
                && Color.Equals(other.Color) && Equator.Equals(other.Equator) && Ground.Equals(other.Ground);

            public override bool Equals(object obj) => obj is EnvironmentKey other && Equals(other);
            public override int GetHashCode() => HashCode.Combine((int)Mode, SkyVersion, Intensity, Sun, Color, Equator, Ground);

            internal static EnvironmentKey Current()
            {
                var key = new EnvironmentKey { Mode = RenderSettings.ambientMode };
                var sky = RenderSettings.skybox;
                if (key.Mode == AmbientMode.Skybox && sky != null)
                {
                    key.Sky = sky;
                    key.SkyVersion = sky.m_Version;
                    key.Intensity = RenderSettings.ambientIntensity;
                    var sun = LightingGlobals.MainLightOf();
                    var toSun = sun != null ? -sun.transform.forward : LightingGlobals.SkyWithoutSun;
                    key.Sun = new Vector3Int(Thousandths(toSun.x), Thousandths(toSun.y), Thousandths(toSun.z));
                    if (sun != null && !Compat.SkyAmbientWithoutLightColor) key.SunColor = sun.color * sun.intensity;
                    return key;
                }
                key.Color = RenderSettings.ambientSkyColor;
                if (key.Mode == AmbientMode.Trilight)
                {
                    key.Equator = RenderSettings.ambientEquatorColor;
                    key.Ground = RenderSettings.ambientGroundColor;
                }
                return key;
            }

            static int Thousandths(float v) => (int)(v * 1000f + 0.5f);
        }

        /// <summary>DynamicGI.UpdateEnvironment, or a scene loaded alone: the environment is made
        /// again from the ambient source in the next update, also over data a script set and
        /// with the source's inputs as they were (tests/projects/realtimegi room-envdata-update,
        /// room-envdata-loaded).</summary>
        internal static void EnvironmentChanged()
        {
            s_EnvironmentStale = true;
            DropScriptFaces();
        }

        /// <summary>The cube a script gave no longer stands for the scenes that keep it.</summary>
        static void DropScriptFaces()
        {
            if (!s_ScriptFacesSet) return;
            s_ScriptFacesSet = false;
            s_EnvironmentVersion++;
        }

        /// <summary>DynamicGI.SetEnvironmentData: the cube's texels as given (their alpha is not
        /// read), until DynamicGI.UpdateEnvironment, a scene loaded alone or, for scenes that
        /// take the ambient source, a change of its inputs.</summary>
        internal static void SetEnvironmentData(float[] input)
        {
            if (input == null) throw new ArgumentNullException(nameof(input));
            if (input.Length != EnvironmentDataLength)
                throw new ArgumentException($"Array size is wrong. It must have {EnvironmentDataLength} elements. The resolution of each face is {FaceSize}x{FaceSize} texels.");
            for (int i = 0; i < 6 * FaceTexels; i++)
            {
                s_Faces[3 * i] = input[4 * i];
                s_Faces[3 * i + 1] = input[4 * i + 1];
                s_Faces[3 * i + 2] = input[4 * i + 2];
            }
            Array.Copy(s_Faces, s_ScriptFaces, s_Faces.Length);
            s_ScriptFacesSet = true;
            s_EnvironmentVersion++;
            s_EnvironmentKey = EnvironmentKey.Current();
            s_EnvironmentStale = false;
        }

        /// <summary>Makes the faces again when the ambient source's inputs changed, unless no
        /// loaded scene takes them (every one has its environment lighting baked): they are
        /// made once one does.</summary>
        internal static void UpdateEnvironment()
        {
            if (!TakesAmbientSource()) return;
            var key = EnvironmentKey.Current();
            if (!s_EnvironmentStale && key.Equals(s_EnvironmentKey)) return;
            s_EnvironmentStale = false;
            s_EnvironmentKey = key;
            if (key.Sky != null) RenderSkyFaces(key.Intensity);
            else
            {
                var color = key.Color.linear;
                bool gradient = key.Mode == AmbientMode.Trilight;
                for (int face = 0; face < 6; face++)
                    FillFace(face, !gradient || face == 2 ? color : face == 3 ? key.Ground.linear : key.Equator.linear);
            }
            s_EnvironmentVersion++;
        }

        static void FillFace(int face, Color c)
        {
            int at = face * FaceTexels * 3;
            for (int i = 0; i < FaceTexels; i++)
            {
                s_Faces[at + 3 * i] = c.r;
                s_Faces[at + 3 * i + 1] = c.g;
                s_Faces[at + 3 * i + 2] = c.b;
            }
        }

        /// <summary>The cube faces' axes: where a face's columns and rows run, and where it looks
        /// from (a cube map's faces).</summary>
        static readonly (Vector3 Across, Vector3 Down, Vector3 Back)[] s_FaceAxes =
        {
            (new Vector3(0, 0, -1), new Vector3(0, -1, 0), new Vector3(-1, 0, 0)),
            (new Vector3(0, 0, 1), new Vector3(0, -1, 0), new Vector3(1, 0, 0)),
            (new Vector3(1, 0, 0), new Vector3(0, 0, 1), new Vector3(0, -1, 0)),
            (new Vector3(1, 0, 0), new Vector3(0, 0, -1), new Vector3(0, 1, 0)),
            (new Vector3(1, 0, 0), new Vector3(0, -1, 0), new Vector3(0, 0, -1)),
            (new Vector3(-1, 0, 0), new Vector3(0, -1, 0), new Vector3(0, 0, 1)),
        };

        /// <summary>Draws the skybox into the faces as the ambient probe's capture draws it
        /// (<see cref="DynamicGI.RenderSky"/>: without the sun's color), one texel per pixel. The
        /// ambient intensity scales the gamma-encoded colors. In a gamma-space project the sky
        /// draws gamma-encoded colors, which are taken as such (tests/projects/realtimegi
        /// `--color-space gamma`, `room-sky`).</summary>
        static void RenderSkyFaces(float intensity)
        {
            bool gamma = DynamicGI.SkyIsGammaEncoded;
            if (s_SkyReadback == null)
                s_SkyReadback = new Texture2D(FaceSize, FaceSize, TextureFormat.RGBAFloat, false, true) { hideFlags = HideFlags.HideAndDontSave };
            for (int face = 0; face < 6; face++)
            {
                var axes = s_FaceAxes[face];
                DynamicGI.RenderSky(FaceSize, -axes.Back, -axes.Down, s_SkyReadback);
                var pixels = s_SkyReadback.GetPixelData<Color>(0);
                int at = face * FaceTexels * 3;
                for (int y = 0; y < FaceSize; y++)
                    for (int x = 0; x < FaceSize; x++)
                    {
                        // The image reads back from its bottom row, the face from its top.
                        var c = pixels[(FaceSize - 1 - y) * FaceSize + x];
                        int i = at + 3 * (y * FaceSize + x);
                        s_Faces[i] = Scaled(c.r, intensity, gamma);
                        s_Faces[i + 1] = Scaled(c.g, intensity, gamma);
                        s_Faces[i + 2] = Scaled(c.b, intensity, gamma);
                    }
            }
        }

        /// <summary>A sky texel (linear, or gamma-encoded with <paramref name="gamma"/>) times the
        /// ambient intensity, linear; a texel that is not above zero (also not a number) is
        /// black.</summary>
        static float Scaled(float texel, float intensity, bool gamma) =>
            texel > 0f ? Mathf.GammaToLinearSpace((gamma ? texel : Mathf.LinearToGammaSpace(texel)) * intensity) : 0f;
    }
}
