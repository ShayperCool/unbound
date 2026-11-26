using System;
using Unbound.Runtime.Playables;
using UnityEngine.Playables;

namespace UnityEngine.Experimental.Playables
{
    // The experimental texture playables, as graph nodes and an output (measured:
    // tests/projects/texplayables): what each holds, its inputs and outputs, the casts between them,
    // and the texture output's target and binding. What evaluating them draws into the output's
    // render texture (the Editor draws: the target's pixels change) is not implemented and says so.

    sealed class CameraPlayableKind : PlayableKind
    {
        public Camera Camera;
    }

    sealed class MaterialEffectKind : PlayableKind
    {
        public Material Material;
        public int Pass;
    }

    sealed class TextureMixerKind : PlayableKind
    {
    }

    sealed class TextureOutputState
    {
        public RenderTexture Target;
    }

    static class TexturePlayableGraph
    {
        /// <summary>The graph's data; an invalid graph fails as the Editor's (measured for the
        /// output and each playable).</summary>
        internal static PlayableGraphData Of(PlayableGraph graph) =>
            PlayableGraphData.Get(graph) ?? throw new NullReferenceException("The PlayableGraph is null.");

        internal static PlayableNode Node(PlayableGraph graph, Type type, PlayableKind kind, int inputs)
        {
            var g = Of(graph);
            var n = g.CreateNode(type);
            n.Kind = kind;
            if (inputs > 0) g.SetInputCount(n, inputs);
            return n;
        }
    }

    /// <summary>A playable that renders a camera into the texture output it feeds.</summary>
    public partial struct CameraPlayable : IPlayable, IEquatable<CameraPlayable>
    {
        PlayableHandle m_Handle;

        public static CameraPlayable Create(PlayableGraph graph, Camera camera) =>
            new CameraPlayable(TexturePlayableGraph.Node(graph, typeof(CameraPlayable), new CameraPlayableKind { Camera = camera }, 0).ToHandle());

        internal CameraPlayable(PlayableHandle handle)
        {
            if (handle.IsValid() && !handle.IsPlayableOfType<CameraPlayable>())
                throw new InvalidCastException("Can't set handle: the playable is not an CameraPlayable.");
            m_Handle = handle;
        }

        public PlayableHandle GetHandle() => m_Handle;

        public static implicit operator Playable(CameraPlayable playable) => new Playable(playable.GetHandle());

        public static explicit operator CameraPlayable(Playable playable) => new CameraPlayable(playable.GetHandle());

        public bool Equals(CameraPlayable other) => GetHandle() == other.GetHandle();

        CameraPlayableKind Kind => PlayableGraphData.Get(m_Handle)?.Kind as CameraPlayableKind ?? throw new InvalidOperationException("The CameraPlayable is invalid.");

        public Camera GetCamera() => Kind.Camera;

        public void SetCamera(Camera value) => Kind.Camera = value;
    }

    /// <summary>A playable that draws its input textures through a material's pass.</summary>
    public partial struct MaterialEffectPlayable : IPlayable, IEquatable<MaterialEffectPlayable>
    {
        PlayableHandle m_Handle;

        public static MaterialEffectPlayable Create(PlayableGraph graph, Material material, int pass = -1) =>
            new MaterialEffectPlayable(TexturePlayableGraph.Node(graph, typeof(MaterialEffectPlayable), new MaterialEffectKind { Material = material, Pass = pass }, 0).ToHandle());

        internal MaterialEffectPlayable(PlayableHandle handle)
        {
            if (handle.IsValid() && !handle.IsPlayableOfType<MaterialEffectPlayable>())
                throw new InvalidCastException("Can't set handle: the playable is not an MaterialEffectPlayable.");
            m_Handle = handle;
        }

        public PlayableHandle GetHandle() => m_Handle;

        public static implicit operator Playable(MaterialEffectPlayable playable) => new Playable(playable.GetHandle());

        public static explicit operator MaterialEffectPlayable(Playable playable) => new MaterialEffectPlayable(playable.GetHandle());

        public bool Equals(MaterialEffectPlayable other) => GetHandle() == other.GetHandle();

        MaterialEffectKind Kind => PlayableGraphData.Get(m_Handle)?.Kind as MaterialEffectKind ?? throw new InvalidOperationException("The MaterialEffectPlayable is invalid.");

        public Material GetMaterial() => Kind.Material;

        public void SetMaterial(Material value) => Kind.Material = value;

        public int GetPass() => Kind.Pass;

        public void SetPass(int value) => Kind.Pass = value;
    }

