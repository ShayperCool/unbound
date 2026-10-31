using System;
using System.Collections.Generic;
using UnityEngine;

namespace Unbound.Runtime
{
    internal sealed partial class AnimatorPlayer
    {
        /// <summary>Child weights of a blend tree for the current parameters.</summary>
        internal float[] BlendWeights(BlendTreeMotion tree)
        {
            int n = tree.Children.Count;
            var w = new float[n];
            if (n == 0) return w;
            switch (tree.BlendType)
            {
                case 4: // Direct: normalization caps the total at one; low totals retain defaults.
                {
                    float total = 0f;
                    for (int i = 0; i < n; i++)
                    {
                        int p = ParameterIndex(tree.Children[i].DirectParameter);
                        w[i] = p >= 0 ? Math.Max(0f, Floats[p]) : 0f;
                        total += w[i];
                    }
                    if (tree.NormalizedBlendValues && total > 1f)
                        for (int i = 0; i < n; i++) w[i] /= total;
                    return w;
                }
                case 0: // Simple 1D
                {
                    int p = ParameterIndex(tree.Parameter);
                    float x = p >= 0 ? GetAsFloat(p) : 0f;
                    var order = new List<int>();
                    for (int i = 0; i < n; i++) order.Add(i);
                    order.Sort((a, b) => tree.Children[a].Threshold.CompareTo(tree.Children[b].Threshold));
                    if (x <= tree.Children[order[0]].Threshold) { w[order[0]] = 1f; return w; }
                    if (x >= tree.Children[order[n - 1]].Threshold) { w[order[n - 1]] = 1f; return w; }
                    for (int k = 0; k < n - 1; k++)
                    {
                        float a = tree.Children[order[k]].Threshold, b = tree.Children[order[k + 1]].Threshold;
                        if (x < a || x > b) continue;
                        float t = b > a ? (x - a) / (b - a) : 0f;
                        w[order[k]] = 1f - t;
                        w[order[k + 1]] = t;
                        return w;
                    }
                    w[order[0]] = 1f;
                    return w;
                }
                case 1:
                {
                    int px = ParameterIndex(tree.Parameter), py = ParameterIndex(tree.ParameterY);
                    return SimpleDirectional(tree, new Vector2(px >= 0 ? GetAsFloat(px) : 0f, py >= 0 ? GetAsFloat(py) : 0f));
                }
                case 2:
                {
                    int px = ParameterIndex(tree.Parameter), py = ParameterIndex(tree.ParameterY);
                    return PolarWeights(tree, new Vector2(px >= 0 ? GetAsFloat(px) : 0f, py >= 0 ? GetAsFloat(py) : 0f));
                }
                default: // Freeform Cartesian: gradient bands
                {
                    int px = ParameterIndex(tree.Parameter), py = ParameterIndex(tree.ParameterY);
                    var p = new Vector2(px >= 0 ? GetAsFloat(px) : 0f, py >= 0 ? GetAsFloat(py) : 0f);
                    if (n == 1) { w[0] = 1f; return w; }
                    float total = 0f;
                    for (int i = 0; i < n; i++)
                    {
                        var pi = tree.Children[i].Position;
                        float h = 1f;
                        for (int j = 0; j < n; j++)
                        {
                            if (j == i) continue;
                            var pij = tree.Children[j].Position - pi;
                            float d = Vector2.Dot(pij, pij);
                            if (d <= 0f) continue;
                            h = Math.Min(h, 1f - Vector2.Dot(p - pi, pij) / d);
                        }
                        w[i] = Math.Max(0f, h);
                        total += w[i];
                    }
                    if (total > 0f)
                    {
                        for (int i = 0; i < n; i++) w[i] /= total;
                    }
                    else
                    {
                        int best = 0;
                        float bestDistance = float.MaxValue;
                        for (int i = 0; i < n; i++)
                        {
                            float d = (tree.Children[i].Position - p).sqrMagnitude;
                            if (d < bestDistance) { bestDistance = d; best = i; }
                        }
                        w[best] = 1f;
                    }
                    return w;
                }
            }
        }

