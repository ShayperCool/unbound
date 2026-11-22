using Unbound.Runtime;
using UnityEditor.VFX;
using UnityEngine.VFX;
using Xunit;
using static UnityEngine.Tests.VisualEffectSimulationTests;

namespace UnityEngine.Tests
{
    /// <summary>The names a compiled graph's lookups go by.</summary>
    public class VisualEffectSealTests
    {
        [Fact]
        public void AGraphWhoseSystemOrPropertyHasNoNameIsSealed()
        {
            var data = Data(new[] { Value(VFXValueType.Float) }, new[] { Constant(0, 1f) },
                systems: new[] { new VFXEditorSystemDesc { type = VFXSystemType.Spawner } },
                events: new[] { new VFXEventDesc() },
                exposed: new[] { new VFXExposedMapping { mapping = new VFXMapping(null, 0) } });
            Assert.Single(data.Systems);
            Assert.Equal(new[] { Shader.PropertyToID("") }, data.EventIds);
        }
    }
}
