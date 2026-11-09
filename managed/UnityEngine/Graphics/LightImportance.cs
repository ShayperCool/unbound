using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Unbound.Runtime
{
    /// <summary>
    /// Which lights reach an object and how the forward paths (the built-in pipeline's and the
    /// scriptable pipelines' per-object lights, URP's Forward) rank them, the most important first:
    /// <list type="bullet">
    /// <item>a light's brightness is the luminance of its colour as set (0.3 r + 0.59 g + 0.11 b:
    /// not linearized, colour temperature aside) times its intensity;</item>
    /// <item>a local light's is divided by 1 + 25 d² / range², d from the centre of the object's
    /// bounds; a directional light's counts sixteen times when it casts shadows;</item>
    /// <item>an Important light adds 1000, a Not Important one subtracts 1000;</item>
    /// <item>equal ones keep the visible lights' order.</item>
    /// </list>
    /// A point light reaches the bounds its range sphere touches, a spot light the bounds that touch
    /// its square pyramid (the cone's half angle, out to the range).
    /// Matches the Unity 6000.0.71f1 Editor, tests/projects/urp light-order.jsonl (color_temperature:
    /// the colour as set) and tests/projects/builtinrp light_rank_* (a linear project).
    /// The spot pyramid's corners against a round cone are not measured.
    /// </summary>
    internal static class LightImportance
    {
        /// <summary>Lights dimmer than this light nothing at run time (they are not visible lights).</summary>
        internal const float MinimumVisibleIntensity = 0.001f;
        // Luminance weights of the light's colour.
        const float RedWeight = 0.3f, GreenWeight = 0.59f, BlueWeight = 0.11f;
        /// <summary>The falloff 1 / (1 + 25 d² / range²) the built-in light attenuation texture uses.</summary>
        const float FalloffScale = 25f;
        /// <summary>How much more a directional light casting shadows counts.</summary>
        const float ShadowedDirectionalWeight = 16f;
        /// <summary>Added for Important lights, taken off Not Important ones.</summary>
        const float RenderModeBias = 1000f;
        /// <summary>The spot pyramid's near plane: this fraction of the range, at least
        /// <see cref="MinimumSpotNear"/>.</summary>
        const float SpotNearFraction = 1e-5f, MinimumSpotNear = 1e-4f;
        /// <summary>The smallest range the falloff divides by.</summary>
        const float MinimumRange = 1e-4f;

        /// <summary>The luminance of the light's colour times its intensity.</summary>
        internal static float Brightness(Light light)
        {
            var c = light.color;
            return (c.b * BlueWeight + c.g * GreenWeight + c.r * RedWeight) * light.intensity;
        }

        /// <summary>A light's importance for an object whose bounds are centred at <paramref name="center"/>.</summary>
        internal static float Of(Light light, LightType type, Vector3 position, float range, Vector3 center)
        {
            float importance = Brightness(light);
            if (type == LightType.Directional)
            {
                if (light.shadows != LightShadows.None) importance *= ShadowedDirectionalWeight;
            }
            else
            {
                float d2 = (center - position).sqrMagnitude;
                // A range of 0 reaches only bounds holding the light: keep the falloff finite.
                range = Mathf.Max(range, MinimumRange);
                importance *= 1f / (FalloffScale / (range * range) * d2 + 1f);
            }
            return importance + light.renderMode switch
            {
                LightRenderMode.ForcePixel => RenderModeBias,
                LightRenderMode.ForceVertex => -RenderModeBias,
                _ => 0f,
            };
        }

        /// <summary>Whether a light's range reaches bounds (a directional light's always does);
        /// a spot light also needs <see cref="SpotReaches"/>.</summary>
        internal static bool Reaches(LightType type, Vector3 position, float range, in Bounds bounds) =>
            type == LightType.Directional || bounds.SqrDistance(position) <= range * range;

        /// <summary>Whether bounds touch a spot light's pyramid (from <paramref name="localToWorld"/>,
        /// looking along its z).</summary>
        internal static bool SpotReaches(in Matrix4x4 localToWorld, float range, float spotAngle, in Bounds bounds)
        {
            var position = (Vector3)localToWorld.GetColumn(3);
            var right = ((Vector3)localToWorld.GetColumn(0)).normalized;
            var up = ((Vector3)localToWorld.GetColumn(1)).normalized;
            var forward = ((Vector3)localToWorld.GetColumn(2)).normalized;
            float cot = 1f / Mathf.Tan(spotAngle * 0.5f * Mathf.Deg2Rad);
            float near = Mathf.Max(range * SpotNearFraction, MinimumSpotNear);
            float far = Mathf.Max(range, near + MinimumSpotNear);
            // The pyramid's planes, facing inwards: |x| <= z / cot, |y| <= z / cot, near <= z <= far.
            float side = 1f / Mathf.Sqrt(cot * cot + 1f);
            return Inside(bounds, (forward - cot * right) * side, position)
                && Inside(bounds, (forward + cot * right) * side, position)
                && Inside(bounds, (forward - cot * up) * side, position)
                && Inside(bounds, (forward + cot * up) * side, position)
                && Inside(bounds, forward, position + forward * near)
                && Inside(bounds, -forward, position + forward * far);
        }

        /// <summary>Whether some of the bounds lie on the inner side of the plane through
        /// <paramref name="point"/> facing <paramref name="normal"/>.</summary>
        static bool Inside(in Bounds bounds, Vector3 normal, Vector3 point)
        {
            var e = bounds.extents;
            float radius = Mathf.Abs(normal.x) * e.x + Mathf.Abs(normal.y) * e.y + Mathf.Abs(normal.z) * e.z;
            return Vector3.Dot(normal, bounds.center - point) + radius >= 0f;
        }

        /// <summary>Sorts candidates (an index and an importance, in the visible lights' order)
        /// the most important first, equal ones in their order.</summary>
        internal static void Sort(List<Candidate> candidates) => candidates.Sort(s_Compare);

        internal readonly struct Candidate
        {
            internal readonly int Index;
            internal readonly float Importance;
            /// <summary>The place among the candidates before sorting (keeps equal ones in order).</summary>
            internal readonly int Order;

            internal Candidate(int index, float importance, int order)
            {
                Index = index;
                Importance = importance;
                Order = order;
            }
        }

        static readonly Comparison<Candidate> s_Compare = (a, b) =>
        {
            int c = b.Importance.CompareTo(a.Importance);
            return c != 0 ? c : a.Order.CompareTo(b.Order);
        };
    }
}