        static float Cross(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;
        static float Angle(Vector2 a, Vector2 b) => (float)Math.Atan2(Cross(a, b), Vector2.Dot(a, b));

        static float[] SimpleDirectional(BlendTreeMotion tree, Vector2 p)
        {
            int n = tree.Children.Count, center = -1;
            var w = new float[n];
            var directions = new List<int>();
            for (int i = 0; i < n; i++)
                if (tree.Children[i].Position.sqrMagnitude < 1e-12f) center = i; else directions.Add(i);
            if (directions.Count == 0) { w[0] = 1f; return w; }
            if (p.sqrMagnitude < 1e-12f)
            {
                if (center >= 0) w[center] = 1f;
                else for (int i = 0; i < n; i++) w[i] = 1f / n;
                return w;
            }
            directions.Sort((a, b) => Math.Atan2(tree.Children[a].Position.y, tree.Children[a].Position.x)
                .CompareTo(Math.Atan2(tree.Children[b].Position.y, tree.Children[b].Position.x)));
            double angle = Math.Atan2(p.y, p.x);
            int hi = directions.FindIndex(i => Math.Atan2(tree.Children[i].Position.y, tree.Children[i].Position.x) > angle);
            if (hi < 0) hi = 0;
            int a = directions[(hi + directions.Count - 1) % directions.Count], b = directions[hi];
            var pa = tree.Children[a].Position;
            var pb = tree.Children[b].Position;
            float det = Cross(pa, pb);
            // Keep signed-zero division for degenerate fans, as the Editor does. A NaN
            // contribution is inactive; an infinite total removes the center's remainder.
            float wa = Math.Max(0f, Cross(p, pb) / det), wb = Math.Max(0f, Cross(pa, p) / det);
            if (float.IsNaN(wa)) wa = 0f;
            if (float.IsNaN(wb)) wb = 0f;
            float sum = wa + wb;
            if (sum > 1f) { wa /= sum; wb /= sum; sum = 1f; }
            if (float.IsNaN(wa)) wa = 0f;
            if (float.IsNaN(wb)) wb = 0f;
            w[a] += wa; w[b] += wb;
            if (center >= 0) w[center] = 1f - sum;
            else for (int i = 0; i < n; i++) w[i] += (1f - sum) / n;
            return w;
        }

        /// <summary>Gradient bands in polar space (Johansen 2009, eq. 6.14-6.15). The Editor uses
        /// unit angular scale and assigns the query direction to a child at the origin.</summary>
        static float[] PolarWeights(BlendTreeMotion tree, Vector2 p)
        {
            int n = tree.Children.Count;
            var w = new float[n];
            float radius = p.magnitude, total = 0f;
            for (int i = 0; i < n; i++)
            {
                var pi = tree.Children[i].Position;
                float ri = pi.magnitude, h = 1f;
                var direction = ri > 0f ? pi : p;
                float pointAngle = Angle(direction, p);
                for (int j = 0; j < n; j++)
                {
                    if (j == i) continue;
                    var pj = tree.Children[j].Position;
                    float rj = pj.magnitude, average = (ri + rj) * 0.5f;
                    if (average == 0f) continue;
                    var difference = new Vector2((rj - ri) / average, Angle(direction, rj > 0f ? pj : p));
                    float distance = difference.sqrMagnitude;
                    if (distance <= 1e-12f) continue;
                    var query = new Vector2((radius - ri) / average, pointAngle);
                    h = Math.Min(h, 1f - Vector2.Dot(query, difference) / distance);
                }
                w[i] = Math.Max(0f, h);
                total += w[i];
            }
            if (total > 0f) for (int i = 0; i < n; i++) w[i] /= total;
            return w;
        }

        readonly List<AnimationPose> m_DirectScratch = new List<AnimationPose>();
        int m_DirectDepth;

        void SampleEmptyTree(BlendTreeMotion tree, float weight, AnimationPose pose, bool[] mask, bool[] objectMask)
        {
            var (animated, objects) = MotionMask(tree);
            for (int c = 0; c < Bindings.FloatCount; c++)
            {
                if (!animated[c] || (mask != null && !mask[c])) continue;
                pose.Value[c] += weight * Bindings.Defaults[c];
                pose.Weight[c] += weight;
            }
            for (int c = 0; c < Bindings.ObjectCount; c++)
            {
                if (!objects[c] || (objectMask != null && !objectMask[c]) || weight <= pose.ObjectWeight[c]) continue;
                pose.Objects[c] = Bindings.ObjectDefaults[c];
                pose.ObjectWeight[c] = weight;
            }
        }

        void SampleDirect(LayerRun layer, int layerIndex, StateRun run, BlendTreeMotion tree, double normalized,
            float weight, AnimationPose pose, bool[] mask, bool[] objectMask)
        {
            if (m_DirectDepth == m_DirectScratch.Count) m_DirectScratch.Add(new AnimationPose());
            var scratch = m_DirectScratch[m_DirectDepth++];
            scratch.Reset(Bindings.FloatCount, Bindings.ObjectCount);
            try
            {
                var weights = BlendWeights(tree);
                for (int i = 0; i < weights.Length; i++)
                    if (weights[i] > 0f)
                        SampleMotion(layer, layerIndex, run, tree.Children[i].Motion, normalized + tree.Children[i].CycleOffset,
                            weight * weights[i], scratch, mask, objectMask);
                var (animated, animatedObjects) = MotionMask(tree);
                pose.Total += weight;
                for (int c = 0; c < Bindings.FloatCount; c++)
                {
                    if (!animated[c] || (mask != null && !mask[c])) continue;
                    // A Direct node's output has its own weight. Its raw scalar sum may exceed one;
                    // a low sum is completed from the bound default, rather than the previous frame
                    // (in an additive layer: with no difference).
                    pose.Value[c] += scratch.Value[c] + (layer.Def.Additive ? 0f : Math.Max(0f, weight - scratch.Weight[c]) * Bindings.Defaults[c]);
                    pose.Weight[c] += weight;
                }
                for (int c = 0; c < Bindings.ObjectCount; c++)
                {
                    if (!animatedObjects[c] || (objectMask != null && !objectMask[c]) || weight <= pose.ObjectWeight[c]) continue;
                    pose.Objects[c] = scratch.ObjectWeight[c] > 0f ? scratch.Objects[c] : Bindings.ObjectDefaults[c];
                    pose.ObjectWeight[c] = weight;
                }
            }
            finally { m_DirectDepth--; }
        }
    }
}
