using System.Reflection;
using UnityEditor;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>The editor domain load as the Unity 6000.0.71f1 Editor runs it
    /// (tests/projects/initializeorder): [InitializeOnLoad] classes by assembly name and
    /// dependencies, and imports and postprocessing outside a domain load not deferred.</summary>
    public sealed class InitializeOrderTests
    {
        [Fact]
        public void AnAssemblyComesAfterThoseItReferences()
        {
            var tests = typeof(InitializeOrderTests).Assembly;
            var engine = typeof(GameObject).Assembly;
            // The tests reference the engine, so it comes first in either listed order.
            Assert.Equal(new[] { engine, tests }, EditorInitialization.InDependencyOrder(new[] { tests, engine }).ToArray());
            Assert.Equal(new[] { engine, tests }, EditorInitialization.InDependencyOrder(new[] { engine, tests }).ToArray());
            // Unrelated assemblies come by name, whatever the listed order.
            var json = typeof(System.Text.Json.JsonDocument).Assembly;
            var asserts = typeof(Assert).Assembly;
            Assert.Equal(new[] { json, asserts }, EditorInitialization.InDependencyOrder(new[] { asserts, json }).ToArray());
        }

        [Fact]
        public void ImportsAndPostprocessingOutsideADomainLoadAreNotDeferred()
        {
            Assert.False(EditorInitialization.DeferImport("Assets/X.png", ImportAssetOptions.ForceUpdate));
            Assert.False(EditorInitialization.DeferPostprocess(() => { }));
        }
    }
}
