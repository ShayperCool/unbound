using System;
using System.Collections.Generic;
using Unbound.Runtime;
using UnityEditor;
using UnityEditor.VFX;
using UnityEngine.Rendering;

namespace UnityEngine.VFX
{
    /// <summary>
    /// A VisualEffectAsset as a build serializes it (an AssetBundle's): its compiled graph read
    /// from its document. The programs a build holds are compiled for its graphics device, as
    /// every built shader is; Unbound compiles programs from their sources, so the built asset
    /// runs with the programs of the project's graph at the path its bundle lists it under, when
    /// that graph compiles to the same graph. Without one it does not run and says why. Matches the Unity 6000.0.71f1 Editor (tests/projects/vfx, <c>bundle/*</c>).
    /// </summary>
    public partial class VisualEffectAsset
    {
        /// <summary>Whether the asset came from a build and has not taken its programs yet.</summary>
        bool m_BuiltPrograms;
        /// <summary>The path its bundle's container lists the asset under; null for a graph a
        /// bundle holds only as another asset's dependency.</summary>
        string m_BuiltPath;

        internal void NameBuiltPath(string path) => m_BuiltPath ??= path;

        internal void ReadSerialized(YNode body, IReferenceResolver resolver)
        {
            var infos = body.Get("m_Infos");
            if (infos == null) return;
            m_Settings = VisualEffectSettings.Read(infos);
            var expressions = infos.Get("m_Expressions");
            var all = new List<VFXExpressionDesc>();
            foreach (var e in expressions?.Get("m_Expressions")?.Seq() ?? Array.Empty<YNode>())
                all.Add(new VFXExpressionDesc
                {
                    op = (VFXExpressionOperation)e.Int("op"),
                    data = new[] { e.Int("data[0]", -1), e.Int("data[1]", -1), e.Int("data[2]", -1), e.Int("data[3]", -1) },
                });
            int common = Math.Clamp(expressions?.Int("m_MaxCommonExpressionsIndex", all.Count) ?? all.Count, 0, all.Count);
            var sheet = new VFXExpressionSheet
            {
                expressions = all.GetRange(0, common).ToArray(),
                expressionsPerSpawnEventAttribute = all.GetRange(common, all.Count - common).ToArray(),
                values = Values(infos.Get("m_PropertySheet"), all, resolver),
                exposed = Exposed(infos.Get("m_ExposedExpressions")),
            };
            var systems = Systems(body.Get("m_Systems"), resolver, out var built);
            var renderer = m_Settings.Renderer;
            var data = VisualEffectResource.CompiledData(sheet, systems, Events(infos.Get("m_Events")), GpuBuffers(infos.Get("m_Buffers")),
                CpuBuffers(infos.Get("m_CPUBuffers")), TemporaryBuffers(infos.Get("m_TemporaryBuffers")),
                renderer.shadowCastingMode, renderer.motionVectorGenerationMode, (VFXInstancingDisabledReason)infos.Int("m_InstancingDisabledReason"),
                (uint)infos.Long("m_CompilationVersion"));
            data.Processors = built;
            data.Seal();
            m_Data = data;
            uint version = (uint)infos.Long("m_RuntimeVersion");
            // A graph of another runtime data layout is not read as this one.
            if (version != RuntimeDataVersion) data.BuiltRefusal = $"a compiled graph of runtime data version {version} (this release's is {RuntimeDataVersion})";
            else m_BuiltPrograms = true;
        }

        static VFXMapping[] Mappings(YNode list)
        {
            var mappings = new List<VFXMapping>();
            foreach (var m in list?.Seq() ?? Array.Empty<YNode>()) mappings.Add(new VFXMapping(m.Str("nameId"), m.Int("index")));
            return mappings.ToArray();
        }

        static uint[] Indices(YNode list)
        {
            var indices = new List<uint>();
            foreach (var i in list?.Seq() ?? Array.Empty<YNode>()) indices.Add((uint)i.AsLong());
            return indices.ToArray();
        }

