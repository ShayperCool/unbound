using System;
using System.Collections.Generic;
using UnityEditor.VFX;
using UnityEngine;
using UnityEngine.VFX;

namespace Unbound.Runtime
{
    /// <summary>
    /// The object values of a task (the textures and graphics buffers its program declares) as
    /// they were last handed to what runs the program: tells which one changed since, so that a
    /// kernel or an output binds a value again only then. A destroyed object counts as none.
    /// </summary>
    internal sealed class VFXTaskObjects
    {
        internal readonly (int Name, int Expression)[] Slots;
        readonly object[] m_Bound;

        internal VFXTaskObjects(VisualEffectCompiledData data, in VFXEditorTaskDesc task)
        {
            Slots = VFXParticleSystem.ObjectValues(data, task);
            m_Bound = new object[Slots.Length];
        }

        /// <summary>Whether the value of <paramref name="slot"/> is another object than the
        /// one last answered (none at first), and that value.</summary>
        internal bool Changed(VisualEffectValues values, int slot, out object value)
        {
            value = values.GetObject(Slots[slot].Expression);
            if (value is UnityEngine.Object engineObject && !engineObject) value = null;
            if (ReferenceEquals(value, m_Bound[slot])) return false;
            m_Bound[slot] = value;
            return true;
        }
    }

    /// <summary>
    /// An output of a particle system: one draw of its particles with the shader the graph
    /// compiled to, whose vertex program finds each particle in the system's buffers by its
    /// vertex id. Drawn here: quads the output draws itself, in the order the system keeps
    /// them (no sorting, no culling per camera, no indirect buffer). An output of another kind
    /// is not drawn and says so once (<see cref="Refusal"/>): the system is simulated all the
    /// same. The draw matches the Unity 6000.0.71f1 Editor (tests/projects/vfx, `draw-*`).
    /// </summary>
    internal sealed class VFXOutput
    {
        /// <summary>The quads one draw's 16-bit indices reach (four vertices each), and the
        /// quads of each instance of a draw of more: the generated vertex programs find a
        /// quad's particle as <c>(vertexID >> 2) + instanceID * 2048</c>.</summary>
        internal const int MaxQuadsPerDraw = 16384, QuadsPerInstance = 2048;
        const int IndicesPerQuad = 6;

        static readonly int s_InstancingConstants = Shader.PropertyToID("instancingConstants");
        static readonly int s_InstanceIndex = Shader.PropertyToID("_InstanceIndex");
        static readonly int s_InstanceActiveIndex = Shader.PropertyToID("_InstanceActiveIndex");

        static GraphicsBuffer s_QuadIndices;

        readonly VFXParticleSystem m_System;
        readonly VFXEditorTaskDesc m_Task;
        readonly Material m_Material;
        readonly MaterialPropertyBlock m_Block = new MaterialPropertyBlock();
        readonly int[] m_BufferNames;
        readonly GraphicsBuffer[] m_Bound;
        readonly VFXTaskObjects m_Objects;

        internal VFXOutput(VFXParticleSystem system, in VFXEditorTaskDesc task, Material material)
        {
            m_System = system;
            m_Task = task;
            m_Material = material;
            int count = task.buffers?.Length ?? 0;
            m_BufferNames = new int[count];
            m_Bound = new GraphicsBuffer[count];
            for (int i = 0; i < count; i++) m_BufferNames[i] = Shader.PropertyToID(task.buffers[i].name);
            m_Objects = new VFXTaskObjects(system.Effect.Data, task);
            // One instance, active, in a batch of one: the counts as the programs read them
            // (the bits of unsigned integers), and instance zero's place in the buffers.
            float one = BitConverter.Int32BitsToSingle(1);
            m_Block.SetVector(s_InstancingConstants, new Vector4(one, one, one, 0f));
            m_Block.SetFloat(s_InstanceIndex, 0f);
            m_Block.SetFloat(s_InstanceActiveIndex, 0f);
        }

