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
    /// vertex id (or, of a mesh, by its instance). Drawn here: quads, unsorted, either all the
    /// system has room for (the vertex program leaves out the dead ones) or through an indirect
    /// buffer: a list of the particles to draw that the system's kernels make (the update
    /// kernel, an output-update kernel at each step, or one for each camera that sees the
    /// effect), whose count gives the draw's arguments (<see cref="VFXIndirectArgs"/>); and
    /// meshes the output draws itself. Either unlit or lit by the pipeline. An output of another
    /// kind is not drawn and says so once (<see cref="Refusal"/>): the system is simulated all
    /// the same. The draws match the Unity 6000.0.71f1 Editor (tests/projects/vfx `draw-*`,
    /// tests/projects/vfxoutputs, tests/projects/vfxlit).
    /// </summary>
    internal sealed partial class VFXOutput
    {
        /// <summary>The quads one draw's 16-bit indices reach (four vertices each), and the
        /// quads of each instance of a draw of more: the generated vertex programs find a
        /// quad's particle as <c>(vertexID >> 2) + instanceID * 2048</c>.</summary>
        internal const int MaxQuadsPerDraw = 16384, QuadsPerInstance = 2048;
        const int IndicesPerQuad = 6;

        static readonly int s_InstancingConstants = Shader.PropertyToID("instancingConstants");
        static readonly int s_InstanceIndex = Shader.PropertyToID("_InstanceIndex");
        static readonly int s_InstanceActiveIndex = Shader.PropertyToID("_InstanceActiveIndex");
        /// <summary>The texture the generated programs sample curves and gradients from.</summary>
        internal const string BakedTextureName = "bakedTexture";
        static readonly int s_BakedTexture = Shader.PropertyToID(BakedTextureName);

        static GraphicsBuffer s_QuadIndices;

        readonly VFXParticleSystem m_System;
        readonly VFXEditorTaskDesc m_Task;
        readonly Material m_Material;
        readonly MaterialPropertyBlock m_Block = new MaterialPropertyBlock();
        readonly int[] m_BufferNames;
        readonly GraphicsBuffer[] m_Bound;
        readonly VFXTaskObjects m_Objects;
        Texture2D m_Baked;
        /// <summary>Of an output drawn through an indirect buffer: the buffer whose first
        /// element counts the particles to draw (of key and index pairs when sorted), and its
        /// entry of arguments; -1 for one drawn directly.</summary>
        readonly int m_CountBuffer = -1, m_ArgsEntry = -1;
        readonly bool m_CountPairs;
        int m_FilledGeneration = -1;

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
            m_MeshExpression = Parameter(task, MeshParameter, -1);
            m_SubMeshMaskExpression = Parameter(task, SubMeshMaskParameter, -1);
            // One instance, active, in a batch of one: the counts as the programs read them
            // (the bits of unsigned integers), and instance zero's place in the buffers.
            float one = BitConverter.Int32BitsToSingle(1);
            m_Block.SetVector(s_InstancingConstants, new Vector4(one, one, one, 0f));
            m_Block.SetFloat(s_InstanceIndex, 0f);
            m_Block.SetFloat(s_InstanceActiveIndex, 0f);
            if (Parameter(task, IndirectDrawName) != 0)
            {
                var data = system.Effect.Data;
                m_CountBuffer = Parameter(task, "indirectIndex");
                m_CountPairs = m_CountBuffer < data.GpuBuffers.Length && data.GpuBuffers[m_CountBuffer].stride == VFXIndirectArgs.KeyIndexPairBytes;
                m_ArgsEntry = VFXIndirectArgs.Shared.Reserve(ArgumentsKind(task.type));
            }
        }

        const string IndirectDrawName = "indirectDraw";

        /// <summary>Whether the output draws through an indirect buffer.</summary>
        internal bool Indirect => m_ArgsEntry >= 0;

        /// <summary>The buffer whose list an indirect output draws (-1 for a direct one).</summary>
        internal int CountBuffer => m_CountBuffer;

        /// <summary>The kind of indirect arguments of an output task's primitive.</summary>
        internal static VFXIndirectArgs.Kind ArgumentsKind(VFXTaskType type) => (VFXIndirectArgs.Kind)(type - VFXTaskType.ParticlePointOutput);

        internal void Release() => VFXIndirectArgs.Shared.Release(m_ArgsEntry);

        /// <summary>Whether a task draws particles (an output of any primitive).</summary>
        internal static bool IsOutput(VFXTaskType type) =>
            type >= VFXTaskType.ParticlePointOutput && type <= VFXTaskType.ParticleOctagonOutput;

        /// <summary>Why an output task is not drawn, null when it is.
        /// <paramref name="sourceRefusal"/> is what its generated shader was found to need
        /// (<see cref="SourceRefusal"/>).</summary>
        internal static string Refusal(in VFXEditorSystemDesc system, in VFXEditorTaskDesc task, UnityEngine.Object processor, string sourceRefusal = null)
        {
            if (task.type != VFXTaskType.ParticleQuadOutput && task.type != VFXTaskType.ParticleMeshOutput) return $"{task.type} is not drawn yet (quads and meshes are)";
            if ((system.flags & VFXSystemFlag.SystemHasStrips) != 0) return "strips are not drawn yet";
            if (Parameter(task, IndirectDrawName) != 0 && Sorted(system, task)) return "its particles are sorted, which is not done yet";
            // Meshes are drawn for every particle the system has room for: not from a list.
            if (Parameter(task, IndirectDrawName) != 0 && task.type == VFXTaskType.ParticleMeshOutput)
                return "it draws meshes through an indirect buffer, which is not done yet";
            if (task.usesMaterialVariant) return "outputs of a Shader Graph are not drawn yet";
            // An output with motion vectors keeps each particle's last frame in buffers of its own.
            if (task.temporaryBuffers != null && task.temporaryBuffers.Length != 0) return "it keeps buffers of the frame before (motion vectors), which is not done yet";
            if (sourceRefusal != null) return sourceRefusal;
            if (!(processor is Material)) return "its shader was not imported";
            if (task.type == VFXTaskType.ParticleMeshOutput) return MeshRefusal(task);
            return null;
        }

        /// <summary>The passes of a generated output shader that the pipeline may draw, by
        /// their LightMode. Measured: the forward pass of an unlit and of a lit output, an
        /// opaque lit output's deferred pass and the shadow pass (tests/projects/vfx,
        /// tests/projects/vfxlit). Admitted with them, as the same vertex program writing
        /// depth alone, and not measured: the depth passes of an opaque output (a depth
        /// prepass, the depth and normals textures). The selection and picking passes are the
        /// Editor's own and the 2D renderer's pass is never asked for here.</summary>
        static readonly string[] s_DrawnPasses =
        {
            "UniversalForwardOnly", "UniversalForward", "UniversalGBuffer", "DepthOnly", "DepthNormals", "DepthNormalsOnly", "ShadowCaster", "SceneSelectionPass", "Picking", "Universal2D",
        };

        /// <summary>The define the graph compiler writes into the shader of an output that the
        /// pipeline lights.</summary>
        const string LitDefine = "#define URP_LIT 1";

        /// <summary>A lit output is measured where it draws itself and an output drawn through
        /// an indirect buffer where it is unlit (tests/projects/vfxlit, tests/projects/vfxoutputs):
        /// the two together are not, and are not drawn until they are.</summary>
        internal const string LitIndirectRefusal = "it is lit and draws through an indirect buffer, which is not measured yet";

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
                    if (index < 0 || index >= sources.Length || sources[index].compute || !IsOutput(tasks[t].type)) continue;
                    string source = sources[index].source;
                    refusals[s][t] = SourceRefusal(source);
                    if (refusals[s][t] == null && Parameter(tasks[t], IndirectDrawName) != 0 && !string.IsNullOrEmpty(source) && source.Contains(LitDefine, StringComparison.Ordinal))
                        refusals[s][t] = LitIndirectRefusal;
                }
            }
            return refusals;
        }

        /// <summary>What the generated shader of an output does that is not drawn yet, read
        /// off its source: a pass that is not among the ones drawn; null without one
        /// (tests/projects/vfx, `draw-*`; tests/projects/vfxlit).</summary>
        internal static string SourceRefusal(string source)
        {
            if (string.IsNullOrEmpty(source)) return null;
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

        /// <summary>Whether an output draws its particles in a sorted order: from a sorted copy
        /// of its list (its indices are another buffer than the one that counts them), or in a
        /// system that sorts a list for its outputs (any of its outputs is refused then).</summary>
        static bool Sorted(in VFXEditorSystemDesc system, in VFXEditorTaskDesc task)
        {
            foreach (var other in system.tasks ?? Array.Empty<VFXEditorTaskDesc>())
                if (other.type == VFXTaskType.GlobalSort) return true;
            return Buffer(task, "indirectBuffer") != Parameter(task, "indirectIndex");
        }

        static int Buffer(in VFXEditorTaskDesc task, string name)
        {
            foreach (var buffer in task.buffers ?? Array.Empty<VFXMapping>())
                if (buffer.name == name) return buffer.index;
            return -1;
        }

        /// <summary>What a parameter of a task holds (a number, or the expression that gives
        /// one), <paramref name="missing"/> when the task has none of that name.</summary>
        internal static int Parameter(in VFXEditorTaskDesc task, string name, int missing = 0)
        {
            foreach (var parameter in task.parameters ?? Array.Empty<VFXMapping>())
                if (parameter.name == name) return parameter.index;
            return missing;
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

        /// <summary>Computes the arguments of an indirect draw from the count its list has now
        /// (after the kernels that make the list for this camera). False when they cannot be.</summary>
        internal bool FillArguments()
        {
            var arguments = VFXIndirectArgs.Shared;
            if (!arguments.Fill(m_ArgsEntry, m_System.Effect.GpuBuffer(m_CountBuffer, 0), m_CountPairs)) return false;
            m_FilledGeneration = arguments.Generation;
            return true;
        }

        /// <summary>Adds the output's draw for a camera that sees the effect's renderer (an
        /// indirect one's list made for that camera: <see cref="VFXParticleSystem.PrepareCamera"/>).</summary>
        internal void Collect(Renderer renderer, FrameBuilder.CameraContext camera, List<FrameBuilder.DrawItem> into)
        {
            uint quads = m_System.DrawnParticles;
            if (quads == 0 || !m_Material) return;
            var arguments = VFXIndirectArgs.Shared;
            if (Indirect)
            {
                if (!arguments.Ready()) return;
                // Arguments made anew since this camera's were computed are computed again.
                if (m_FilledGeneration != arguments.Generation && !FillArguments()) return;
            }
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
            var baked = effect.BakedTexture;
            if (!ReferenceEquals(baked, m_Baked) && baked != null) m_Block.SetTexture(s_BakedTexture, m_Baked = baked);
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
            if (m_Task.type == VFXTaskType.ParticleMeshOutput)
            {
                CollectMeshes(renderer, quads, camera, into);
                return;
            }
            var setup = Indirect
                ? IndexedDraws.Indirect(QuadIndices(), arguments.Arguments.Handle, VFXIndirectArgs.Offset(m_ArgsEntry))
                : QuadDraw(QuadIndices(), quads);
            FrameBuilder.AddProceduralDraw(renderer, m_Material, renderer.transform.localToWorldMatrix, setup, m_Block.m_Values, camera, into);
        }
    }
}
