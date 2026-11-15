using System;
using System.Collections.Generic;
using System.Linq;
using Unbound.Runtime;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>The generated reader field tables that field coverage (tests/projects/classids,
    /// `unbound doctor`) consumes must derive every key: a key the generator cannot follow fails
    /// here, before a landing, rather than in the coverage run.</summary>
    public class ReaderFieldTableTests
    {
        sealed class Script : MonoBehaviour { }
        sealed class Settings : ScriptableObject { }

        /// <summary>Every declaration field coverage can consume: the project settings, the fixed
        /// scene documents, scripts of each kind and every registered engine type.</summary>
        static IEnumerable<(Type Owner, string Method, int Parameters, int Node)> Consumed()
        {
            foreach (int id in new[] { 55, 19, 126 })
                foreach (var d in FieldCoverage.Declarations("projectSettings", id, null)) yield return d;
            foreach (var script in new[] { typeof(Script), typeof(Settings) })
                foreach (var d in FieldCoverage.Declarations(null, EngineTypes.MonoBehaviourId, script)) yield return d;
            foreach (int id in new[] { EngineTypes.PrefabInstanceId, 1660057539, 142, 115, EngineTypes.GameObjectId, 104, 157, 196, 124 })
                foreach (var d in FieldCoverage.Declarations(null, id, null)) yield return d;
            foreach (int id in EngineTypes.RegisteredIds.ToArray())
                if (EngineTypes.ForClassId(id, null, warn: false) is Type type)
                    foreach (var d in FieldCoverage.Declarations(null, id, type)) yield return d;
        }

        [Fact]
        public void EveryConsumedReaderKeyIsDerived()
        {
            var problems = new SortedSet<string>(StringComparer.Ordinal);
            var empty = new YNode { Kind = YKind.Map, Keys = Array.Empty<string>(), Values = Array.Empty<YNode>() };
            foreach (var (owner, method, parameters, node) in Consumed().Distinct())
            {
                try { ReaderFieldSchema.Consume(empty, ReaderFieldSchema.Fields(owner, method, parameters, node)); }
                catch (NotSupportedException e) { problems.Add(owner.FullName + "." + method + ": " + e.Message.Replace('\u001f', '/')); }
            }
            Assert.True(problems.Count == 0, "underivable reader keys:\n" + string.Join("\n", problems));
        }

        [Fact]
        public void InterpolationPaddingIsPartOfTheKey()
        {
            // RenderSettings reads its ambient probe as $"sh[{k,2}]": "sh[ 0]" to "sh[26]".
            var reads = ReaderFieldSchema.Fields(typeof(RenderSettings), nameof(RenderSettings.SceneLoaded), 3, 1);
            Assert.Contains("km_AmbientProbe\u001fksh[ 0]", reads);
            Assert.Contains("km_AmbientProbe\u001fksh[26]", reads);
            Assert.DoesNotContain("km_AmbientProbe\u001fksh[0]", reads);
        }
    }
}
