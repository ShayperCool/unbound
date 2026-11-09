using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Unbound.Runtime
{
    /// <summary>
    /// An animated property of one object, written as a whole: a Transform's position, a color, a
    /// field. Its float channels are consecutive in the bindings' channel space; an object-valued
    /// property (a sprite) has one object channel instead.
    /// </summary>
    internal abstract class PropertyGroup
    {
        internal Object Target;
        /// <summary>The Transform of the animated object (animation masks and mixing transforms).</summary>
        internal Transform Transform;
        internal int First;
        internal int Width;
        internal bool IsObject;
        /// <summary>A rotation's four channels (x, y, z, w): blended as a quaternion.</summary>
        internal bool IsQuaternion;
        /// <summary>A Transform's scale (an additive reference without it subtracts one).</summary>
        internal bool IsScale;
        /// <summary>A humanoid body curve (RootT, RootQ): additive layers leave it to the root
        /// motion pass, which composes their body differences (<see cref="AnimatorPlayer"/>).</summary>
        internal bool HumanBody;
        /// <summary>The humanoid curve (<see cref="Unbound.Runtime.Humanoid.HumanClipImport"/>) this
        /// writes, −1 for other properties: layer masks filter it by its body part.</summary>
        internal int HumanCurve = -1;

        /// <summary>A script whose fields this writes: it gets OnDidApplyAnimationProperties.</summary>
        internal MonoBehaviour Script;

        /// <summary>A skeleton node's property (<see cref="SkeletonGroup"/>): its path below the root,
        /// which masks filter it by.</summary>
        internal string SkeletonPath;

        internal bool Alive => Target != null;

        /// <summary>Current values into values[offset..offset+Width).</summary>
        internal virtual void Read(float[] values, int offset) { }

        /// <summary>Writes values[offset..offset+Width); returns whether anything changed.</summary>
        internal virtual bool Write(float[] values, int offset) => false;

        internal virtual Object ReadObject() => null;

        internal virtual bool WriteObject(Object value) => false;
    }

    /// <summary>A float, vector or color property read and written through accessors.</summary>
    internal sealed class AccessorGroup : PropertyGroup
    {
        readonly Func<Vector4> m_Get;
        readonly Action<Vector4> m_Set;
        /// <summary>Written even when the value reads the same (the getter gives the default, not
        /// what was written: material channels).</summary>
        internal bool AlwaysWrite;

        internal AccessorGroup(Object target, int width, Func<Vector4> get, Action<Vector4> set)
        {
            Target = target;
            Width = width;
            m_Get = get;
            m_Set = set;
        }

        internal override void Read(float[] values, int offset)
        {
            var v = m_Get();
            for (int i = 0; i < Width; i++) values[offset + i] = v[i];
        }

        internal override bool Write(float[] values, int offset)
        {
            var current = m_Get();
            var v = current;
            for (int i = 0; i < Width; i++) v[i] = values[offset + i];
            if (!AlwaysWrite && v.x == current.x && v.y == current.y && v.z == current.z && v.w == current.w) return false;
            m_Set(v);
            return true;
        }
    }

    /// <summary>An object reference property written through accessors.</summary>
    internal sealed class ObjectAccessorGroup : PropertyGroup
    {
        readonly Func<Object> m_Get;
        readonly Action<Object> m_Set;

        internal ObjectAccessorGroup(Object target, Func<Object> get, Action<Object> set)
        {
            Target = target;
            IsObject = true;
            m_Get = get;
            m_Set = set;
        }

        internal override Object ReadObject() => m_Get();

        internal override bool WriteObject(Object value)
        {
            if (ReferenceEquals(m_Get(), value)) return false;
            m_Set(value);
            return true;
        }
    }

    /// <summary>
    /// An Animator float parameter channel: clip curves of its name (Animator parameter curves,
    /// documented, 6000.0 Manual "Animation curves on imported clips": a curve with the name of a
    /// parameter in the Animator Controller gives that parameter its value; the curves of several
    /// clips of the same controller blend, a clip without the curve blends with the parameter's
    /// default).
    /// <list type="bullet">
    /// <item><see cref="OwnDriven"/>: the Animator's own controller had a float of this name when the
    /// channel was bound. Every write drives the Animator's own parameter, exactly as the plain
    /// accessor did before (any curve of the name on these bindings, whoever plays it).</item>
    /// <item>A controller playable (an AnimatorControllerPlayable on an animation output targeting
    /// the Animator) whose own clips feed this channel, and whose controller has a float of this
    /// name, owns its parameter of the channel, whether or not the Animator's controller has one.
    /// The channel's writes never reach it: the playable takes its own controller's layered sample
    /// when it is evaluated (<see cref="PlayableParameters.Sample"/>), before outer graph mixing.</item>
    /// </list>
    /// </summary>
    internal sealed class ParameterCurveGroup : PropertyGroup
    {
        readonly Animator m_Animator;
        internal readonly string Name;
        /// <summary>The Animator's own controller has the float (decided when bound, as before).</summary>
        internal readonly bool OwnDriven;
        /// <summary>The bindings the channel belongs to; null for a scene handle (own only).</summary>
        internal AnimationBindings Bindings;
        /// <summary>The clip whose curve made a channel for a controller playable's parameter (its
        /// player is being made while the channel is bound): see <see cref="PlayableValue"/>.</summary>
        internal AnimationClip MadeBy;
        AnimatorPlayer m_FedPlayer;
        int m_FedEpoch = -1;
        int m_FedOverride;
        bool m_Fed;

        internal ParameterCurveGroup(Animator animator, string name, bool ownDriven)
        {
            Target = animator;
            Width = 1;
            m_Animator = animator;
            Name = name;
            OwnDriven = ownDriven;
        }

        internal override void Read(float[] values, int offset) =>
            values[offset] = OwnDriven ? m_Animator.GetParameterAsFloat(Name) : PlayableValue();

        /// <summary>The own parameter, as the accessor wrote it (only when the value changes). A
        /// controller playable's parameter is never written from the channel: the channel holds the
        /// Animator-wide blend of every source (outputs, mixers, other playables, jobs and stream
        /// writes), not its controller's own sample.</summary>
        internal override bool Write(float[] values, int offset)
        {
            float v = values[offset];
            if (!OwnDriven || v == m_Animator.GetParameterAsFloat(Name)) return false;
            m_Animator.SetParameterFromCurve(Name, v);
            return true;
        }

        /// <summary>The channel's value on the playables' side (what is captured as its default and
        /// read as its current value; no playable parameter is written from it): the first owning
        /// playable's parameter, else, while an owning playable's player is being made (its clips
        /// binding), its controller's default. Only a controller whose own clips feed the channel
        /// counts: an unrelated playable's default never does. Which owning playable comes first
        /// (the graph's node order) is Unbound's choice (unmeasured).</summary>
        float PlayableValue()
        {
            foreach (var kind in PlayableParameters.ControllersOn(m_Animator))
            {
                var player = kind.Player;
                if (player != null)
                {
                    int i = FedIndex(player);
                    if (i < 0 && !player.Attached && (Bindings == null || player.Bindings == Bindings) && PlayableParameters.Plays(player.Controller, player.Graph, MadeBy))
                        i = PlayableParameters.FloatIndex(player.Graph, Name);
                    if (i >= 0) return player.Floats[i];
                    continue;
                }
                var controller = kind.Controller;
                var graph = controller.Graph;
                int d = PlayableParameters.FloatIndex(graph, Name);
                if (d < 0) continue;
                if (PlayableParameters.Plays(controller, graph, MadeBy) || (Bindings != null && Bindings.Feeds(controller, graph, First)))
                    return graph.Parameters[d].DefaultFloat;
            }
            return 0f;
        }

        /// <summary>The index of this float parameter in a controller playable's player whose own
        /// clips feed this channel through these bindings, or -1.</summary>
        internal int FedIndex(AnimatorPlayer player)
        {
            if (player == null || player.Attached || Bindings == null || player.Bindings != Bindings) return -1;
            int i = PlayableParameters.FloatIndex(player.Graph, Name);
            if (i < 0) return -1;
            int version = player.Controller.OverrideVersion;
            if (!ReferenceEquals(player, m_FedPlayer) || m_FedEpoch != Bindings.ClipEpoch || m_FedOverride != version)
            {
                m_FedPlayer = player;
                m_FedEpoch = Bindings.ClipEpoch;
                m_FedOverride = version;
                m_Fed = Bindings.Feeds(player, First);
            }
            return m_Fed ? i : -1;
        }
    }

    /// <summary>The controller playables that animate an Animator (through animation outputs
    /// targeting it), for the parameters their clips drive.</summary>
    internal static class PlayableParameters
    {
        internal static bool HasOutputs(Animator animator) =>
            animator != null && Unbound.Runtime.Playables.AnimationOutputs.Of(animator) is { Count: > 0 };

        /// <summary>The controller playables of the live graphs with an animation output targeting
        /// <paramref name="animator"/>, each graph once, connected to the output or not (they may
        /// own a parameter channel; only an evaluated one samples it).</summary>
        internal static IEnumerable<Unbound.Runtime.Playables.ControllerKind> ControllersOn(Animator animator)
        {
            var outputs = animator != null ? Unbound.Runtime.Playables.AnimationOutputs.Of(animator) : null;
            if (outputs == null) yield break;
            for (int o = 0; o < outputs.Count; o++)
            {
                var output = outputs[o];
                var graph = output.Graph;
                if (output.Destroyed || graph == null || graph.Destroyed) continue;
                if (!ReferenceEquals((output.Engine as Unbound.Runtime.Playables.AnimationOutputState)?.Target, animator)) continue;
                bool seen = false;
                for (int e = 0; e < o && !seen; e++) seen = ReferenceEquals(outputs[e].Graph, graph) && !outputs[e].Destroyed;
                if (seen) continue;
                foreach (var node in graph.Nodes)
                    if (!node.Destroyed && node.Kind is Unbound.Runtime.Playables.ControllerKind kind && kind.Controller != null)
                        yield return kind;
            }
        }

        internal static int FloatIndex(ControllerGraph graph, string name)
        {
            int i = graph != null ? graph.ParameterIndex(name) : -1;
            return i >= 0 && graph.Parameters[i].Type == AnimatorControllerParameterType.Float ? i : -1;
        }

        /// <summary>Whether <paramref name="clip"/> is one of the controller's clips (after overrides).</summary>
        internal static bool Plays(RuntimeAnimatorController controller, ControllerGraph graph, AnimationClip clip)
        {
            if (clip == null || controller == null || graph == null) return false;
            clip = clip.Asset;
            foreach (var original in graph.Clips)
                if (ReferenceEquals(controller.MapClip(original), clip)) return true;
            return false;
        }

        /// <summary>Whether a controller playable on <paramref name="animator"/> plays
        /// <paramref name="clip"/> (among its controller's clips, after overrides) and has a float
        /// parameter named <paramref name="name"/>: the clip's curve of that name then drives it.
        /// The Animator's own controller graph is not a playable here, nor is a playable whose
        /// player animates another Animator (a graph whose outputs target several Animators: its
        /// player is made for the first, as before): such a curve stays reported as not supported.</summary>
        internal static bool Owned(Animator animator, string name, AnimationClip clip)
        {
            if (clip == null || !HasOutputs(animator)) return false;
            foreach (var kind in ControllersOn(animator))
            {
                var player = kind.Player;
                if (player != null && (player.Attached || !ReferenceEquals(player.Bindings.Root, animator.m_GameObject))) continue;
                var controller = kind.Controller;
                if (FloatIndex(controller.Graph, name) < 0) continue;
                if (Plays(controller, controller.Graph, clip)) return true;
            }
            return false;
        }

        sealed class SampleMap
        {
            internal int Epoch = -1;
            internal int Override;
            internal int Count;
            internal int Parameters;
            internal int[] Parameter = Array.Empty<int>();
            internal int[] Channel = Array.Empty<int>();
        }

        static readonly System.Runtime.CompilerServices.ConditionalWeakTable<AnimatorPlayer, SampleMap> s_Maps =
            new System.Runtime.CompilerServices.ConditionalWeakTable<AnimatorPlayer, SampleMap>();

        /// <summary>
        /// A controller playable's own parameter curves, right after its player evaluated its pose
        /// (AnimatorControllerPlayable's evaluation, before the graph mixes it with anything): each
        /// float parameter whose channel its own clips feed takes that player's layered sample of the
        /// channel (<see cref="AnimatorPlayer.LayeredSample"/>), over the parameter's own default.
        /// Nothing else writes it: not the Animator-wide channel, other playables, other graphs,
        /// jobs or stream writes; a playable that is not evaluated (unconnected, its graph not
        /// evaluated) keeps its values.
        /// Documented (6000.0 Manual, "Animation curves on imported clips"): the parameter takes the
        /// curve's value; the clips of the same controller blend; a clip without the curve blends
        /// with the parameter's default. Unbound's choices (unmeasured): the outer graph's weights
        /// (outputs, mixers, layer mixers) do not scale a playable's own sample, so a playable at
        /// weight 0 is driven too; "keep" (a state without write defaults) holds the parameter's
        /// current value; the sample is taken at each evaluation of the playable (a playable reached
        /// twice samples twice, with the same layers); a layer that animates nothing of the channel
        /// leaves the parameter unchanged.
        /// </summary>
        internal static void Sample(AnimatorPlayer player, Animator animator)
        {
            if (player == null || player.Attached || animator == null) return;
            var bindings = player.Bindings;
            var graph = player.Graph;
            if (bindings == null || graph == null || graph.Parameters.Length == 0) return;
            var map = s_Maps.GetValue(player, _ => new SampleMap());
            int version = player.Controller.OverrideVersion;
            if (map.Epoch != bindings.ClipEpoch || map.Override != version || map.Parameters != graph.Parameters.Length)
            {
                map.Epoch = bindings.ClipEpoch;
                map.Override = version;
                map.Parameters = graph.Parameters.Length;
                map.Count = 0;
                for (int i = 0; i < graph.Parameters.Length; i++)
                {
                    if (graph.Parameters[i].Type != AnimatorControllerParameterType.Float) continue;
                    var group = bindings.ParameterChannel(animator, graph.Parameters[i].Name);
                    if (group == null || group.FedIndex(player) != i) continue;
                    if (map.Count == map.Parameter.Length)
                    {
                        Array.Resize(ref map.Parameter, Math.Max(4, map.Count * 2));
                        Array.Resize(ref map.Channel, map.Parameter.Length);
                    }
                    map.Parameter[map.Count] = i;
                    map.Channel[map.Count] = group.First;
                    map.Count++;
                }
            }
            for (int k = 0; k < map.Count; k++)
            {
                int i = map.Parameter[k];
                if (player.LayeredSample(map.Channel[k], graph.Parameters[i].DefaultFloat, player.Floats[i], out float value))
                    player.Floats[i] = value;
            }
        }
    }

    /// <summary>A serialized field of a script (or an engine object's own field), by its property
    /// path (`m_Color.r`): numbers, bools and enums are one float channel each.</summary>
    internal sealed class FieldGroup : PropertyGroup
    {
        readonly FieldInfo[] m_Chain;
        readonly Type m_Leaf;

        internal FieldGroup(Object target, FieldInfo[] chain)
        {
            Target = target;
            m_Chain = chain;
            m_Leaf = chain[chain.Length - 1].FieldType;
            IsObject = typeof(Object).IsAssignableFrom(m_Leaf);
            Width = IsObject ? 0 : 1;
        }

        internal static bool IsAnimatable(Type t) =>
            t == typeof(float) || t == typeof(double) || t == typeof(int) || t == typeof(uint) || t == typeof(bool) || t.IsEnum ||
            t == typeof(short) || t == typeof(ushort) || t == typeof(byte) || t == typeof(sbyte) || t == typeof(long) || t == typeof(ulong) ||
            typeof(Object).IsAssignableFrom(t);

        object GetLeaf()
        {
            object o = Target;
            foreach (var f in m_Chain)
            {
                if (o == null) return null;
                o = f.GetValue(o);
            }
            return o;
        }

        void SetLeaf(object value) => SetAt(Target, 0, value);

        void SetAt(object owner, int depth, object value)
        {
            var field = m_Chain[depth];
            if (depth == m_Chain.Length - 1)
            {
                field.SetValue(owner, value);
                return;
            }
            var child = field.GetValue(owner);
            if (child == null) return;
            SetAt(child, depth + 1, value);
            if (field.FieldType.IsValueType) field.SetValue(owner, child); // write the modified copy back
        }

        internal override void Read(float[] values, int offset) => values[offset] = ToFloat(GetLeaf());

        internal override bool Write(float[] values, int offset)
        {
            var current = GetLeaf();
            var value = FromFloat(values[offset]);
            if (Equals(current, value)) return false;
            SetLeaf(value);
            return true;
        }

        internal override Object ReadObject() => GetLeaf() as Object;

        internal override bool WriteObject(Object value)
        {
            if (value != null && !m_Leaf.IsInstanceOfType(value)) return false;
            if (ReferenceEquals(GetLeaf(), value)) return false;
            SetLeaf(value);
            return true;
        }

        static float ToFloat(object v)
        {
            switch (v)
            {
                case null: return 0f;
                case float f: return f;
                case bool b: return b ? 1f : 0f;
                case Enum e: return Convert.ToInt64(e);
                default: return Convert.ToSingle(v);
            }
        }

        object FromFloat(float v)
        {
            var t = m_Leaf;
            if (t == typeof(float)) return v;
            // A blended bool is true while any curve keeps it from 0 (measured: true through a
            // transition away from the clip that sets it).
            if (t == typeof(bool)) return v != 0f;
            if (t == typeof(double)) return (double)v;
            // Integers round half up (0.5 is 1, 2.5 is 3: measured).
            long n = (long)Math.Floor(v + 0.5f);
            if (t.IsEnum) return Enum.ToObject(t, n);
            return Convert.ChangeType(n, t);
        }
    }

    /// <summary>A stepped-and-blended view of every property one animation player animates on its
    /// hierarchy: groups, their channels and default values, and the clips bound to them.</summary>
    internal sealed class AnimationBindings
    {
        static int[] NewMinusOnes(int n)
        {
            var a = new int[n];
            for (int i = 0; i < n; i++) a[i] = -1;
            return a;
        }

        internal readonly GameObject Root;
        internal readonly List<PropertyGroup> Groups = new List<PropertyGroup>();
        internal int FloatCount;
        internal int ObjectCount;
        /// <summary>Values when the property was bound: what write-defaults restores.</summary>
        internal float[] Defaults = new float[16];
        internal Object[] ObjectDefaults = new Object[4];
        internal PropertyGroup[] FloatOwner = new PropertyGroup[16];
        internal PropertyGroup[] ObjectOwner = new PropertyGroup[4];
        int[] m_HumanChannels;
        int m_HumanChannelsFor = -1;

        /// <summary>The float channel a humanoid curve is bound to (-1 when it is not bound).</summary>
        internal int HumanChannel(int curve)
        {
            if (m_HumanChannelsFor != FloatCount)
            {
                m_HumanChannels ??= new int[Unbound.Runtime.Humanoid.HumanClipImport.CurveCount];
                Array.Fill(m_HumanChannels, -1);
                foreach (var g in Groups)
                    if (g.HumanCurve >= 0 && g.HumanCurve < m_HumanChannels.Length && g.First < FloatCount) m_HumanChannels[g.HumanCurve] = g.First;
                m_HumanChannelsFor = FloatCount;
            }
            return curve >= 0 && curve < m_HumanChannels.Length ? m_HumanChannels[curve] : -1;
        }
        readonly Dictionary<(Object, string), PropertyGroup> m_Index = new Dictionary<(Object, string), PropertyGroup>();
        readonly Dictionary<AnimationClip, BoundClip> m_Clips = new Dictionary<AnimationClip, BoundClip>();

        /// <summary>The bound properties by target and key.</summary>
        internal IReadOnlyDictionary<(Object, string), PropertyGroup> Index => m_Index;

        /// <summary>The skeleton of an Animator without its transform hierarchy that groups here
        /// animate: posed after each write.</summary>
        internal AnimatorSkeleton Skeleton;

        /// <summary>Puts <paramref name="group"/> in place of <paramref name="old"/> on the same
        /// channels (an optimized or deoptimized hierarchy's transform properties).</summary>
        internal void Replace(PropertyGroup old, (Object, string) key, PropertyGroup group)
        {
            int index = Groups.IndexOf(old);
            if (index < 0 || group.Width != old.Width || group.IsObject != old.IsObject) return;
            group.First = old.First;
            group.Transform = group is SkeletonGroup ? null : (key.Item1 as Component)?.transform;
            Groups[index] = group;
            for (int i = 0; i < group.Width; i++) FloatOwner[group.First + i] = group;
            foreach (var entry in m_Index)
                if (ReferenceEquals(entry.Value, old))
                {
                    m_Index.Remove(entry.Key);
                    break;
                }
            m_Index[key] = group;
            if (group is SkeletonGroup skeleton) Skeleton = skeleton.Skeleton;
            ClipEpoch++;
        }

        /// <summary>Changes whenever a clip is (re)bound: what <see cref="ParameterCurveGroup"/>
        /// caches which clips feed it by.</summary>
        internal int ClipEpoch;

        internal AnimationBindings(GameObject root)
        {
            Root = root;
        }

        internal BoundClip Bind(AnimationClip clip)
        {
            clip.EnsureHumanCurves();
            if (m_Clips.TryGetValue(clip, out var bound) && bound.Version == clip.m_Version && !NowOwned(bound)) return bound;
            bound = Map(clip, create: true);
            m_Clips[clip] = bound;
            ClipEpoch++;
            return bound;
        }

        /// <summary>A clip bound before a controller playable that plays it existed (one it shares
        /// with the Animator's own controller, say) left its curves of that playable's float
        /// parameters unbound: it binds again once such a playable is there. Clips without such
        /// curves, and Animators without animation outputs, keep their binding as before.</summary>
        static bool NowOwned(BoundClip bound)
        {
            if (bound.MissedParameters == null) return false;
            foreach (var (animator, name) in bound.MissedParameters)
                if (PlayableParameters.Owned(animator, name, bound.Clip)) return true;
            return false;
        }

        /// <summary>A clip's curves on channels; without <paramref name="create"/> only on the
        /// properties bound already (-1 for the others).</summary>
        BoundClip Map(AnimationClip clip, bool create)
        {
            var bound = new BoundClip { Clip = clip, Version = clip.m_Version };
            bound.Float = new int[clip.m_Curves.Count];
            for (int i = 0; i < clip.m_Curves.Count; i++)
            {
                var curve = clip.m_Curves[i];
                if (curve.EulerStart && i + 2 < clip.m_Curves.Count)
                {
                    // Euler angles animate the transform's rotation channels as a quaternion.
                    var b = curve.Binding;
                    var rotation = new CurveBinding
                    {
                        Path = b.Path, ClassId = b.ClassId, Script = b.Script, Hashed = b.Hashed, PathHash = b.PathHash,
                        Attribute = "m_LocalRotation.x",
                    };
                    int ch = BindCurve(rotation, objectValued: false, clip, create);
                    bound.EulerRotation ??= NewMinusOnes(clip.m_Curves.Count);
                    bound.EulerRotation[i] = ch;
                    bound.Float[i] = ch;
                    bound.Float[i + 1] = ch >= 0 ? ch + 1 : -1;
                    bound.Float[i + 2] = ch >= 0 ? ch + 2 : -1;
                    i += 2;
                    continue;
                }
                bound.Float[i] = BindCurve(curve.Binding, objectValued: false, clip, create, bound);
            }
            bound.Object = new int[clip.m_ObjectCurves.Count];
            for (int i = 0; i < clip.m_ObjectCurves.Count; i++)
                bound.Object[i] = BindCurve(clip.m_ObjectCurves[i].Binding, objectValued: true, clip, create);
            return bound;
        }

        /// <summary>
        /// The pose an additive layer subtracts from a clip, per channel: the clip's additive
        /// reference clip at the reference time, else the clip's first frame. Channels the reference
        /// clip does not animate subtract 0, a scale 1; a missing rotation is the zero quaternion
        /// (see <see cref="ClipSampling.Sample"/>).
        /// </summary>
        internal float[] AdditiveReference(BoundClip bound)
        {
            var clip = bound.Clip;
            var source = clip.m_HasAdditiveReferencePose && clip.m_AdditiveReferencePoseClip != null ? clip.m_AdditiveReferencePoseClip : clip;
            if (bound.Reference != null && bound.ReferenceFloatCount == FloatCount && bound.ReferenceVersion == source.m_Version)
                return bound.Reference;
            var pose = new AnimationPose();
            pose.Reset(FloatCount, ObjectCount);
            // Without a reference clip, the clip's first frame; with one (the clip itself too, as
            // the model importer refers to frames within the clip), that clip at the time.
            if (source == clip && !(clip.m_HasAdditiveReferencePose && clip.m_AdditiveReferencePoseClip != null)) ClipSampling.Sample(bound, clip.StartTime, 1f, pose);
            else if (source == clip) ClipSampling.Sample(bound, clip.StartTime + clip.m_AdditiveReferencePoseTime, 1f, pose);
            else ClipSampling.Sample(Map(source, create: false), source.StartTime + clip.m_AdditiveReferencePoseTime, 1f, pose);
            var reference = new float[FloatCount];
            for (int c = 0; c < FloatCount; c++)
            {
                if (pose.Weight[c] > 0f) reference[c] = pose.Value[c] / pose.Weight[c];
                else if (FloatOwner[c].IsScale) reference[c] = 1f;
            }
            bound.Reference = reference;
            bound.ReferenceFloatCount = FloatCount;
            bound.ReferenceVersion = source.m_Version;
            return reference;
        }

        /// <summary>The bound clips (a player's write-defaults set).</summary>
        internal IEnumerable<BoundClip> BoundClips => m_Clips.Values;

        /// <summary>The first channel of a property already bound ("position", "rotation", "scale"
        /// of a Transform, ...), or -1.</summary>
        internal int ChannelOf(Object target, string key) =>
            m_Index.TryGetValue((target, key), out var g) && !g.IsObject ? g.First : -1;

        /// <summary>A custom stream property (no object behind it), by name.</summary>
        internal int BindCustom(Object owner, string name)
        {
            var key = (owner, "custom:" + name);
            if (!m_Index.TryGetValue(key, out var group))
            {
                group = new Unbound.Runtime.Playables.CustomGroup(owner);
                Add(group);
                m_Index[key] = group;
            }
            return group.First;
        }

        /// <summary>The channel of one property (a stream handle's binding): its first channel for
        /// multi-channel properties; -1 when the property cannot be animated.</summary>
        internal int BindChannel(CurveBinding binding, bool objectValued) => BindCurve(binding, objectValued, null);

        bool m_HumanCurvesBound;

        /// <summary>Binds every humanoid curve of the root's Animator (body, goals, muscles,
        /// translation DoF) once.</summary>
        internal void BindHumanCurves()
        {
            if (m_HumanCurvesBound) return;
            m_HumanCurvesBound = true;
            var names = Unbound.Runtime.Humanoid.HumanClipImport.CurveNames;
            for (int c = Unbound.Runtime.Humanoid.HumanClipImport.RootFirst; c < names.Length; c++)
                BindChannel(CurveBinding.ForType("", names[c], typeof(Animator)), false);
        }

        List<int> m_QuaternionStarts = new List<int>();
        int m_QuaternionGroupsSeen;

        /// <summary>The first channels of the rotation groups (blended as quaternions).</summary>
        internal List<int> QuaternionStarts
        {
            get
            {
                for (; m_QuaternionGroupsSeen < Groups.Count; m_QuaternionGroupsSeen++)
                    if (Groups[m_QuaternionGroupsSeen].IsQuaternion) m_QuaternionStarts.Add(Groups[m_QuaternionGroupsSeen].First);
                return m_QuaternionStarts;
            }
        }

        int BindCurve(CurveBinding binding, bool objectValued, AnimationClip clip, bool create = true, BoundClip into = null)
        {
            try
            {
                var spec = PropertyBinder.Resolve(Root, binding, objectValued, clip);
                if (spec.Factory == null)
                {
                    // An Animator curve no controller here claims yet (see NowOwned).
                    if (spec.MissedAnimator != null && into != null)
                        (into.MissedParameters ??= new List<(Animator, string)>()).Add((spec.MissedAnimator, spec.MissedName));
                    return -1;
                }
                if (!m_Index.TryGetValue((spec.Target, spec.Key), out var group))
                {
                    if (!create) return -1;
                    group = spec.Factory();
                    if (group == null) return -1;
                    if (group is ParameterCurveGroup parameter) parameter.Bindings = this;
                    if (group is SkeletonGroup skeleton) Skeleton = skeleton.Skeleton;
                    else group.Transform ??= (spec.Target as Component)?.transform ?? (spec.Target as GameObject)?.transform;
                    Add(group);
                    m_Index[(spec.Target, spec.Key)] = group;
                }
                if (group.IsObject != objectValued) return -1;
                return objectValued ? group.First : group.First + Math.Min(spec.Component, group.Width - 1);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"Unbound: binding {binding} of clip '{clip?.name}' failed: {e.Message}");
                return -1;
            }
        }

        void Add(PropertyGroup group)
        {
            Groups.Add(group);
            if (group.IsObject)
            {
                group.First = ObjectCount++;
                Grow(ref ObjectDefaults, ObjectCount);
                Grow(ref ObjectOwner, ObjectCount);
                ObjectOwner[group.First] = group;
                ObjectDefaults[group.First] = group.ReadObject();
            }
            else
            {
                group.First = FloatCount;
                FloatCount += group.Width;
                Grow(ref Defaults, FloatCount);
                Grow(ref FloatOwner, FloatCount);
                for (int i = 0; i < group.Width; i++) FloatOwner[group.First + i] = group;
                group.Read(Defaults, group.First);
            }
        }

        static void Grow<T>(ref T[] array, int count)
        {
            if (array.Length >= count) return;
            Array.Resize(ref array, Math.Max(count, array.Length * 2));
        }

        /// <summary>Writes the default values of every bound property (Animator.WriteDefaultValues).</summary>
        internal void WriteDefaults()
        {
            foreach (var g in Groups)
            {
                if (!g.Alive) continue;
                try
                {
                    if (g.IsObject) g.WriteObject(ObjectDefaults[g.First]);
                    else g.Write(Defaults, g.First);
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                }
            }
            Skeleton?.Apply();
        }

        /// <summary>Whether a curve drives the Animator's own parameter (Animator float parameters
        /// animated by clips): as before, any curve of the name bound here while the Animator's own
        /// controller has that float. A channel bound only for a controller playable's parameter
        /// does not count.</summary>
        internal bool AnimatesParameter(Animator animator, string name)
        {
            foreach (var pair in m_Index)
                if (ReferenceEquals(pair.Key.Item1, animator) && pair.Key.Item2 == "param:" + name && pair.Value is ParameterCurveGroup { OwnDriven: true })
                    return true;
            return false;
        }

        /// <summary>Whether the clips of <paramref name="player"/>'s controller (after overrides),
        /// as bound here, feed <paramref name="channel"/>.</summary>
        internal bool Feeds(AnimatorPlayer player, int channel) => Feeds(player.Controller, player.Graph, channel);

        /// <summary>Whether the clips of <paramref name="controller"/> (after overrides), as bound
        /// here, feed <paramref name="channel"/>.</summary>
        internal bool Feeds(RuntimeAnimatorController controller, ControllerGraph graph, int channel)
        {
            if (controller == null || graph == null) return false;
            foreach (var original in graph.Clips)
            {
                var clip = controller.MapClip(original);
                if (clip == null || clip.m_Legacy || !m_Clips.TryGetValue(clip, out var bound)) continue;
                foreach (var ch in bound.Float)
                    if (ch == channel) return true;
            }
            return false;
        }

        /// <summary>The parameter channel of <paramref name="name"/> on <paramref name="animator"/>
        /// bound here, or null.</summary>
        internal ParameterCurveGroup ParameterChannel(Animator animator, string name) =>
            m_Index.TryGetValue((animator, "param:" + name), out var group) ? group as ParameterCurveGroup : null;

        /// <summary>Whether a curve of one of <paramref name="player"/>'s own clips drives its float
        /// parameter <paramref name="name"/> on <paramref name="animator"/> (a controller playable's
        /// IsParameterControlledByCurve). Curves of other playables' clips do not count.</summary>
        internal bool FeedsParameter(AnimatorPlayer player, Animator animator, string name) =>
            m_Index.TryGetValue((animator, "param:" + name), out var group) && group is ParameterCurveGroup parameter
            && parameter.FedIndex(player) >= 0;
    }

    /// <summary>A clip's curves mapped to channels of one AnimationBindings (-1: not bound).</summary>
    internal sealed class BoundClip
    {
        internal AnimationClip Clip;
        internal int Version;
        internal int[] Float;
        /// <summary>Animator curves (on the root's Animator) left unbound because no controller had
        /// a float of their name: see <see cref="AnimationBindings.Bind"/>. Null when none.</summary>
        internal List<(Animator Animator, string Name)> MissedParameters;
        /// <summary>Per curve that starts an Euler triple, the first of its rotation's four channels
        /// (-1 elsewhere); null when the clip has none.</summary>
        internal int[] EulerRotation;
        internal int[] Object;
        /// <summary>The additive reference pose (<see cref="AnimationBindings.AdditiveReference"/>).</summary>
        internal float[] Reference;
        internal int ReferenceFloatCount = -1;
        internal int ReferenceVersion;
        bool[] m_FloatMask;
        bool[] m_ObjectMask;
        int m_MaskFloatCount = -1;
        int m_MaskObjectCount = -1;

        /// <summary>Which channels the clip animates.</summary>
        internal bool[] FloatMask(int floatCount)
        {
            if (m_MaskFloatCount == floatCount) return m_FloatMask;
            m_FloatMask = new bool[floatCount];
            foreach (var ch in Float) if (ch >= 0) m_FloatMask[ch] = true;
            if (EulerRotation != null)
                foreach (var ch in EulerRotation) if (ch >= 0 && ch + 3 < floatCount) m_FloatMask[ch + 3] = true;
            m_MaskFloatCount = floatCount;
            return m_FloatMask;
        }

        internal bool[] ObjectMask(int objectCount)
        {
            if (m_MaskObjectCount == objectCount) return m_ObjectMask;
            m_ObjectMask = new bool[objectCount];
            foreach (var ch in Object) if (ch >= 0) m_ObjectMask[ch] = true;
            m_MaskObjectCount = objectCount;
            return m_ObjectMask;
        }
    }

    /// <summary>
    /// Maps a curve binding to the property it writes, with Unity's semantics per component type:
    /// Transform local position/rotation/Euler angles/scale, RectTransform rect fields, GameObject
    /// activity, enabled flags, sprite renderer color/sprite/flip, canvas group alpha, material
    /// properties of renderers, Animator parameters, and serialized fields of scripts (uGUI's Image,
    /// Text and TextMeshPro included) by reflection.
    /// </summary>
    internal static class PropertyBinder
    {
        internal struct Spec
        {
            internal Object Target;
            internal string Key;
            internal int Component;
            internal Func<PropertyGroup> Factory;
            /// <summary>Without a factory: the root's Animator and the curve name no controller
            /// claims as a float parameter yet (see <see cref="AnimationBindings.Bind"/>).</summary>
            internal Animator MissedAnimator;
            internal string MissedName;
        }

        static readonly HashSet<string> s_Warned = new HashSet<string>();

        internal static Spec Resolve(GameObject root, CurveBinding binding, bool objectValued, AnimationClip clip)
        {
            var spec = default(Spec);
            if (root == null) return spec;
            if (!objectValued && SkeletonNode(root, binding, out var skeleton, out int node))
            {
                var s = ResolveSkeleton(skeleton, node, binding.Attribute);
                if (s.Factory != null) return s;
            }
            if (binding.Hashed) return ResolveHashed(root, binding, objectValued, clip);
            var t = binding.Path.Length == 0 ? root.transform : root.transform.Find(binding.Path);
            if (t == null) return spec; // a missing path is not animated (Unity shows it as missing)
            return Resolve(root, t, binding, binding.Attribute, objectValued, clip);
        }

        /// <summary>A player clip's binding: the path by its hash below the root, then the
        /// attribute by its hash among the target's properties (Transforms' are named already).</summary>
        static Spec ResolveHashed(GameObject root, CurveBinding binding, bool objectValued, AnimationClip clip)
        {
            var t = HashedBindings.Find(root.transform, binding.PathHash);
            if (t == null) return default;
            var attribute = binding.Attribute;
            if (attribute.Length == 0)
            {
                attribute = HashedBindings.Attribute(t.m_GameObject, binding, out var component);
                if (attribute == null)
                {
                    Warn($"{clip?.name}: hashed {binding.ClassId}:{binding.AttributeHash}",
                        $"Unbound: animation clip '{clip?.name}' animates a property (class {binding.ClassId}, attribute hash {binding.AttributeHash}) that could not be identified; the curve is ignored.");
                    return default;
                }
                if (component != null)
                {
                    // The matching component's own type (a script found by its fields).
                    var named = CurveBinding.ForType("", attribute, component.GetType());
                    return Resolve(root, t, named, attribute, objectValued, clip);
                }
                return Resolve(root, t, CurveBinding.ForType("", attribute, typeof(GameObject)), attribute, objectValued, clip);
            }
            return Resolve(root, t, binding, attribute, objectValued, clip);
        }

        static Spec Resolve(GameObject root, Transform t, CurveBinding binding, string attribute, bool objectValued, AnimationClip clip)
        {
            var spec = default(Spec);
            Animator missedAnimator = null;
            string missedName = null;
            var go = t.m_GameObject;
            var type = binding.ResolveType();
            if (type == null)
            {
                Warn($"{clip?.name}: {binding}", binding.ClassId == EngineTypes.MonoBehaviourId
                    ? $"Unbound: animation clip '{clip?.name}' animates a script that could not be found (guid {binding.Script.Guid}); the curve '{binding.Attribute}' is ignored."
                    : $"Unbound: animation clip '{clip?.name}' animates class {binding.ClassId}, which is not supported yet; the curve '{binding.Attribute}' is ignored.");
                return spec;
            }

            if (type == typeof(GameObject))
            {
                if (attribute == "m_IsActive" && !objectValued)
                    return Make(go, "active", 0, () => new AccessorGroup(go, 1,
                        () => new Vector4(go.m_IsActive ? 1f : 0f, 0, 0, 0),
                        v => go.SetActive(v.x >= 0.5f)));
                Unsupported(clip, binding);
                return spec;
            }

            var component = go.GetComponent(type);
            if (component == null) return spec;

            if (objectValued) return ResolveObject(component, binding, clip);

            if (attribute == "m_Enabled")
                return ResolveEnabled(component, binding, clip);

            if (component is Transform transform)
            {
                var s = ResolveTransform(transform, attribute);
                if (s.Factory != null) return s;
            }
            if (attribute.StartsWith("material.", StringComparison.Ordinal) && component is Renderer renderer)
                return ResolveMaterial(renderer, attribute);
            switch (component)
            {
                case SpriteRenderer sr:
                {
                    if (TryComponent(attribute, "m_Color", out int c))
                        return Make(sr, "color", c, () => new AccessorGroup(sr, 4, () => (Vector4)sr.color, v => sr.color = v));
                    if (TryComponent(attribute, "m_Size", out c))
                        return Make(sr, "size", c, () => new AccessorGroup(sr, 2, () => sr.size, v => sr.size = v));
                    if (attribute == "m_FlipX")
                        return Make(sr, "flipX", 0, () => new AccessorGroup(sr, 1, () => new Vector4(sr.flipX ? 1 : 0, 0), v => sr.flipX = v.x >= 0.5f));
                    if (attribute == "m_FlipY")
                        return Make(sr, "flipY", 0, () => new AccessorGroup(sr, 1, () => new Vector4(sr.flipY ? 1 : 0, 0), v => sr.flipY = v.x >= 0.5f));
                    break;
                }
                case CanvasGroup cg:
                {
                    if (attribute == "m_Alpha")
                        return Make(cg, "alpha", 0, () => new AccessorGroup(cg, 1, () => new Vector4(cg.alpha, 0), v => cg.alpha = v.x));
                    if (attribute == "m_Interactable")
                        return Make(cg, "interactable", 0, () => new AccessorGroup(cg, 1, () => new Vector4(cg.interactable ? 1 : 0, 0), v => cg.interactable = v.x >= 0.5f));
                    if (attribute == "m_BlocksRaycasts")
                        return Make(cg, "blocksRaycasts", 0, () => new AccessorGroup(cg, 1, () => new Vector4(cg.blocksRaycasts ? 1 : 0, 0), v => cg.blocksRaycasts = v.x >= 0.5f));
                    break;
                }
                case Animator animator:
                {
                    var name = attribute;
                    // Muscle curves (humanoid clips) drive the Animator's human pose.
                    int human = Unbound.Runtime.Humanoid.HumanClipImport.CurveIndex(name);
                    if (human >= 0)
                    {
                        if (!animator.isHuman) break;
                        return Make(animator, "human:" + name, 0, () => new AccessorGroup(animator, 1,
                            () => new Vector4(Unbound.Runtime.Humanoid.AnimatorHuman.DefaultValue(human), 0),
                            v => animator.Human.Set(human, v.x))
                        {
                            AlwaysWrite = true,
                            HumanCurve = human,
                            HumanBody = human >= Unbound.Runtime.Humanoid.HumanClipImport.RootFirst && human < Unbound.Runtime.Humanoid.HumanClipImport.GoalFirst,
                        });
                    }
                    // Clip curves drive float parameters only; int and bool ones keep their
                    // values (measured, tests/projects/modelclips).
                    if (animator.HasFloatParameterNamed(name))
                        return Make(animator, "param:" + name, 0, () => new ParameterCurveGroup(animator, name, ownDriven: true));
                    // The root Animator's own controller has no such float: a clip a controller
                    // playable on it plays drives that playable's float of the name.
                    if (clip != null && ReferenceEquals(go, root))
                    {
                        if (PlayableParameters.Owned(animator, name, clip))
                            return Make(animator, "param:" + name, 0, () => new ParameterCurveGroup(animator, name, ownDriven: false) { MadeBy = clip });
                        missedAnimator = animator;
                        missedName = name;
                    }
                    break;
                }
            }
            if (component is SkinnedMeshRenderer skinned && attribute.StartsWith("blendShape.", StringComparison.Ordinal))
            {
                // A blend shape's weight, by the shape's name in the renderer's current mesh.
                var shape = attribute.Substring("blendShape.".Length);
                int Index() => skinned.sharedMesh != null ? skinned.sharedMesh.GetBlendShapeIndex(shape) : -1;
                return Make(skinned, "blendShape:" + shape, 0, () => new AccessorGroup(skinned, 1,
                    () => new Vector4(Index() is int i && i >= 0 ? skinned.GetBlendShapeWeight(i) : 0f, 0),
                    v =>
                    {
                        int i = Index();
                        if (i >= 0) skinned.SetBlendShapeWeight(i, v.x);
                    }));
            }
            if (component is Renderer r2 && attribute == "m_SortingOrder")
                return Make(r2, "sortingOrder", 0, () => new AccessorGroup(r2, 1, () => new Vector4(r2.sortingOrder, 0), v => r2.sortingOrder = (int)Math.Floor(v.x + 0.5f)));

            // Scripts (and engine objects whose fields carry the serialized names): by reflection.
            var chain = FieldChain(component.GetType(), attribute, component is MonoBehaviour);
            if (chain != null && !typeof(Object).IsAssignableFrom(chain[chain.Length - 1].FieldType))
            {
                var mb = component as MonoBehaviour;
                return Make(component, "field:" + attribute, 0, () => new FieldGroup(component, chain) { Script = mb });
            }
            Unsupported(clip, binding);
            spec.MissedAnimator = missedAnimator;
            spec.MissedName = missedName;
            return spec;
        }

        static Spec ResolveObject(Component component, CurveBinding binding, AnimationClip clip)
        {
            var attribute = binding.Attribute;
            if (component is SpriteRenderer sr && attribute == "m_Sprite")
                return Make(sr, "sprite", 0, () => new ObjectAccessorGroup(sr, () => sr.sprite, o => sr.sprite = o as Sprite));
            if (component is Renderer renderer && attribute.StartsWith("m_Materials.Array.data[", StringComparison.Ordinal))
            {
                int close = attribute.IndexOf(']');
                if (close > 0 && int.TryParse(attribute.AsSpan(23, close - 23), out int index))
                    return Make(renderer, "materials:" + index, 0, () => new ObjectAccessorGroup(renderer,
                        () => index < renderer.m_Materials.Length ? renderer.m_Materials[index] : null,
                        o =>
                        {
                            if (index >= renderer.m_Materials.Length) return;
                            var materials = renderer.sharedMaterials;
                            materials[index] = o as Material;
                            renderer.sharedMaterials = materials;
                        }));
            }
            var chain = FieldChain(component.GetType(), attribute, component is MonoBehaviour);
            if (chain != null && typeof(Object).IsAssignableFrom(chain[chain.Length - 1].FieldType))
            {
                var mb = component as MonoBehaviour;
                return Make(component, "field:" + attribute, 0, () => new FieldGroup(component, chain) { Script = mb });
            }
            Unsupported(clip, binding);
            return default;
        }

        static Spec ResolveEnabled(Component component, CurveBinding binding, AnimationClip clip)
        {
            switch (component)
            {
                case Behaviour b:
                    return Make(b, "enabled", 0, () => new AccessorGroup(b, 1, () => new Vector4(b.enabled ? 1 : 0, 0), v => b.enabled = v.x >= 0.5f));
                case Renderer r:
                    return Make(r, "enabled", 0, () => new AccessorGroup(r, 1, () => new Vector4(r.enabled ? 1 : 0, 0), v => r.enabled = v.x >= 0.5f));
                case Collider c:
                    return Make(c, "enabled", 0, () => new AccessorGroup(c, 1, () => new Vector4(c.enabled ? 1 : 0, 0), v => c.enabled = v.x >= 0.5f));
            }
            Unsupported(clip, binding);
            return default;
        }

        /// <summary>A transform curve's property and component.</summary>
        static bool TransformAttribute(string attribute, out TransformProperty property, out int component)
        {
            property = TransformProperty.Position;
            if (TryComponent(attribute, "m_LocalPosition", out component)) return true;
            property = TransformProperty.Rotation;
            if (TryComponent(attribute, "m_LocalRotation", out component)) return true;
            property = TransformProperty.Euler;
            if (TryComponent(attribute, "localEulerAnglesRaw", out component) || TryComponent(attribute, "localEulerAngles", out component) ||
                TryComponent(attribute, "localEulerAnglesBaked", out component) || TryComponent(attribute, "m_LocalEulerAnglesHint", out component))
                return true;
            property = TransformProperty.Scale;
            return TryComponent(attribute, "m_LocalScale", out component);
        }

        /// <summary>The binding key of a Transform's property.</summary>
        internal static string TransformKey(TransformProperty property) => property switch
        {
            TransformProperty.Position => "position",
            TransformProperty.Rotation => "rotation",
            TransformProperty.Euler => "euler",
            _ => "scale",
        };

        /// <summary>The property a Transform binding key names.</summary>
        internal static bool TransformPropertyOf(string key, out TransformProperty property)
        {
            switch (key)
            {
                case "position": property = TransformProperty.Position; return true;
                case "rotation": property = TransformProperty.Rotation; return true;
                case "euler": property = TransformProperty.Euler; return true;
                case "scale": property = TransformProperty.Scale; return true;
            }
            property = default;
            return false;
        }

        /// <summary>The binding key of a skeleton node's property (keyed on the Animator).</summary>
        internal static string SkeletonKey(int node, TransformProperty property) => "skeleton:" + node + ":" + TransformKey(property);

        internal static PropertyGroup TransformGroup(Transform t, TransformProperty property) => property switch
        {
            TransformProperty.Position => new AccessorGroup(t, 3, () => t.localPosition, v => t.localPosition = v),
            TransformProperty.Rotation => new AccessorGroup(t, 4,
                () => { var q = t.localRotation; return new Vector4(q.x, q.y, q.z, q.w); },
                v =>
                {
                    float m = Mathf.Sqrt(v.x * v.x + v.y * v.y + v.z * v.z + v.w * v.w);
                    if (m < 1e-6f) return;
                    t.localRotation = new Quaternion(v.x / m, v.y / m, v.z / m, v.w / m);
                }) { IsQuaternion = true },
            TransformProperty.Euler => new AccessorGroup(t, 3, () => t.localEulerAngles, v => t.localRotation = Quaternion.Euler(v.x, v.y, v.z)),
            _ => new AccessorGroup(t, 3, () => t.localScale, v => t.localScale = v) { IsScale = true },
        };

        /// <summary>A Transform curve of a path the root's Animator animates as a skeleton node
        /// (an Animator without its transform hierarchy binds every such curve to its skeleton).</summary>
        static bool SkeletonNode(GameObject root, CurveBinding binding, out AnimatorSkeleton skeleton, out int node)
        {
            skeleton = null;
            node = -1;
            if (binding.Hashed ? binding.PathHash == 0 : binding.Path.Length == 0) return false;
            if (!(binding.Type != null ? typeof(Transform).IsAssignableFrom(binding.Type) : binding.ClassId == EngineTypes.TransformId)) return false;
            var animator = root.GetComponent<Animator>();
            if (animator == null || !animator.m_NoHierarchy) return false;
            skeleton = animator.Skeleton;
            if (skeleton == null) return false;
            node = skeleton.NodeOfPath(binding.Hashed ? binding.PathHash : HashedBindings.Crc32(binding.Path));
            return node > 0;
        }

        static Spec ResolveSkeleton(AnimatorSkeleton skeleton, int node, string attribute)
        {
            if (!TransformAttribute(attribute, out var property, out int c)) return default;
            return Make(skeleton.Animator, SkeletonKey(node, property), c, () => new SkeletonGroup(skeleton, node, property));
        }

        static Spec ResolveTransform(Transform t, string attribute)
        {
            if (TransformAttribute(attribute, out var property, out int c))
                return Make(t, TransformKey(property), c, () => TransformGroup(t, property));
            if (t is RectTransform rt)
            {
                if (TryComponent(attribute, "m_AnchoredPosition", out c))
                    return Make(rt, "anchoredPosition", c, () => new AccessorGroup(rt, 2, () => rt.anchoredPosition, v => rt.anchoredPosition = v));
                if (TryComponent(attribute, "m_SizeDelta", out c))
                    return Make(rt, "sizeDelta", c, () => new AccessorGroup(rt, 2, () => rt.sizeDelta, v => rt.sizeDelta = v));
                if (TryComponent(attribute, "m_AnchorMin", out c))
                    return Make(rt, "anchorMin", c, () => new AccessorGroup(rt, 2, () => rt.anchorMin, v => rt.anchorMin = v));
                if (TryComponent(attribute, "m_AnchorMax", out c))
                    return Make(rt, "anchorMax", c, () => new AccessorGroup(rt, 2, () => rt.anchorMax, v => rt.anchorMax = v));
                if (TryComponent(attribute, "m_Pivot", out c))
                    return Make(rt, "pivot", c, () => new AccessorGroup(rt, 2, () => rt.pivot, v => rt.pivot = v));
            }
            return default;
        }

        /// <summary>`material._Color.r`, `material._Offset.x`, `material._Glossiness`: a material
        /// property of the renderer. Written to the renderer's own material instance (Unity uses a
        /// property block; Unbound's renderer draws its material, so the instance is what shows).</summary>
        static MaterialPropertyBlock Block(Renderer renderer)
        {
            var block = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(block);
            return block;
        }

        static Spec ResolveMaterial(Renderer renderer, string attribute)
        {
            var rest = attribute.Substring("material.".Length);
            int c = -1;
            int dot = rest.LastIndexOf('.');
            if (dot > 0 && dot == rest.Length - 2 && ComponentIndex(rest[rest.Length - 1], out int component, out _))
            {
                rest = rest.Substring(0, dot);
                c = component;
            }
            var property = rest;
            int id = Shader.PropertyToID(property);
            // Each channel on its own, into the renderer's property block, as Unity animates
            // materials: a channel's default is the material's value, the block gets only the
            // channels curves drive (the others read as the block has them: 0 when unset; measured).
            Func<PropertyGroup> factory;
            if (c < 0)
            {
                factory = () => new AccessorGroup(renderer, 1,
                    () => new Vector4(renderer.sharedMaterial != null && renderer.sharedMaterial.HasProperty(id) ? renderer.sharedMaterial.GetFloat(id) : 0f, 0),
                    v =>
                    {
                        var block = Block(renderer);
                        block.SetFloat(id, v.x);
                        renderer.SetPropertyBlock(block);
                    }) { AlwaysWrite = true };
                return Make(renderer, "material:" + property, 0, factory);
            }
            int channel = c;
            factory = () => new AccessorGroup(renderer, 1,
                () => new Vector4(renderer.sharedMaterial != null && renderer.sharedMaterial.HasProperty(id) ? renderer.sharedMaterial.GetVector(id)[channel] : 0f, 0),
                v =>
                {
                    var block = Block(renderer);
                    var value = block.GetVector(id);
                    value[channel] = v.x;
                    block.SetVector(id, value);
                    renderer.SetPropertyBlock(block);
                }) { AlwaysWrite = true };
            return Make(renderer, "material:" + property + "." + channel, 0, factory);
        }

        static Spec Make(Object target, string key, int component, Func<PropertyGroup> factory) =>
            new Spec { Target = target, Key = key, Component = component, Factory = factory };

        /// <summary>`m_Color.a` against `m_Color` gives component 3.</summary>
        static bool TryComponent(string attribute, string name, out int component)
        {
            component = 0;
            if (attribute.Length != name.Length + 2 || !attribute.StartsWith(name, StringComparison.Ordinal) || attribute[name.Length] != '.')
                return false;
            return ComponentIndex(attribute[name.Length + 1], out component, out _);
        }

        static bool ComponentIndex(char c, out int index, out bool color)
        {
            color = c == 'r' || c == 'g' || c == 'b' || c == 'a';
            switch (c)
            {
                case 'x': case 'r': index = 0; return true;
                case 'y': case 'g': index = 1; return true;
                case 'z': case 'b': index = 2; return true;
                case 'w': case 'a': index = 3; return true;
            }
            index = 0;
            return false;
        }

        /// <summary>The fields a property path walks: serialized fields of scripts, then nested
        /// fields of their structs and classes, down to a number, bool, enum or object reference.</summary>
        internal static FieldInfo[] FieldChain(Type type, string path, bool script)
        {
            if (path.Contains("Array.", StringComparison.Ordinal)) return null;
            var parts = path.Split('.');
            var chain = new FieldInfo[parts.Length];
            var current = type;
            for (int i = 0; i < parts.Length; i++)
            {
                var field = FindField(current, parts[i], script || i > 0);
                if (field == null) return null;
                chain[i] = field;
                current = field.FieldType;
            }
            return FieldGroup.IsAnimatable(current) ? chain : null;
        }

        static FieldInfo FindField(Type type, string name, bool serialized)
        {
            if (serialized && !type.IsPrimitive)
            {
                foreach (var member in SerializationRules.Members(type))
                    if (member.Name == name) return member.Field;
            }
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
            for (var t = type; t != null && t != typeof(object); t = t.BaseType)
            {
                var f = t.GetField(name, flags);
                if (f != null && !f.IsInitOnly && !f.IsLiteral) return f;
            }
            return null;
        }

        static void Unsupported(AnimationClip clip, CurveBinding binding) =>
            Warn($"{binding.Type?.FullName}.{binding.Attribute}",
                $"Unbound: animating {binding.Type?.Name ?? $"class {binding.ClassId}"}.{binding.Attribute} is not supported yet (clip '{clip?.name}'); the curve is ignored.");

        static void Warn(string key, string message)
        {
            lock (s_Warned)
            {
                if (!s_Warned.Add(key)) return;
            }
            Debug.LogWarning(message);
        }
    }
}