        /// <summary>Whether a task draws particles (an output of any primitive).</summary>
        internal static bool IsOutput(VFXTaskType type) =>
            type >= VFXTaskType.ParticlePointOutput && type <= VFXTaskType.ParticleOctagonOutput;

        /// <summary>Why an output task is not drawn, null when it is.
        /// <paramref name="sourceRefusal"/> is what its generated shader was found to need
        /// (<see cref="SourceRefusal"/>).</summary>
        internal static string Refusal(in VFXEditorSystemDesc system, in VFXEditorTaskDesc task, UnityEngine.Object processor, string sourceRefusal = null)
        {
            if (task.type != VFXTaskType.ParticleQuadOutput) return $"{task.type} is not drawn yet (quads are)";
            if ((system.flags & VFXSystemFlag.SystemHasStrips) != 0) return "strips are not drawn yet";
            if (Parameter(task, "indirectDraw") != 0)
                return "it draws through an indirect buffer (sorted, culled per camera or asked to), which is not done yet";
            if (task.usesMaterialVariant) return "outputs of a Shader Graph are not drawn yet";
            // An output with motion vectors keeps each particle's last frame in buffers of its own.
            if (task.temporaryBuffers != null && task.temporaryBuffers.Length != 0) return "it keeps buffers of the frame before (motion vectors), which is not done yet";
            if (sourceRefusal != null) return sourceRefusal;
            if (!(processor is Material)) return "its shader was not imported";
            return null;
        }

        /// <summary>The passes of a generated output shader that are drawn as measured, by
        /// their LightMode: the unlit forward pass, the depth passes of an opaque output and
        /// the shadow pass (shadows themselves are left out, and said so); the selection and
        /// picking passes are the Editor's own and the 2D renderer's pass is never asked for
        /// here.</summary>
        static readonly string[] s_DrawnPasses =
        {
            "UniversalForwardOnly", "DepthOnly", "DepthNormalsOnly", "ShadowCaster", "SceneSelectionPass", "Picking", "Universal2D",
        };

        /// <summary>The defines the graph compiler writes into an output's shader for what
        /// the draw would need besides the system's buffers: the pipeline's lighting, and the
        /// camera's depth for particles that fade against it.</summary>
        const string LitDefine = "#define URP_LIT 1", SoftParticleDefine = "#define USE_SOFT_PARTICLE 1";

        /// <summary>Per system and task of a compiled graph: what each output's generated
        /// shader does that is not drawn yet (null for a task that is no output, has no shader
        /// of the graph's, or only does what is drawn).</summary>
        internal static string[][] SourceRefusals(VFXEditorSystemDesc[] systems, VFXShaderSourceDesc[] sources)
        {
            var refusals = new string[systems.Length][];
            for (int s = 0; s < systems.Length; s++)
            {
                var tasks = systems[s].tasks ?? Array.Empty<VFXEditorTaskDesc>();
                refusals[s] = new string[tasks.Length];
                for (int t = 0; t < tasks.Length; t++)
                {
                    int index = tasks[t].shaderSourceIndex;
                    if (index >= 0 && index < sources.Length && !sources[index].compute && IsOutput(tasks[t].type))
                        refusals[s][t] = SourceRefusal(sources[index].source);
                }
            }
            return refusals;
        }

        /// <summary>What the generated shader of an output does that is not drawn yet, read
        /// off its source; null when it does only what the probe measured
        /// (tests/projects/vfx, `draw-*`).</summary>
        internal static string SourceRefusal(string source)
        {
            if (string.IsNullOrEmpty(source)) return null;
            if (source.Contains(LitDefine, StringComparison.Ordinal)) return "it is lit, and lit outputs are not drawn yet";
            if (source.Contains(SoftParticleDefine, StringComparison.Ordinal)) return "it fades against the camera's depth (soft particles), which is not done yet";
            const string Tag = "\"LightMode\"";
            for (int at = source.IndexOf(Tag, StringComparison.Ordinal); at >= 0; at = source.IndexOf(Tag, at + Tag.Length, StringComparison.Ordinal))
            {
                int open = source.IndexOf('"', at + Tag.Length);
                int close = open < 0 ? -1 : source.IndexOf('"', open + 1);
                if (close < 0) break;
                string mode = source.Substring(open + 1, close - open - 1);
                if (Array.IndexOf(s_DrawnPasses, mode) < 0) return $"its pass '{mode}' is not drawn yet";
            }
            return null;
        }

