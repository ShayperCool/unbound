using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Unbound.Runtime
{
    /// <summary>Weighted sums of sampled values per channel: what one layer (or one player) wants to
    /// write this frame. Object channels keep the value of the heaviest contribution.</summary>
    internal sealed class AnimationPose
    {
        internal float[] Value = Array.Empty<float>();
        internal float[] Weight = Array.Empty<float>();
        /// <summary>Weights of the states that do not animate a channel: Keep for motion states
        /// without write defaults (any channel), Fill for motion states with write defaults (the
        /// layer's channels) and for states without a motion and write defaults where the layer has
        /// no last value, Rest for states without a motion with write defaults (the layer's
        /// channels; see <see cref="PoseWriter.CombineLayers"/>).</summary>
        internal float[] Keep = Array.Empty<float>();
        internal float[] Fill = Array.Empty<float>();
        internal float[] Rest = Array.Empty<float>();
        internal Object[] Objects = Array.Empty<Object>();
        internal float[] ObjectWeight = Array.Empty<float>();
        /// <summary>The summed weight of the motions sampled, whatever channels they animate.</summary>
        internal float Total;

        internal void Reset(int floats, int objects)
        {
            Total = 0f;
            if (Value.Length < floats)
            {
                Value = new float[floats];
                Weight = new float[floats];
                Keep = new float[floats];
                Fill = new float[floats];
                Rest = new float[floats];
            }
            else
            {
                Array.Clear(Value, 0, floats);
                Array.Clear(Weight, 0, floats);
                Array.Clear(Keep, 0, floats);
                Array.Clear(Fill, 0, floats);
                Array.Clear(Rest, 0, floats);
            }
            if (Objects.Length < objects)
            {
                Objects = new Object[objects];
                ObjectWeight = new float[objects];
            }
            else
            {
                Array.Clear(Objects, 0, objects);
                Array.Clear(ObjectWeight, 0, objects);
            }
        }
    }

    /// <summary>One layer's pose and how it combines with the layers below it.</summary>
    internal struct LayerPose
    {
        internal AnimationPose AnimationPose;
        internal float Weight;
        /// <summary>Where an override layer's own values go when it is combined (null: nowhere).</summary>
        internal float[] Last;
        internal bool[] Held;
        internal bool Additive;
    }

    internal static class ClipSampling
    {
        /// <summary>Adds the clip's values at <paramref name="time"/> (on the clip's own timeline) with
        /// a weight. <paramref name="mask"/>, when given, excludes channels (layer masks, mixing transforms).
        /// With a <paramref name="reference"/> pose (additive layers) the values are differences from it:
        /// v - reference, and for rotations reference⁻¹·q.</summary>
        internal static void Sample(BoundClip bound, float time, float weight, AnimationPose pose, bool[] mask = null, bool[] objectMask = null,
            float[] reference = null)
        {
            if (weight <= 0f) return;
            pose.Total += weight;
            var curves = bound.Clip.m_Curves;
            var channels = bound.Float;
            for (int i = 0; i < curves.Count && i < channels.Length; i++)
            {
                var curve = curves[i];
                if (curve.QuaternionStart && i + 3 < curves.Count)
                {
                    SampleQuaternion(bound, i, time, weight, pose, mask, reference);
                    i += 3;
                    continue;
                }
                if (curve.EulerStart && bound.EulerRotation != null && i + 2 < curves.Count)
                {
                    int first = bound.EulerRotation[i];
                    if (first >= 0 && (mask == null || mask[first]))
                    {
                        var q = Quaternion.Euler(curve.Evaluate(time), curves[i + 1].Evaluate(time), curves[i + 2].Evaluate(time));
                        // Euler curves measure from the identity where the reference has no rotation.
                        if (reference != null) q = Difference(reference, first, first + 1, first + 2, first + 3, q.x, q.y, q.z, q.w, true);
                        AddQuaternion(pose, first, q.x, q.y, q.z, q.w, weight);
                    }
                    i += 2;
                    continue;
                }
                int ch = channels[i];
                if (ch < 0 || (mask != null && !mask[ch])) continue;
                pose.Value[ch] += weight * (reference != null ? curve.Evaluate(time) - reference[ch] : curve.Evaluate(time));
                pose.Weight[ch] += weight;
            }
            var objectCurves = bound.Clip.m_ObjectCurves;
            for (int i = 0; i < objectCurves.Count && i < bound.Object.Length; i++)
            {
                int ch = bound.Object[i];
                if (ch < 0 || (objectMask != null && !objectMask[ch])) continue;
                if (weight > pose.ObjectWeight[ch])
                {
                    pose.Objects[ch] = objectCurves[i].Evaluate(time);
                    pose.ObjectWeight[ch] = weight;
                }
            }
        }

        static void SampleQuaternion(BoundClip bound, int first, float time, float weight, AnimationPose pose, bool[] mask, float[] reference)
        {
            var curves = bound.Clip.m_Curves;
            int cx = bound.Float[first], cy = bound.Float[first + 1], cz = bound.Float[first + 2], cw = bound.Float[first + 3];
            if (cx < 0 || cy < 0 || cz < 0 || cw < 0) return;
            if (mask != null && !mask[cx]) return;
            float x = curves[first].Evaluate(time);
            float y = curves[first + 1].Evaluate(time);
            float z = curves[first + 2].Evaluate(time);
            float w = curves[first + 3].Evaluate(time);
            float m = MathF.Sqrt(x * x + y * y + z * z + w * w);
            if (m < 1e-6f) return;
            x /= m; y /= m; z /= m; w /= m;
            if (reference != null)
            {
                var d = Difference(reference, cx, cy, cz, cw, x, y, z, w, false);
                x = d.x; y = d.y; z = d.z; w = d.w;
            }
            if (cy == cx + 1 && cz == cx + 2 && cw == cx + 3)
            {
                AddQuaternion(pose, cx, x, y, z, w, weight);
                return;
            }
            // Blend in the same hemisphere as what is accumulated already.
            float dot = pose.Value[cx] * x + pose.Value[cy] * y + pose.Value[cz] * z + pose.Value[cw] * w;
            if (dot < 0f) { x = -x; y = -y; z = -z; w = -w; }
            pose.Value[cx] += weight * x; pose.Weight[cx] += weight;
            pose.Value[cy] += weight * y; pose.Weight[cy] += weight;
            pose.Value[cz] += weight * z; pose.Weight[cz] += weight;
            pose.Value[cw] += weight * w; pose.Weight[cw] += weight;
        }

        /// <summary>r⁻¹·q for the reference rotation r in the given channels. A zero reference (the
        /// reference clip does not rotate the object) gives the zero quaternion, which leaves the
        /// object unrotated, or q for Euler curves when <paramref name="zeroIsIdentity"/>.</summary>
        static Quaternion Difference(float[] reference, int cx, int cy, int cz, int cw, float x, float y, float z, float w, bool zeroIsIdentity)
        {
            var r = new Quaternion(reference[cx], reference[cy], reference[cz], reference[cw]);
            float m = MathF.Sqrt(r.x * r.x + r.y * r.y + r.z * r.z + r.w * r.w);
            if (m < 1e-6f) return zeroIsIdentity ? new Quaternion(x, y, z, w) : new Quaternion(0f, 0f, 0f, 0f);
            r = new Quaternion(-r.x / m, -r.y / m, -r.z / m, r.w / m);
            return r * new Quaternion(x, y, z, w);
        }

        /// <summary>Adds a unit quaternion to four consecutive channels, in the hemisphere of what
        /// they hold already.</summary>
        static void AddQuaternion(AnimationPose pose, int c, float x, float y, float z, float w, float weight)
        {
            float dot = pose.Value[c] * x + pose.Value[c + 1] * y + pose.Value[c + 2] * z + pose.Value[c + 3] * w;
            if (dot < 0f) { x = -x; y = -y; z = -z; w = -w; }
            pose.Value[c] += weight * x; pose.Weight[c] += weight;
            pose.Value[c + 1] += weight * y; pose.Weight[c + 1] += weight;
            pose.Value[c + 2] += weight * z; pose.Weight[c + 2] += weight;
            pose.Value[c + 3] += weight * w; pose.Weight[c + 3] += weight;
        }

        /// <summary>The time on a clip's own curves for a normalized time of a state.</summary>
        internal static float ClipTime(AnimationClip clip, double normalized, bool loop)
        {
            float length = clip.length;
            double t = loop ? normalized - Math.Floor(normalized) : Math.Clamp(normalized, 0.0, 1.0);
            return clip.StartTime + (float)(t * length);
        }

        static readonly ConditionalWeakTable<GameObject, AnimationBindings> s_SampleBindings = new ConditionalWeakTable<GameObject, AnimationBindings>();

        /// <summary>AnimationClip.SampleAnimation: the clip alone, at full weight.</summary>
        internal static void SampleOnce(AnimationClip clip, GameObject go, float time)
        {
            var bindings = s_SampleBindings.GetValue(go, g => new AnimationBindings(g));
            var bound = bindings.Bind(clip);
            var pose = new AnimationPose();
            pose.Reset(bindings.FloatCount, bindings.ObjectCount);
            Sample(bound, clip.StartTime + time, 1f, pose);
            PoseWriter.WriteLayers(bindings, new[] { new LayerPose { AnimationPose = pose, Weight = 1f } }, 1);
        }
    }

    /// <summary>Combines layer poses and writes the result to the animated objects.</summary>
    internal static class PoseWriter
    {
        /// <summary>Buffers of one write. Writing runs game code (OnEnable through SetActive,
        /// OnDidApplyAnimationProperties) that may evaluate animation again, so every write rents
        /// its own and computes all values before the first one is written.</summary>
        sealed class Scratch
        {
            internal float[] Current = Array.Empty<float>();
            internal float[] Result = Array.Empty<float>();
            internal float[] Kept = Array.Empty<float>();
            internal bool[] Touched = Array.Empty<bool>();
            internal Object[] Objects = Array.Empty<Object>();
            internal bool[] ObjectTouched = Array.Empty<bool>();
            internal readonly List<MonoBehaviour> Scripts = new List<MonoBehaviour>();

            internal void Ensure(int floats, int objects)
            {
                if (Current.Length < floats)
                {
                    int size = Math.Max(floats, 64);
                    Current = new float[size];
                    Result = new float[size];
                    Kept = new float[size];
                    Touched = new bool[size];
                }
                if (Objects.Length < objects)
                {
                    int size = Math.Max(objects, 8);
                    Objects = new Object[size];
                    ObjectTouched = new bool[size];
                }
                Array.Clear(Touched, 0, floats);
                Array.Clear(ObjectTouched, 0, objects);
            }
        }

        [ThreadStatic] static Stack<Scratch> t_Free;

        static Scratch Rent(AnimationBindings b)
        {
            t_Free ??= new Stack<Scratch>();
            var s = t_Free.Count > 0 ? t_Free.Pop() : new Scratch();
            s.Ensure(b.FloatCount, b.ObjectCount);
            return s;
        }

        static void Return(Scratch s)
        {
            Array.Clear(s.Objects, 0, s.Objects.Length);
            s.Scripts.Clear();
            t_Free.Push(s);
        }

        /// <summary>
        /// The Animator's rule: layers apply in order over the current values (see <see cref="CombineLayers"/>).
        /// </summary>
        /// <summary>Writes the layers over the current values. With <paramref name="written"/>, what
        /// states without write defaults keep is the value it holds (where <paramref name="held"/>)
        /// instead of the current one, and it receives what is written.</summary>
        internal static void WriteLayers(AnimationBindings b, LayerPose[] layers, int count, float[] written = null, bool[] held = null)
        {
            var s = Rent(b);
            try
            {
                int n = b.FloatCount;
                var touched = s.Touched;
                for (int c = 0; c < n; c++)
                {
                    for (int l = 0; l < count; l++)
                        if (layers[l].AnimationPose.Weight[c] > 0f) { touched[c] = true; break; }
                }
                for (int c = 0; c < n; c++)
                {
                    if (touched[c]) continue;
                    for (int l = 0; l < count; l++)
                    {
                        var p = layers[l].AnimationPose;
                        if (p.Keep[c] > 0f || p.Fill[c] > 0f || p.Rest[c] > 0f) { touched[c] = true; break; }
                    }
                }
                ReadCurrent(b, s);
                Array.Copy(s.Current, s.Result, n);
                float[] kept = null;
                if (written != null)
                {
                    kept = s.Kept;
                    for (int c = 0; c < n; c++) kept[c] = c < held.Length && held[c] ? written[c] : s.Current[c];
                }
                CombineLayers(b, layers, count, s.Result, null, overDefaults: true, kept);
                if (written != null)
                {
                    for (int c = 0; c < n && c < written.Length; c++)
                    {
                        if (!touched[c]) continue;
                        written[c] = s.Result[c];
                        held[c] = true;
                    }
                }
                for (int c = 0; c < b.ObjectCount; c++)
                {
                    for (int l = count - 1; l >= 0; l--)
                    {
                        if (layers[l].AnimationPose.ObjectWeight[c] > 0f && layers[l].Weight > 0f)
                        {
                            s.Objects[c] = layers[l].AnimationPose.Objects[c];
                            s.ObjectTouched[c] = true;
                            break;
                        }
                    }
                }
                Write(b, s);
            }
            finally
            {
                Return(s);
            }
        }

        /// <summary>
        /// Combines layer poses in place of <paramref name="values"/> (the current values), for the
        /// channels a layer pose has weight in (marked in <paramref name="animated"/> when given).
        /// Each layer applies over the result of the layers below it; where none of them writes, over
        /// the default values (<paramref name="overDefaults"/>, else over <paramref name="values"/>).
        /// Within an override layer, the states that do not animate a channel complete its weighted
        /// sum: motion states without write defaults (for any channel of the controller) with the
        /// value below, or where no layer below writes it the value the Animator last wrote
        /// (<paramref name="kept"/>, else the current value); motion states with
        /// write defaults (for the layer's channels), and states without a motion and write defaults
        /// where the layer has no last value, with the default value while another state has the
        /// channel. The rest up to full weight is the value below, which the layer does not keep as
        /// its own. The layer then blends over the value below by its weight, rotations normalized
        /// first unless the rest completed them.
        /// An additive layer's pose holds differences from its clips' reference poses; what a motion
        /// does not animate counts as no difference (a zero quaternion for rotations, so that their
        /// normalized sum leaves it out). Positions, scales and floats add weight × difference,
        /// rotations multiply by the normalized difference whose vector part is scaled by the layer
        /// weight times the weight of the motions sampled (<see cref="AdditiveRotation"/>).
        /// </summary>
        internal static void CombineLayers(AnimationBindings b, LayerPose[] layers, int count, float[] values, bool[] animated, bool overDefaults,
            float[] kept = null)
        {
            int n = Math.Min(b.FloatCount, values.Length);
            for (int c = 0; c < n; c++)
            {
                var owner = b.FloatOwner[c];
                if (owner != null && owner.IsQuaternion && owner.First == c && owner.Width == 4 && c + 3 < n)
                {
                    CombineRotation(b, layers, count, values, animated, c, overDefaults, kept);
                    c += 3;
                    continue;
                }
                float current = kept != null ? kept[c] : values[c], r = overDefaults ? b.Defaults[c] : values[c];
                bool any = false, written = false;
                for (int l = 0; l < count; l++)
                {
                    var layer = layers[l];
                    var pose = layer.AnimationPose;
                    float w = pose.Weight[c], keep = pose.Keep[c];
                    if (w <= 0f && keep <= 0f && pose.Fill[c] <= 0f && pose.Rest[c] <= 0f) continue;
                    any = true;
                    float lw = layer.Weight;
                    if (lw <= 0f) continue;
                    float v = pose.Value[c];
                    if (layer.Additive)
                    {
                        r += lw * v;
                        written = true;
                        continue;
                    }
                    float fill = w > 0f || keep > 0f ? pose.Fill[c] : 0f;
                    v += keep * (written ? r : current) + fill * b.Defaults[c];
                    w += keep + fill;
                    float lv = w >= 1f ? v / w : v + (1f - w) * r;
                    // Only the value below: the layer has no value of its own to keep.
                    bool own = w > 0f || pose.Rest[c] > 0f;
                    if (own && layer.Last != null && c < layer.Last.Length) { layer.Last[c] = lv; layer.Held[c] = true; }
                    r = lw >= 1f ? lv : r + (lv - r) * lw;
                    written = true;
                }
                if (!any) continue;
                values[c] = r;
                if (animated != null) animated[c] = true;
            }
        }

        /// <summary>An additive pose's rotation difference at a weight: the normalized sum, its vector
        /// part scaled by the weight times the pose's motion weight, normalized again. False for a
        /// zero difference.</summary>
        internal static bool AdditiveRotation(AnimationPose pose, int c, float weight, out Quaternion d)
        {
            float dx = pose.Value[c], dy = pose.Value[c + 1], dz = pose.Value[c + 2], dw = pose.Value[c + 3];
            float dm = MathF.Sqrt(dx * dx + dy * dy + dz * dz + dw * dw);
            if (dm < 1e-6f) { d = default; return false; }
            float s = weight * pose.Total / dm;
            dx *= s; dy *= s; dz *= s; dw /= dm;
            dm = MathF.Sqrt(dx * dx + dy * dy + dz * dz + dw * dw);
            d = new Quaternion(dx / dm, dy / dm, dz / dm, dw / dm);
            return true;
        }

        static Quaternion Normalized(float x, float y, float z, float w)
        {
            float m = MathF.Sqrt(x * x + y * y + z * z + w * w);
            return m < 1e-6f ? new Quaternion(0f, 0f, 0f, 0f) : new Quaternion(x / m, y / m, z / m, w / m);
        }

        static void CombineRotation(AnimationBindings b, LayerPose[] layers, int count, float[] values, bool[] animated, int c, bool overDefaults,
            float[] kept)
        {
            var k0 = kept ?? values;
            var current = new Quaternion(k0[c], k0[c + 1], k0[c + 2], k0[c + 3]);
            var src = overDefaults ? b.Defaults : values;
            var r = Normalized(src[c], src[c + 1], src[c + 2], src[c + 3]);
            var defaults = new Quaternion(b.Defaults[c], b.Defaults[c + 1], b.Defaults[c + 2], b.Defaults[c + 3]);
            bool any = false, written = false;
            for (int l = 0; l < count; l++)
            {
                var layer = layers[l];
                var pose = layer.AnimationPose;
                float w = pose.Weight[c], keep = pose.Keep[c];
                if (w <= 0f && keep <= 0f && pose.Fill[c] <= 0f && pose.Rest[c] <= 0f) continue;
                any = true;
                float lw = layer.Weight;
                if (lw <= 0f) continue;
                if (layer.Additive)
                {
                    written = true;
                    if (!AdditiveRotation(pose, c, lw, out var d)) { r = new Quaternion(0f, 0f, 0f, 0f); continue; }
                    if (r.x * r.x + r.y * r.y + r.z * r.z + r.w * r.w < 1e-12f) continue;
                    r = Normalized(r.x, r.y, r.z, r.w) * d;
                    continue;
                }
                // The layer's own rotation: its states' sum completed to full weight, normalized.
                float x = pose.Value[c], y = pose.Value[c + 1], z = pose.Value[c + 2], qw = pose.Value[c + 3];
                float fill = w > 0f || keep > 0f ? pose.Fill[c] : 0f;
                foreach (var (part, q) in new[] { (keep, written ? r : current), (fill, defaults) })
                {
                    if (part <= 0f) continue;
                    float k = x * q.x + y * q.y + z * q.z + qw * q.w < 0f ? -part : part;
                    x += k * q.x; y += k * q.y; z += k * q.z; qw += k * q.w;
                    w += part;
                }
                written = true;
                bool partial = w < 1f;
                if (partial)
                {
                    float f = (x * r.x + y * r.y + z * r.z + qw * r.w < 0f ? -1f : 1f) * (1f - w);
                    x += f * r.x; y += f * r.y; z += f * r.z; qw += f * r.w;
                }
                var lv = Normalized(x, y, z, qw);
                if ((w > 0f || pose.Rest[c] > 0f) && layer.Last != null && c + 3 < layer.Last.Length)
                {
                    layer.Last[c] = lv.x; layer.Last[c + 1] = lv.y; layer.Last[c + 2] = lv.z; layer.Last[c + 3] = lv.w;
                    for (int i = 0; i < 4; i++) layer.Held[c + i] = true;
                }
                if (lw >= 1f || r.x * r.x + r.y * r.y + r.z * r.z + r.w * r.w < 1e-12f) { r = lv; continue; }
                // Completed with the value below, the sum blends over it unnormalized: one
                // normalization of below·(1 - weight·w) + weight·sum (measured in a transition
                // to a state without a motion with write defaults, at half layer weight).
                if (partial) lv = new Quaternion(x, y, z, qw);
                if (Quaternion.Dot(r, lv) < 0f) lv = new Quaternion(-lv.x, -lv.y, -lv.z, -lv.w);
                r = Normalized(r.x + (lv.x - r.x) * lw, r.y + (lv.y - r.y) * lw, r.z + (lv.z - r.z) * lw, r.w + (lv.w - r.w) * lw);
            }
            if (!any) return;
            // A zero quaternion (a difference from a missing reference rotation) leaves the object unrotated.
            if (r.x * r.x + r.y * r.y + r.z * r.z + r.w * r.w < 1e-12f) r = Quaternion.identity;
            values[c] = r.x; values[c + 1] = r.y; values[c + 2] = r.z; values[c + 3] = r.w;
            if (animated != null) for (int i = 0; i < 4; i++) animated[c + i] = true;
        }

        /// <summary>
        /// The legacy Animation rule: higher layers take weight first, lower layers get what is left;
        /// a channel's weight below one is completed with its default value. Additive states add their
        /// difference to the defaults on top.
        /// </summary>
        internal static void WriteLegacy(AnimationBindings b, AnimationPose[] layersHighFirst, int count, AnimationPose additive)
        {
            var s = Rent(b);
            try
            {
                int n = b.FloatCount;
                for (int c = 0; c < n; c++)
                {
                    float remaining = 1f, acc = 0f;
                    bool any = false;
                    for (int l = 0; l < count && remaining > 0f; l++)
                    {
                        float w = layersHighFirst[l].Weight[c];
                        if (w <= 0f) continue;
                        any = true;
                        float use = Math.Min(w, remaining);
                        acc += layersHighFirst[l].Value[c] * (use / w);
                        remaining -= use;
                    }
                    if (additive != null && additive.Weight[c] > 0f)
                    {
                        if (!any) { acc = b.Defaults[c]; remaining = 0f; any = true; }
                        acc += additive.Value[c] - additive.Weight[c] * b.Defaults[c];
                    }
                    s.Touched[c] = any;
                    if (any) s.Result[c] = acc + remaining * b.Defaults[c];
                }
                for (int c = 0; c < b.ObjectCount; c++)
                {
                    for (int l = 0; l < count; l++)
                    {
                        if (layersHighFirst[l].ObjectWeight[c] > 0f)
                        {
                            s.Objects[c] = layersHighFirst[l].Objects[c];
                            s.ObjectTouched[c] = true;
                            break;
                        }
                    }
                }
                ReadCurrent(b, s);
                Write(b, s);
            }
            finally
            {
                Return(s);
            }
        }

        /// <summary>Writes the channels an animation stream animates (playable outputs): their
        /// values as they are, the other channels of their properties as they were.</summary>
        internal static void WriteStream(AnimationBindings b, float[] values, bool[] animated, Object[] objects, bool[] objectAnimated)
        {
            var s = Rent(b);
            try
            {
                int n = Math.Min(b.FloatCount, values.Length);
                for (int c = 0; c < n; c++)
                {
                    s.Touched[c] = animated[c];
                    s.Result[c] = values[c];
                }
                int m = Math.Min(b.ObjectCount, objects.Length);
                for (int c = 0; c < m; c++)
                {
                    s.ObjectTouched[c] = objectAnimated[c];
                    s.Objects[c] = objects[c];
                }
                ReadCurrent(b, s);
                Write(b, s);
            }
            finally
            {
                Return(s);
            }
        }

        /// <summary>Current values of every group with a touched channel (also the base of what is written).</summary>
        static void ReadCurrent(AnimationBindings b, Scratch s)
        {
            foreach (var g in b.Groups)
            {
                if (g.IsObject || !AnyTouched(g, s.Touched) || !g.Alive) continue;
                try { g.Read(s.Current, g.First); }
                catch (Exception e) { Debug.LogException(e); }
            }
        }

        static bool AnyTouched(PropertyGroup g, bool[] touched)
        {
            for (int i = 0; i < g.Width; i++)
                if (touched[g.First + i]) return true;
            return false;
        }

        static void Write(AnimationBindings b, Scratch s)
        {
            // The group list may grow while game code runs: iterate what exists now.
            var groups = b.Groups.ToArray();
            foreach (var g in groups)
            {
                if (!g.Alive) continue;
                try
                {
                    bool changed;
                    if (g.IsObject)
                    {
                        if (!s.ObjectTouched[g.First]) continue;
                        changed = g.WriteObject(s.Objects[g.First]);
                    }
                    else
                    {
                        if (!AnyTouched(g, s.Touched)) continue;
                        for (int i = 0; i < g.Width; i++)
                            if (s.Touched[g.First + i]) s.Current[g.First + i] = s.Result[g.First + i];
                        changed = g.Write(s.Current, g.First);
                    }
                    if (changed && g.Script != null && !s.Scripts.Contains(g.Script)) s.Scripts.Add(g.Script);
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                }
            }
            NotifyScripts(s.Scripts);
        }

        static readonly ConcurrentDictionary<Type, MethodInfo> s_DidApply = new ConcurrentDictionary<Type, MethodInfo>();

        /// <summary>Scripts whose fields animation changed get OnDidApplyAnimationProperties, as in
        /// Unity (uGUI graphics mark themselves dirty there).</summary>
        static void NotifyScripts(List<MonoBehaviour> scripts)
        {
            if (scripts.Count == 0) return;
            foreach (var mb in scripts.ToArray())
            {
                if (mb == null) continue;
                var method = s_DidApply.GetOrAdd(mb.GetType(), t => ScriptMessages.Find(t, "OnDidApplyAnimationProperties", Type.EmptyTypes));
                if (method == null) continue;
                try { method.Invoke(mb, null); }
                catch (TargetInvocationException e) { Debug.LogException(e.InnerException ?? e, mb); }
            }
        }
    }

    /// <summary>Animation events: which ones a step of time crosses, and calling them on scripts.</summary>
    internal static class AnimationEvents
    {
        /// <summary>
        /// Fires the events of <paramref name="clip"/> whose times a state crossed going from
        /// normalized time <paramref name="from"/> to <paramref name="to"/> (unwrapped: the integer part
        /// counts loops). <paramref name="includeStart"/> also fires events exactly at
        /// <paramref name="from"/> (the state just started).
        /// </summary>
        internal static void FireCrossed(AnimationClip clip, double from, double to, bool loop, bool includeStart, Action<AnimationEvent> fire)
        {
            var events = clip.m_Events;
            if (events.Count == 0) return;
            float length = clip.length;
            float start = clip.StartTime;
            if (to >= from)
            {
                if (!loop)
                {
                    if (from > 1.0 && !includeStart) return;
                    double hi = Math.Min(to, 1.0);
                    foreach (var e in events.ToArray())
                    {
                        double n = Normalized(e, start, length);
                        if ((includeStart ? n >= from : n > from) && n <= hi) fire(e);
                    }
                    return;
                }
                long first = (long)Math.Floor(from), last = (long)Math.Floor(to);
                if (last - first > 64) first = last - 64; // a huge step does not replay hundreds of loops
                for (long k = first; k <= last; k++)
                {
                    foreach (var e in events.ToArray())
                    {
                        double t = k + Normalized(e, start, length);
                        if ((includeStart ? t >= from : t > from) && t <= to) fire(e);
                    }
                }
            }
            else
            {
                // Playing backwards: events in [to, from).
                if (!loop)
                {
                    double lo = Math.Max(to, 0.0);
                    for (int i = events.Count - 1; i >= 0; i--)
                    {
                        double n = Normalized(events[i], start, length);
                        if (n >= lo && (includeStart ? n <= from : n < from)) fire(events[i]);
                    }
                    return;
                }
                long first = (long)Math.Floor(from), last = (long)Math.Floor(to);
                if (first - last > 64) last = first - 64;
                for (long k = first; k >= last; k--)
                {
                    for (int i = events.Count - 1; i >= 0; i--)
                    {
                        double t = k + Normalized(events[i], start, length);
                        if (t >= to && (includeStart ? t <= from : t < from)) fire(events[i]);
                    }
                }
            }
        }

        static double Normalized(AnimationEvent e, float start, float length) =>
            length > 0f ? (e.m_Time - start) / length : 0.0;

        static readonly ConcurrentDictionary<(Type, string), MethodInfo[]> s_Methods = new ConcurrentDictionary<(Type, string), MethodInfo[]>();

        /// <summary>Calls the event's function on every script of the GameObject that has it, with the
        /// argument its parameter asks for (none, float, int, string, object reference, enum or the
        /// AnimationEvent itself).</summary>
        internal static void Dispatch(GameObject go, AnimationEvent e, AnimationClip clip)
        {
            if (go == null || string.IsNullOrEmpty(e.m_FunctionName)) return;
            bool received = false;
            foreach (var c in go.m_Components.ToArray())
            {
                if (!(c is MonoBehaviour mb) || mb.m_Destroyed) continue;
                var methods = s_Methods.GetOrAdd((mb.GetType(), e.m_FunctionName), key => FindMethods(key.Item1, key.Item2));
                if (methods.Length == 0) continue;
                received = true;
                var method = methods[0];
                var parameters = method.GetParameters();
                object[] args = null;
                if (parameters.Length == 1)
                {
                    var p = parameters[0].ParameterType;
                    object arg;
                    if (p == typeof(float)) arg = e.m_FloatParameter;
                    else if (p == typeof(int)) arg = e.m_IntParameter;
                    else if (p == typeof(string)) arg = e.m_StringParameter;
                    else if (p == typeof(AnimationEvent)) arg = e;
                    else if (p.IsEnum) arg = Enum.ToObject(p, e.m_IntParameter);
                    else arg = e.m_ObjectReferenceParameter != null && p.IsInstanceOfType(e.m_ObjectReferenceParameter) ? e.m_ObjectReferenceParameter : null;
                    args = new[] { arg };
                }
                object result = null;
                try
                {
                    result = method.Invoke(mb, args);
                }
                catch (TargetInvocationException ex)
                {
                    Debug.LogException(ex.InnerException ?? ex, mb);
                }
                if (result is IEnumerator routine && mb.isActiveAndEnabled)
                    Coroutines.Start(mb, routine, e.m_FunctionName);
            }
            if (!received && e.messageOptions == SendMessageOptions.RequireReceiver)
                Debug.LogError($"'{go.name}' AnimationEvent '{e.m_FunctionName}' on animation '{clip?.name}' has no receiver! Are you missing a component?", go);
        }

        static MethodInfo[] FindMethods(Type type, string name)
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
            var list = new List<MethodInfo>();
            for (var t = type; t != null && t != typeof(MonoBehaviour); t = t.BaseType)
            {
                foreach (var m in t.GetMethods(flags))
                {
                    if (m.Name != name || m.IsGenericMethodDefinition) continue;
                    var parameters = m.GetParameters();
                    if (parameters.Length > 1) continue;
                    if (parameters.Length == 1 && !Supported(parameters[0].ParameterType)) continue;
                    list.Add(m);
                }
            }
            // A method with a parameter wins over a parameterless overload.
            list.Sort((a, b) => b.GetParameters().Length.CompareTo(a.GetParameters().Length));
            return list.ToArray();
        }

        static bool Supported(Type p) =>
            p == typeof(float) || p == typeof(int) || p == typeof(string) || p == typeof(AnimationEvent) || p.IsEnum ||
            typeof(Object).IsAssignableFrom(p) || p == typeof(object);
    }
}