    /// <summary>A playable that mixes its input textures; it is made with two inputs.</summary>
    public partial struct TextureMixerPlayable : IPlayable, IEquatable<TextureMixerPlayable>
    {
        PlayableHandle m_Handle;

        public static TextureMixerPlayable Create(PlayableGraph graph) =>
            new TextureMixerPlayable(TexturePlayableGraph.Node(graph, typeof(TextureMixerPlayable), new TextureMixerKind(), 2).ToHandle());

        internal TextureMixerPlayable(PlayableHandle handle)
        {
            if (handle.IsValid() && !handle.IsPlayableOfType<TextureMixerPlayable>())
                throw new InvalidCastException("Can't set handle: the playable is not an TextureMixerPlayable.");
            m_Handle = handle;
        }

        public PlayableHandle GetHandle() => m_Handle;

        public static implicit operator Playable(TextureMixerPlayable playable) => new Playable(playable.GetHandle());

        public static explicit operator TextureMixerPlayable(Playable playable) => new TextureMixerPlayable(playable.GetHandle());

        public bool Equals(TextureMixerPlayable other) => GetHandle() == other.GetHandle();
    }

    public static partial class TexturePlayableBinding
    {
        public static PlayableBinding Create(string name, Object key) =>
            PlayableBinding.CreateInternal(name, key, typeof(RenderTexture), CreateTextureOutput);

        static PlayableOutput CreateTextureOutput(PlayableGraph graph, string name) => TexturePlayableOutput.Create(graph, name, null);
    }

    internal static partial class TexturePlayableGraphExtensions
    {
        internal static bool InternalCreateTextureOutput(ref PlayableGraph graph, string name, out PlayableOutputHandle handle)
        {
            var o = TexturePlayableGraph.Of(graph).CreateOutput(typeof(TexturePlayableOutput), name);
            o.Engine = new TextureOutputState();
            TextureOutputs.EnsureRegistered();
            handle = o.ToHandle();
            return true;
        }
    }

    /// <summary>The texture outputs' processing: their notifications as any output's; drawing the
    /// source into the target is reported as not implemented.</summary>
    sealed class TextureOutputs : OutputSystems.IProcessor
    {
        static readonly TextureOutputs s_Instance = new TextureOutputs();
        static bool s_Registered;

        internal static void EnsureRegistered()
        {
            if (s_Registered) return;
            s_Registered = true;
            OutputSystems.Register(typeof(TexturePlayableOutput), s_Instance);
        }

        public void Process(PlayableOutputData output, double dt, bool seek)
        {
            if (output.Source != null && (output.Engine as TextureOutputState)?.Target != null)
                Unbound.Runtime.Stubs.NotImplemented("UnityEngine.Experimental.Playables.TexturePlayableOutput: drawing a graph into its render texture");
            output.Graph.DeliverNotifications(output);
        }

        public void Stopped(PlayableGraphData graph) { }

        public void Flush() { }
    }

    /// <summary>An output drawing its source into a render texture. The drawing is not implemented
    /// (reported once when a graph evaluates one).</summary>
    public partial struct TexturePlayableOutput : IPlayableOutput
    {
        PlayableOutputHandle m_Handle;

        public static TexturePlayableOutput Null => new TexturePlayableOutput(PlayableOutputHandle.Null);

        public static TexturePlayableOutput Create(PlayableGraph graph, string name, RenderTexture target)
        {
            if (!TexturePlayableGraphExtensions.InternalCreateTextureOutput(ref graph, name, out var handle)) return Null;
            var output = new TexturePlayableOutput(handle);
            output.SetTarget(target);
            return output;
        }

        internal TexturePlayableOutput(PlayableOutputHandle handle)
        {
            if (handle.IsValid() && !handle.IsPlayableOutputOfType<TexturePlayableOutput>())
                throw new InvalidCastException("Can't set handle: the playable is not an TexturePlayableOutput.");
            m_Handle = handle;
        }

        public PlayableOutputHandle GetHandle() => m_Handle;

        public static implicit operator PlayableOutput(TexturePlayableOutput output) => new PlayableOutput(output.GetHandle());

        public static explicit operator TexturePlayableOutput(PlayableOutput output) => new TexturePlayableOutput(output.GetHandle());

        TextureOutputState State
        {
            get
            {
                var o = PlayableGraphData.Get(m_Handle) ?? throw new InvalidOperationException("The PlayableOutput is invalid.");
                return o.Engine as TextureOutputState ?? (TextureOutputState)(o.Engine = new TextureOutputState());
            }
        }

        public RenderTexture GetTarget() => State.Target;

        public void SetTarget(RenderTexture value) => State.Target = value;
    }
}
