using System;
using System.IO;
using System.Linq;
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
    public static T Add<T>(GameObject go) where T : Component => go.AddComponent<T>();
    public static string TryGet(GameObject go) => go.TryGetComponent<BoxCollider>(out var found) + "" "" + (found == null);
}
public class Behaviour : MonoBehaviour
{
    public BoxCollider Base() => base.GetComponent<BoxCollider>();
}
public class Inspector : UnityEditor.Editor
{
    public void Header() => base.OnHeaderGUI();
}
public static class Keys
{
    public static void Mixed(in int x, int[,] a, System.Collections.Generic.List<int>[] b, ref float f) { }
}";

        static readonly string[] Listed =
        {
            "UnityEngine.Texture::get_width()", "UnityEngine.GameObject::GetComponent`1()", "UnityEngine.Transform::SetParent(UnityEngine.Transform)",
            "UnityEngine.GameObject::AddComponent`1()", "UnityEngine.GameObject::TryGetComponent`1(!!0&)", "UnityEngine.Component::GetComponent`1()",
            "UnityEditor.Editor::OnHeaderGUI()", "UnityEngine.Texture::get_height()",
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
        public void ConstraintsOutParametersAndBaseCallsKeepWorking()
        {
            var image = LifetimeChecks.Instrument(Image(OptimizationLevel.Release));
            var probe = WideFloatsTests.Load(image);
            var go = new GameObject("Holder");
            Assert.IsType<BoxCollider>(RunGeneric(probe, "Add", typeof(BoxCollider), go));
            Assert.Equal("True False", Run(probe, "TryGet", go));
            var behaviour = (Component)go.AddComponent(probe.GetType("Behaviour")!);
            Assert.NotNull(probe.GetType("Behaviour")!.GetMethod("Base")!.Invoke(behaviour, null));
            Object.DestroyImmediate(go);
            Assert.Throws<MissingReferenceException>(() => Run(probe, "TryGet", go));
            Assert.Throws<MissingReferenceException>(() =>
            {
                try { probe.GetType("Behaviour")!.GetMethod("Base")!.Invoke(behaviour, null); }
                catch (TargetInvocationException e) { throw e.InnerException!; }
            });
            // A protected member is called from its subclass only: the call stays as it is.
            using var module = Mono.Cecil.ModuleDefinition.ReadModule(new MemoryStream(image));
            var calls = module.GetType(Unbound.Runtime.Lifetime.CallsType)!;
            Assert.DoesNotContain(calls.Methods, m => m.Name.StartsWith("OnHeaderGUI_"));
            Assert.Contains(calls.Methods, m => m.Name.StartsWith("GetComponent_c"));
        }

        [Fact]
        public void KeysWriteTypesAsTheListDoes()
        {
            using var module = Mono.Cecil.ModuleDefinition.ReadModule(new MemoryStream(Image(OptimizationLevel.Release)));
            var method = module.GetType("Keys")!.Methods.Single(m => m.Name == "Mixed");
            // The same text Unbound.ApiTools.Tests.CheckedMembersTests expects of its scanner.
            Assert.Equal("Keys::Mixed(System.Int32&,System.Int32[,],System.Collections.Generic.List`1<System.Int32>[],System.Single&)",
                LifetimeChecks.Key(method));
        }

        const string Edited = @"
using UnityEngine;
public static class Game
{
    public static int Width(Texture2D t) => t.width;
    public static int Other(Texture2D t) => 0;
}";

        /// <summary>A hot-reload edit of <see cref="Edited"/> (one delta) against the instrumented
        /// image: its IL as redirected, the delta's own IL, or null when it needs a restart.</summary>
        static (byte[]? Redirected, byte[] Original) Edit(string before, string after)
        {
            var original = WideFloatsTests.Compilation(Edited, OptimizationLevel.Debug, "EditedGame", engine: true);
            var (image, pdb) = WideFloatsTests.Emit(original);
            var loaded = LifetimeChecks.Instrument(image);
            var session = new Unbound.Compiler.EditAndContinue();
            session.Begin("EditedGame", original, loaded, pdb);
            var current = WideFloatsTests.Compilation(Edited.Replace(before, after), OptimizationLevel.Debug, "EditedGame", engine: true);
            var delta = session.Emit("EditedGame", current);
            Assert.NotNull(delta);
            return (LifetimeChecks.RedirectDelta(loaded, new[] { delta!.Metadata }, delta.IL), delta.IL);
        }

        [Fact]
        public void HotReloadEditsCallThroughTheLoadedChecksOrRestart()
        {
            // A listed member the image already checks: the edited call goes through that check.
            var (redirected, original) = Edit("Other(Texture2D t) => 0", "Other(Texture2D t) => t.width + 1");
            Assert.NotNull(redirected);
            Assert.NotEqual(original, redirected);
            // A listed member without a checked call in the image: a restart.
            Assert.Null(Edit("Other(Texture2D t) => 0", "Other(Texture2D t) => t.height").Redirected);
            // An unlisted member: the delta as it is.
            var (unlisted, own) = Edit("Other(Texture2D t) => 0", "Other(Texture2D t) => t.GetInstanceID()");
            Assert.Equal(own, unlisted);
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