        static VFXLayoutElementDesc[] Layout(YNode list)
        {
            var layout = new List<VFXLayoutElementDesc>();
            foreach (var e in list?.Seq() ?? Array.Empty<YNode>())
            {
                var offset = e.Get("offset");
                layout.Add(new VFXLayoutElementDesc
                {
                    name = e.Str("name"),
                    type = (VFXValueType)e.Int("type"),
                    offset = new VFXLayoutOffset { bucket = (uint)(offset?.Long("bucket") ?? 0), structure = (uint)(offset?.Long("structure") ?? 0), element = (uint)(offset?.Long("element") ?? 0) },
                });
            }
            return layout.ToArray();
        }

        static VFXGPUBufferDesc GpuBuffer(YNode b) => new VFXGPUBufferDesc
        {
            target = (GraphicsBuffer.Target)b.Int("target"), size = (uint)b.Long("size"), layout = Layout(b.Get("layout")),
            capacity = (uint)b.Long("capacity"), stride = (uint)b.Long("stride"), mode = (ComputeBufferMode)b.Int("mode"),
        };

        static VFXGPUBufferDesc[] GpuBuffers(YNode list)
        {
            var buffers = new List<VFXGPUBufferDesc>();
            foreach (var b in list?.Seq() ?? Array.Empty<YNode>()) buffers.Add(GpuBuffer(b));
            return buffers.ToArray();
        }

        static VFXTemporaryGPUBufferDesc[] TemporaryBuffers(YNode list)
        {
            var buffers = new List<VFXTemporaryGPUBufferDesc>();
            foreach (var b in list?.Seq() ?? Array.Empty<YNode>())
                buffers.Add(new VFXTemporaryGPUBufferDesc { frameCount = (uint)b.Long("frameCount"), desc = b.Get("desc") is YNode d ? GpuBuffer(d) : default });
            return buffers.ToArray();
        }

        static VFXCPUBufferDesc[] CpuBuffers(YNode list)
        {
            var buffers = new List<VFXCPUBufferDesc>();
            foreach (var b in list?.Seq() ?? Array.Empty<YNode>())
            {
                var initial = new VFXCPUBufferData();
                foreach (var word in b.Get("initialData")?.Get("data")?.Seq() ?? Array.Empty<YNode>()) initial.PushUInt((uint)word.AsLong());
                buffers.Add(new VFXCPUBufferDesc { capacity = (uint)b.Long("capacity"), stride = (uint)b.Long("stride"), layout = Layout(b.Get("layout")), initialData = initial });
            }
            return buffers.ToArray();
        }

        static VFXEventDesc[] Events(YNode list)
        {
            var events = new List<VFXEventDesc>();
            foreach (var e in list?.Seq() ?? Array.Empty<YNode>())
                events.Add(new VFXEventDesc { name = e.Str("name"), startSystems = Indices(e.Get("playSystems")), stopSystems = Indices(e.Get("stopSystems")), initSystems = Indices(e.Get("initSystems")) });
            return events.ToArray();
        }

        static VFXExposedMapping[] Exposed(YNode list)
        {
            var exposed = new List<VFXExposedMapping>();
            foreach (var e in list?.Seq() ?? Array.Empty<YNode>())
            {
                var mapping = e.Get("mapping");
                exposed.Add(new VFXExposedMapping { mapping = new VFXMapping(mapping?.Str("nameId"), mapping?.Int("index") ?? -1), space = (VFXSpace)e.Int("space", -1) });
            }
            return exposed.ToArray();
        }

