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
        internal Object[] Objects = Array.Empty<Object>();
        internal float[] ObjectWeight = Array.Empty<float>();

        internal void Reset(int floats, int objects)
        {
            if (Value.Length < floats)
            {
                Value = new float[floats];
                Weight = new float[floats];
            }
            else
            {
                Array.Clear(Value, 0, floats);
                Array.Clear(Weight, 0, floats);
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
        internal bool Additive;
    }

    internal static class ClipSampling
    {
        /// <summary>Adds the clip's values at <paramref name="time"/> (on the clip's own timeline) with
        /// a weight. <paramref name="mask"/>, when given, excludes channels (layer masks, mixing transforms).</summary>
        internal static void Sample(BoundClip bound, float time, float weight, AnimationPose pose, bool[] mask = null, bool[] objectMask = null)
        {
            if (weight <= 0f) return;
            var curves = bound.Clip.m_Curves;
            var channels = bound.Float;
            for (int i = 0; i < curves.Count && i < channels.Length; i++)
            {
                var curve = curves[i];
                if (curve.QuaternionStart && i + 3 < curves.Count)
                {
                    SampleQuaternion(bound, i, time, weight, pose, mask);
                    i += 3;
                    continue;
                }
                int ch = channels[i];
                if (ch < 0 || (mask != null && !mask[ch])) continue;
                pose.Value[ch] += weight * curve.Curve.Evaluate(time);
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

        static void SampleQuaternion(BoundClip bound, int first, float time, float weight, AnimationPose pose, bool[] mask)
        {
            var curves = bound.Clip.m_Curves;
            int cx = bound.Float[first], cy = bound.Float[first + 1], cz = bound.Float[first + 2], cw = bound.Float[first + 3];
            if (cx < 0 || cy < 0 || cz < 0 || cw < 0) return;
            if (mask != null && !mask[cx]) return;
            float x = curves[first].Curve.Evaluate(time);
            float y = curves[first + 1].Curve.Evaluate(time);
            float z = curves[first + 2].Curve.Evaluate(time);
            float w = curves[first + 3].Curve.Evaluate(time);
            float m = MathF.Sqrt(x * x + y * y + z * z + w * w);
            if (m < 1e-6f) return;
            x /= m; y /= m; z /= m; w /= m;
            // Blend in the same hemisphere as what is accumulated already.
            float dot = pose.Value[cx] * x + pose.Value[cy] * y + pose.Value[cz] * z + pose.Value[cw] * w;
            if (dot < 0f) { x = -x; y = -y; z = -z; w = -w; }
            pose.Value[cx] += weight * x; pose.Weight[cx] += weight;
            pose.Value[cy] += weight * y; pose.Weight[cy] += weight;
            pose.Value[cz] += weight * z; pose.Weight[cz] += weight;
            pose.Value[cw] += weight * w; pose.Weight[cw] += weight;
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
        /// The Animator's rule: layers apply in order over the current values. Within a layer, a
        /// channel's weighted sum is completed to full weight with the value below it (what states
        /// that do not write the property leave there); an override layer then blends over the result
        /// by its weight, an additive layer adds its difference to the defaults.
        /// </summary>
        internal static void WriteLayers(AnimationBindings b, LayerPose[] layers, int count)
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
                ReadCurrent(b, s);
                for (int c = 0; c < n; c++)
                {
                    if (!touched[c]) continue;
                    float r = s.Current[c];
                    for (int l = 0; l < count; l++)
                    {
                        var layer = layers[l];
                        float w = layer.AnimationPose.Weight[c];
                        if (w <= 0f || layer.Weight <= 0f) continue;
                        float v = layer.AnimationPose.Value[c];
                        if (layer.Additive)
                        {
                            r += layer.Weight * (v - w * b.Defaults[c]);
                            continue;
                        }
                        float lv = w >= 1f ? v / w : v + (1f - w) * r;
                        r = layer.Weight >= 1f ? lv : r + (lv - r) * layer.Weight;
                    }
                    s.Result[c] = r;
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
