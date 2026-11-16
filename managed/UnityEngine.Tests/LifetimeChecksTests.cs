using System;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Unbound.Host;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>
    /// Game code's calls of engine members whose Unity binding refuses a dead receiver check it
    /// first (Unbound.Host.LifetimeChecks with the release's list): a destroyed receiver throws the
    /// binding's MissingReferenceException, a null one the Mono runtime's null reference, and calls
    /// of members the list leaves out, or through a constrained prefix, are not touched.
    /// </summary>
    [Collection("RenderSettings state")]
    public sealed class LifetimeChecksTests : IDisposable
    {
        const string Source = @"
using UnityEngine;
public static class Probe
{
    public static int Width(Texture2D t) => t.width;
    public static int Id(Texture2D t) => t.GetInstanceID();
    public static T Get<T>(GameObject go) where T : Component => go.GetComponent<T>();
    public static void Parent(Transform child, Transform parent) => child.SetParent(parent);
    public static int Generic<T>(T texture) where T : Texture => texture.width;
}";

        static readonly string[] Listed =
        {
            "UnityEngine.Texture::get_width()", "UnityEngine.GameObject::GetComponent`1()", "UnityEngine.Transform::SetParent(UnityEngine.Transform)",
        };

        public LifetimeChecksTests() => LifetimeChecks.Set(Listed);

        public void Dispose() => LifetimeChecks.Set(Array.Empty<string>());

        static byte[] Image(OptimizationLevel optimization) =>
            WideFloatsTests.Emit(WideFloatsTests.Compilation(Source, optimization, "LifetimeProbe", engine: true)).Image;

        static object? Run(Assembly assembly, string method, params object?[] arguments)
        {
            try { return assembly.GetType("Probe")!.GetMethod(method)!.Invoke(null, arguments); }
            catch (TargetInvocationException e) { throw e.InnerException!; }
        }

        static object? RunGeneric(Assembly assembly, string method, Type argument, params object?[] arguments)
        {
            try { return assembly.GetType("Probe")!.GetMethod(method)!.MakeGenericMethod(argument).Invoke(null, arguments); }
            catch (TargetInvocationException e) { throw e.InnerException!; }
        }

        [Theory]
        [MemberData(nameof(WideFloatsTests.Modes), MemberType = typeof(WideFloatsTests))]
        public void ListedCallsCheckTheirReceiver(OptimizationLevel optimization)
        {
            var probe = WideFloatsTests.Load(LifetimeChecks.Instrument(Image(optimization)));
            var texture = new Texture2D(2, 2);
            Assert.Equal(2, Run(probe, "Width", texture));
            Object.DestroyImmediate(texture);
            // The engine's own getter answers a destroyed texture; the binding does not.
            Assert.Equal("The object of type 'UnityEngine.Texture2D' has been destroyed but you are still trying to access it.\n" +
                         "Your script should either check if it is null or you should not destroy the object.",
                Assert.Throws<MissingReferenceException>(() => Run(probe, "Width", texture)).Message);
            // A member the list leaves out answers.
            Run(probe, "Id", texture);
            Assert.Equal("Object reference not set to an instance of an object",
                Assert.Throws<NullReferenceException>(() => Run(probe, "Width", new object?[] { null })).Message);

            var go = new GameObject("Doomed");
            var child = new GameObject("Child").transform;
            Assert.Null(RunGeneric(probe, "Get", typeof(BoxCollider), go));
            Object.DestroyImmediate(go);
            Assert.Throws<MissingReferenceException>(() => RunGeneric(probe, "Get", typeof(BoxCollider), go));
            Object.DestroyImmediate(child.gameObject);
            Assert.Throws<MissingReferenceException>(() => Run(probe, "Parent", child, null));
            // A type parameter's member answers a live object.
            Assert.Equal(2, RunGeneric(probe, "Generic", typeof(Texture2D), new Texture2D(2, 2)));
        }

        [Fact]
        public void AnAssemblyWithoutListedCallsIsLeftAsItIs()
        {
            var image = Image(OptimizationLevel.Release);
            LifetimeChecks.Set(new[] { "UnityEngine.Light::get_range()" });
            Assert.Same(image, LifetimeChecks.Instrument(image));
        }
    }
}