        /// <summary>The systems with their tasks; each task's processor (the built program, or a
        /// spawn block's script object) in <paramref name="processors"/>.</summary>
        static VFXEditorSystemDesc[] Systems(YNode list, IReferenceResolver resolver, out Object[][] processors)
        {
            var systems = new List<VFXEditorSystemDesc>();
            var objects = new List<Object[]>();
            foreach (var s in list?.Seq() ?? Array.Empty<YNode>())
            {
                var tasks = new List<VFXEditorTaskDesc>();
                var taskObjects = new List<Object>();
                foreach (var t in s.Get("tasks")?.Seq() ?? Array.Empty<YNode>())
                {
                    var temporary = new List<VFXMappingTemporary>();
                    foreach (var m in t.Get("temporaryBuffers")?.Seq() ?? Array.Empty<YNode>())
                    {
                        var mapping = m.Get("mapping");
                        temporary.Add(new VFXMappingTemporary(new VFXMapping(mapping?.Str("nameId"), mapping?.Int("index") ?? -1), (uint)m.Long("pastFrameIndex"), m.Bool("perCameraBuffer")));
                    }
                    var task = new VFXEditorTaskDesc
                    {
                        type = (VFXTaskType)t.Int("type"), buffers = Mappings(t.Get("buffers")), temporaryBuffers = temporary.ToArray(),
                        values = Mappings(t.Get("values")), parameters = Mappings(t.Get("params")), instanceSplitIndex = (uint)t.Long("instanceSplitIndex"),
                    };
                    var processor = t.Ref("processor");
                    taskObjects.Add(processor.IsNull ? null : resolver.Resolve(processor, typeof(Object)));
                    tasks.Add(task);
                }
                var splits = new List<VFXInstanceSplitDesc>();
                foreach (var d in s.Get("instanceSplitDescs")?.Seq() ?? Array.Empty<YNode>()) splits.Add(new VFXInstanceSplitDesc { values = Indices(d.Get("values")) });
                systems.Add(new VFXEditorSystemDesc
                {
                    type = (VFXSystemType)s.Int("type"), flags = (VFXSystemFlag)s.Long("flags"), capacity = (uint)s.Long("capacity"), layer = (uint)s.Long("layer"),
                    name = s.Str("name"), buffers = Mappings(s.Get("buffers")), values = Mappings(s.Get("values")), tasks = tasks.ToArray(), instanceSplitDescs = splits.ToArray(),
                });
                objects.Add(taskObjects.ToArray());
            }
            processors = objects.ToArray();
            return systems.ToArray();
        }

        /// <summary>The constants of the property sheet, by value type.</summary>
        static VFXExpressionValueContainerDesc[] Values(YNode sheet, List<VFXExpressionDesc> expressions, IReferenceResolver resolver)
        {
            var values = new List<VFXExpressionValueContainerDesc>();
            void Each(string key, Func<YNode, VFXExpressionValueContainerDesc> make)
            {
                foreach (var v in sheet?.Get(key)?.Get("m_Array")?.Seq() ?? Array.Empty<YNode>())
                {
                    var desc = make(v.Get("m_Value"));
                    if (desc == null) continue;
                    desc.expressionIndex = (uint)v.Long("m_ExpressionIndex");
                    values.Add(desc);
                }
            }
            VFXExpressionValueContainerDesc Of<T>(T value) => new VFXExpressionValueContainerDesc<T> { value = value };
            Each("m_Float", n => Of(n.AsFloat()));
            Each("m_Vector2f", n => Of(new Vector2(n.Float("x"), n.Float("y"))));
            Each("m_Vector3f", n => Of(new Vector3(n.Float("x"), n.Float("y"), n.Float("z"))));
            Each("m_Vector4f", n => Of(new Vector4(n.Float("x"), n.Float("y"), n.Float("z"), n.Float("w"))));
            Each("m_Uint", n => Of((uint)n.AsLong()));
            Each("m_Int", n => Of(n.AsInt()));
            Each("m_Bool", n => Of(n.AsBool()));
            Each("m_Matrix4x4f", n =>
            {
                var m = new Matrix4x4();
                for (int row = 0; row < 4; row++)
                    for (int column = 0; column < 4; column++) m[row, column] = n.Float($"e{row}{column}");
                return Of(m);
            });
            Each("m_AnimationCurve", n => Of(BuiltinReaders.AnimationCurve(n)));
            Each("m_Gradient", n => Of(BuiltinReaders.Gradient(n)));
            foreach (var v in sheet?.Get("m_NamedObject")?.Get("m_Array")?.Seq() ?? Array.Empty<YNode>())
            {
                int index = v.Int("m_ExpressionIndex");
                var reference = v.Ref("m_Value");
                var value = reference.IsNull ? null : resolver.Resolve(reference, typeof(Object));
                int id = value ? value.GetInstanceID() : 0;
                var type = (uint)index < (uint)expressions.Count ? VFXExpressionHelper.TypeOf(expressions[index].op, expressions[index].data) : VFXValueType.None;
                VFXExpressionValueContainerDesc desc = type switch
                {
                    VFXValueType.Mesh => new VFXExpressionObjectValueContainerDesc<Mesh> { instanceID = id },
                    VFXValueType.SkinnedMeshRenderer => new VFXExpressionObjectValueContainerDesc<SkinnedMeshRenderer> { instanceID = id },
                    _ => new VFXExpressionObjectValueContainerDesc<Texture> { instanceID = id },
                };
                desc.expressionIndex = (uint)index;
                values.Add(desc);
            }
            return values.ToArray();
        }

