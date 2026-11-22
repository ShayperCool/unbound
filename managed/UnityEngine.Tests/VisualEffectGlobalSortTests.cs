using System;
using System.Reflection;
using Unbound.Runtime;
using UnityEditor.VFX;
using UnityEngine.VFX;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>A system's global sort (the compiler's, for outputs that share their sort): its
    /// keys kernel and the sort of its keys into the system's list, kept when an output that is
    /// drawn reads that list, for each camera when the task says so (tests/projects/vfxoutputs
    /// `Global*`).</summary>
    public partial class VisualEffectSimulationTests
    {
        static readonly FieldInfo s_GlobalSort = typeof(VFXParticleSystem).GetField("m_GlobalSort", BindingFlags.NonPublic | BindingFlags.Instance)!;
        static readonly FieldInfo s_GlobalKeys = typeof(VFXParticleSystem).GetField("m_GlobalKeys", BindingFlags.NonPublic | BindingFlags.Instance)!;

        /// <summary>The counting effect's particles with a global sort (per camera or not) and
        /// one indirect output drawing list <paramref name="drawn"/>; the system's list is
        /// buffer 2, the sort's keys and scratch list buffers 3 and 4.</summary>
        static (object Sort, object Keys) GlobalSortOf(bool perCamera, int drawn, bool keys = true)
        {
            var shader = UnityEditor.ShaderUtil.CreateShaderAsset("Shader \"Tests/VfxShared\" { SubShader { Pass { Tags { \"LightMode\"=\"UniversalForwardOnly\" } } } }");
            var asset = Counting(30f);
            var system = asset.m_Data.Systems[1];
            system.capacity = 32;
            system.buffers = keys
                ? new[] { new VFXMapping("spawner_input", 1), new VFXMapping("indirectBuffer0", 2), new VFXMapping("sortBufferA", 3), new VFXMapping("sortBufferB", 4) }
                : new[] { new VFXMapping("spawner_input", 1), new VFXMapping("indirectBuffer0", 2) };
            system.tasks = new[]
            {
                new VFXEditorTaskDesc
                {
                    type = VFXTaskType.GlobalSort,
                    parameters = new[] { new VFXMapping("globalSort", 1), new VFXMapping("isPerCameraSort", perCamera ? 1 : 0) },
                    buffers = new[] { new VFXMapping("inputBuffer", 2), new VFXMapping("outputBuffer", 3) },
                },
                new VFXEditorTaskDesc
                {
                    type = VFXTaskType.ParticleQuadOutput,
                    parameters = new[] { new VFXMapping("sortPriority", 0), new VFXMapping("indirectDraw", 1), new VFXMapping("indirectIndex", drawn) },
                    buffers = new[] { new VFXMapping("indirectBuffer", drawn) },
                },
            };
            asset.m_Data.Systems[1] = system;
            asset.m_Data.Processors = new[]
            {
                Array.Empty<Object>(),
                new Object[] { ComputeShader.FromFile("Tests/VfxGlobalSortKeys.compute", "VfxGlobalSortKeys", "#pragma kernel CSMain\n"), new Material(shader) },
            };
            var effect = Running(asset);
            try
            {
                using var warnings = new Warnings();
                Counts(effect, 1);
                var particles = effect.SystemAt(1);
                return (s_GlobalSort.GetValue(particles), s_GlobalKeys.GetValue(particles));
            }
            finally
            {
                Object.DestroyImmediate(effect.gameObject);
            }
        }

        static bool PerCamera(object sort) => (bool)sort.GetType().GetField("PerCamera", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(sort)!;

        static int SortBuffer(object sort, string name) => (int)sort.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(sort)!;

        [Fact]
        public void AGlobalSortOrdersTheSystemsListThroughItsKeysForEachCameraOrEachStep()
        {
            var (byCamera, keys) = GlobalSortOf(true, 2);
            Assert.NotNull(byCamera);
            Assert.NotNull(keys);
            Assert.True(PerCamera(byCamera));
            // The keys into the system's list, through the scratch list.
            Assert.Equal((3, 4, 2), (SortBuffer(byCamera, "List"), SortBuffer(byCamera, "Scratch"), SortBuffer(byCamera, "Sorted")));
            var (byStep, _) = GlobalSortOf(false, 2);
            Assert.False(PerCamera(byStep));
            // No output drawn reads the system's list: nothing sorts it.
            Assert.Equal((null, null), GlobalSortOf(true, 5));
            // Without its keys the sort is not made (and its outputs say why they are not drawn).
            Assert.Equal((null, null), GlobalSortOf(true, 2, keys: false));
        }
    }
}
