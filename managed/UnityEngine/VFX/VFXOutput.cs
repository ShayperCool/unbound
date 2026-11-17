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
    /// vertex id (or, of a mesh, by its instance). Drawn here: planar primitives (quads,
    /// triangles, octagons: <see cref="Primitive"/>), either all the system has room for (the
    /// vertex program leaves out the dead ones) or through an indirect buffer: a
    /// list of the particles to draw that the system's kernels make (the update kernel, an
    /// output-update kernel at each step, or one for each camera that sees the effect), sorted
    /// when the output sorts (<see cref="VFXSort"/>), whose count gives the draw's arguments
    /// (<see cref="VFXIndirectArgs"/>); and meshes the output draws itself. The quads of a strip
    /// output go from each particle of a strip to the next: for all the strips' room (the
    /// vertex program leaves out those past a strip's last particle), or from each particle of
    /// a sorted list. What an output draws itself, or from a sorted list, may be lit by the
    /// pipeline; what goes through a list that is not sorted is unlit. An
    /// output of another kind is not drawn and says so once (<see cref="Refusal"/>): the system
    /// is simulated all the same. The draws match the Unity 6000.0.71f1 Editor
    /// (tests/projects/vfx `draw-*`, tests/projects/vfxoutputs, tests/projects/vfxlit,
    /// tests/projects/vfxstrips).
    /// </summary>
    internal sealed partial class VFXOutput
    {
        static readonly int s_InstancingConstants = Shader.PropertyToID("instancingConstants");
        static readonly int s_InstanceIndex = Shader.PropertyToID("_InstanceIndex");
        static readonly int s_InstanceActiveIndex = Shader.PropertyToID("_InstanceActiveIndex");
        /// <summary>The texture the generated programs sample curves and gradients from.</summary>
        internal const string BakedTextureName = "bakedTexture";
        static readonly int s_BakedTexture = Shader.PropertyToID(BakedTextureName);

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
        readonly bool m_CountPairs, m_Sorted;
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
            m_Strips = system.HasStrips && Parameter(task, StripsName) != 0;
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
                m_Sorted = ReadsSortedList(task);
            }
        }

        const string IndirectDrawName = "indirectDraw";
        /// <summary>The parameter of an output that draws its system's strips: quads from each
        /// particle of a strip to the next, rather than one at each particle.</summary>
        const string StripsName = "strips";
        readonly bool m_Strips;

        /// <summary>Whether the output draws through an indirect buffer.</summary>
        internal bool Indirect => m_ArgsEntry >= 0;

        /// <summary>The system whose particles the output draws.</summary>
        internal VFXParticleSystem System => m_System;

        /// <summary>The output's place among the effect's outputs (its `sortPriority`): the
        /// lowest is drawn first.</summary>
        internal int SortPriority => Parameter(m_Task, "sortPriority");

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
            if (!IsPlanar(task.type) && task.type != VFXTaskType.ParticleMeshOutput) return $"{task.type} is not drawn yet (quads, triangles, octagons and meshes are)";
            if ((system.flags & VFXSystemFlag.SystemHasStrips) != 0)
            {
                // Strips are quads from each particle to the next, drawn for all the strips'
                // room; a strip system's particles may be drawn one by one as well.
                if (task.type != VFXTaskType.ParticleQuadOutput) return $"{task.type} of a strip system's particles is not measured yet";
                // Through an indirect buffer: strips from a sorted list (what an alpha blended
                // strip output does by default) are measured.
                if (Parameter(task, IndirectDrawName) != 0)
                {
                    if (Parameter(task, StripsName) == 0) return "it draws a strip system's particles one by one through an indirect buffer, which is not measured yet";
                    if (!ReadsSortedList(task)) return "it draws strips through an indirect buffer that is not sorted, which is not measured yet";
                }
            }
            if (Parameter(task, IndirectDrawName) != 0)
            {
                // Meshes are drawn for the particles the system has to draw, not from a list.
                if (task.type == VFXTaskType.ParticleMeshOutput) return "it draws meshes through an indirect buffer, which is not done yet";
                if (SortsForAll(system)) return "its system sorts one list for all its outputs, which is not done yet";
                if (!ListMade(system, task)) return "it draws a list of particles no task of its system makes or can sort";
            }
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

        /// <summary>A lit output is measured where it draws itself and where it draws a sorted
        /// list (an alpha blended one sorts by default: tests/projects/vfxlit, `lit sorted`);
        /// through a list that is not sorted only unlit outputs are (tests/projects/vfxoutputs),
        /// and a lit one is not drawn until it is.</summary>
        internal const string LitIndirectRefusal = "it is lit and draws through an indirect buffer that is not sorted, which is not measured yet";

        /// <summary>Strips are measured unlit (tests/projects/vfxstrips): a lit strip output is
        /// not drawn until it is.</summary>
        internal const string LitStripRefusal = "it is a lit output of strips, which is not measured yet";

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
                    if (refusals[s][t] != null || string.IsNullOrEmpty(source) || !source.Contains(LitDefine, StringComparison.Ordinal)) continue;
                    if ((systems[s].flags & VFXSystemFlag.SystemHasStrips) != 0) refusals[s][t] = LitStripRefusal;
                    else if (Parameter(tasks[t], IndirectDrawName) != 0 && !ReadsSortedList(tasks[t])) refusals[s][t] = LitIndirectRefusal;
                }
            }
            // The renderer finds a generated shader by its name: outputs whose shaders share one
            // with another source (outputs of one system the graph names alike: no label, no
            // letter) cannot each be drawn with their own.
            var seen = new Dictionary<string, string>(StringComparer.Ordinal);
            var clashing = new HashSet<string>(StringComparer.Ordinal);
            foreach (var system in systems)
                foreach (var task in system.tasks ?? Array.Empty<VFXEditorTaskDesc>())
                    if (task.shaderSourceIndex >= 0 && task.shaderSourceIndex < sources.Length && !sources[task.shaderSourceIndex].compute && IsOutput(task.type)
                        && ShaderLab.Name(sources[task.shaderSourceIndex].source ?? "") is string name)
                    {
                        string source = sources[task.shaderSourceIndex].source;
                        if (!seen.TryAdd(name, source) && !string.Equals(seen[name], source, StringComparison.Ordinal)) clashing.Add(name);
                    }
            for (int s = 0; s < systems.Length; s++)
            {
                var tasks = systems[s].tasks ?? Array.Empty<VFXEditorTaskDesc>();
                for (int t = 0; t < tasks.Length; t++)
                {
                    int index = tasks[t].shaderSourceIndex;
                    if (index >= 0 && index < sources.Length && !sources[index].compute && IsOutput(tasks[t].type)
                        && ShaderLab.Name(sources[index].source ?? "") is string name && clashing.Contains(name))
                        refusals[s][t] ??= $"its shader's name '{name}' is another output's, whose shader differs, and Unbound finds generated shaders by name";
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

        /// <summary>Whether a system sorts one list of particles for all its outputs (a global
        /// sort, which the compiler makes for outputs that share their sort).</summary>
        static bool SortsForAll(in VFXEditorSystemDesc system)
        {
            foreach (var other in system.tasks ?? Array.Empty<VFXEditorTaskDesc>())
                if (other.type == VFXTaskType.GlobalSort) return true;
            return false;
        }

        /// <summary>Whether an output draws a sorted copy of its list: one a sort task of its
        /// system writes (its indices are another buffer than the one that counts them).</summary>
        static bool ListMade(in VFXEditorSystemDesc system, in VFXEditorTaskDesc task)
        {
            if (!ReadsSortedList(task)) return true;
            int read = Buffer(task, "indirectBuffer");
            foreach (var other in system.tasks ?? Array.Empty<VFXEditorTaskDesc>())
                if (other.type == VFXTaskType.PerOutputSort && Buffer(other, "dstBuffer") == read)
                    return Buffer(other, "srcBuffer") == Parameter(task, "indirectIndex") && VFXSort.Plan(system.capacity, Buffer(other, "scratchBuffer") >= 0) != null;
            return false;
        }

        /// <summary>Whether the output draws a sorted copy of its list.</summary>
        internal bool Sorted => m_Sorted;

        /// <summary>Whether an indirect output's task draws a sorted copy of its list: the
        /// indices it reads are another buffer than the one that counts them.</summary>
        static bool ReadsSortedList(in VFXEditorTaskDesc task) => Buffer(task, "indirectBuffer") != Parameter(task, "indirectIndex");

        internal static int Buffer(in VFXEditorTaskDesc task, string name)
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

        /// <summary>Whether a task draws a planar primitive (a triangle, a quad or an octagon
        /// for each particle).</summary>
        internal static bool IsPlanar(VFXTaskType type) =>
            type == VFXTaskType.ParticleQuadOutput || type == VFXTaskType.ParticleTriangleOutput || type == VFXTaskType.ParticleOctagonOutput;

        /// <summary>The planar primitive an output task draws.</summary>
        internal static Primitive PrimitiveOf(VFXTaskType type) =>
            type == VFXTaskType.ParticleTriangleOutput ? Primitive.Triangle : type == VFXTaskType.ParticleOctagonOutput ? Primitive.Octagon : Primitive.Quad;

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
            if (m_Strips) quads = m_System.StripSegments;
            if (quads == 0) return;
            var arguments = VFXIndirectArgs.Shared;
            if (Indirect)
            {
                if (!arguments.Ready() || Sorted && !VFXSort.Shared.Usable()) return;
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
                else m_Block.Remove(name);
            }
            if (m_Task.type == VFXTaskType.ParticleMeshOutput)
            {
                CollectMeshes(renderer, quads, camera, into);
                return;
            }
            var primitive = PrimitiveOf(m_Task.type);
            // Indices for as many as the system has room for: made once (triangles grow them).
            var indices = primitive.Indices(m_System.Capacity);
            var setup = Indirect
                ? IndexedDraws.Indirect(indices, arguments.Arguments.Handle, VFXIndirectArgs.Offset(m_ArgsEntry))
                : primitive.Draw(indices, quads);
            FrameBuilder.AddProceduralDraw(renderer, m_Material, renderer.transform.localToWorldMatrix, setup, m_Block.m_Values, camera, into);
        }
    }
}