        static int Parameter(in VFXEditorTaskDesc task, string name)
        {
            foreach (var parameter in task.parameters ?? Array.Empty<VFXMapping>())
                if (parameter.name == name) return parameter.index;
            return 0;
        }

        /// <summary>The indices of <see cref="MaxQuadsPerDraw"/> quads: two triangles over each
        /// four vertices.</summary>
        static GraphicsBuffer QuadIndices()
        {
            if (s_QuadIndices != null && s_QuadIndices.IsValid()) return s_QuadIndices;
            var indices = new ushort[MaxQuadsPerDraw * IndicesPerQuad];
            for (int quad = 0, at = 0; quad < MaxQuadsPerDraw; quad++)
            {
                int first = quad * 4;
                indices[at++] = (ushort)first;
                indices[at++] = (ushort)(first + 2);
                indices[at++] = (ushort)(first + 1);
                indices[at++] = (ushort)(first + 1);
                indices[at++] = (ushort)(first + 2);
                indices[at++] = (ushort)(first + 3);
            }
            s_QuadIndices = new GraphicsBuffer(GraphicsBuffer.Target.Index, indices.Length, sizeof(ushort));
            s_QuadIndices.SetData(indices);
            return s_QuadIndices;
        }

        /// <summary>The draw of <paramref name="quads"/> quads: all in one while the indices
        /// reach, else instances of <see cref="QuadsPerInstance"/>.</summary>
        internal static DrawSetup QuadDraw(GraphicsBuffer indices, uint quads) => quads <= MaxQuadsPerDraw
            ? IndexedDraws.Direct(indices, 0, (int)quads * IndicesPerQuad, 1)
            : IndexedDraws.Direct(indices, 0, QuadsPerInstance * IndicesPerQuad, (int)((quads + QuadsPerInstance - 1) / QuadsPerInstance));

        /// <summary>Adds the output's draw for a camera that sees the effect's renderer.</summary>
        internal void Collect(Renderer renderer, FrameBuilder.CameraContext camera, List<FrameBuilder.DrawItem> into)
        {
            uint quads = m_System.DrawnParticles;
            if (quads == 0 || !m_Material) return;
            var effect = m_System.Effect;
            for (int i = 0; i < m_BufferNames.Length; i++)
            {
                int index = m_Task.buffers[i].index;
                if (index < 0) continue;
                var buffer = effect.GpuBuffer(index, 0);
                if (ReferenceEquals(buffer, m_Bound[i])) continue;
                m_Bound[i] = buffer;
                m_Block.SetBuffer(m_BufferNames[i], buffer);
            }
            var values = effect.Values;
            for (int i = 0; i < m_Objects.Slots.Length; i++)
            {
                if (!m_Objects.Changed(values, i, out object value)) continue;
                int name = m_Objects.Slots[i].Name;
                if (value is Texture texture) m_Block.SetTexture(name, texture);
                else if (value is GraphicsBuffer graphicsBuffer) m_Block.SetBuffer(name, graphicsBuffer);
                // A property without an object leaves the program what it has without one.
                else m_Block.m_Values.Remove(name);
            }
            FrameBuilder.AddProceduralDraw(renderer, m_Material, renderer.transform.localToWorldMatrix, QuadDraw(QuadIndices(), quads), m_Block.m_Values, camera, into);
        }
    }
}