        /// <summary>For an asset read from a build, before an effect first runs it: the programs
        /// of the project's graph at the asset's path in its bundle, when that graph compiles to
        /// the same graph; otherwise the asset says why it does not run. The values written into
        /// the graph's blocks are part of its programs: a project graph changed in those after
        /// the build cannot be told apart, so the asset says which graph it runs with.</summary>
        internal void TakeBuiltPrograms()
        {
            if (m_BuiltPrograms) TakeBuiltPrograms(m_BuiltPath, ProjectGraphAt);
        }

        /// <summary>The project's graph asset at a path, compared without case (a bundle keeps
        /// its paths in lower case); null without one.</summary>
        static VisualEffectAsset ProjectGraphAt(string path)
        {
            foreach (var guid in AssetDatabase.FindAssets("t:VisualEffectAsset"))
            {
                string found = AssetDatabase.GUIDToAssetPath(guid);
                if (string.Equals(found, path, StringComparison.OrdinalIgnoreCase)) return AssetDatabase.LoadAssetAtPath<VisualEffectAsset>(found);
            }
            return null;
        }

        internal void TakeBuiltPrograms(string path, Func<string, VisualEffectAsset> graphAt)
        {
            if (!m_BuiltPrograms) return;
            m_BuiltPrograms = false;
            if (path == null)
            {
                m_Data.BuiltRefusal = "programs built for a graphics device, and its bundle does not list it (a graph built in as another asset's dependency), so no graph of the project is known to compile them";
                return;
            }
            var twin = graphAt(path);
            if (twin == null || ReferenceEquals(twin, this) || twin.m_BuiltPrograms)
            {
                m_Data.BuiltRefusal = $"programs built for a graphics device, and the project has no graph at {path} to compile them from";
                return;
            }
            if (!twin.m_Data.SameGraph(m_Data))
            {
                m_Data.BuiltRefusal = $"programs built for a graphics device, and the project's graph at {path} compiles to another graph";
                return;
            }
            m_Data.Processors = twin.m_Data.Processors;
            m_Data.OutputRefusals = twin.m_Data.OutputRefusals;
            m_ShaderSourceIndices = twin.m_ShaderSourceIndices;
            // What a build does not keep of a task: whether its output's material is a variant.
            for (int s = 0; s < m_Data.Systems.Length; s++)
            {
                var tasks = m_Data.Systems[s].tasks;
                for (int t = 0; t < (tasks?.Length ?? 0); t++) tasks[t].usesMaterialVariant = twin.m_Data.Systems[s].tasks[t].usesMaterialVariant;
            }
            Debug.Log($"Unbound: the built VisualEffectAsset '{name}' runs with the programs the project's {path} compiles to (values written into its blocks are the project's).");
        }
    }
}
