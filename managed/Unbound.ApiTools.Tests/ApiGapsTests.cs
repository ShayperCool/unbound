using System;
using System.IO;
using System.Linq;
using Xunit;

namespace Unbound.ApiTools.Tests;

public class ApiGapsTests
{
    const string UnityCore = """
        namespace UnityEngine
        {
            public class Object
            {
                public string name { get; set; }
                public static void Destroy(Object o) {}
                public override string ToString() => "";
            }
            public class Component : Object
            {
                public Transform transform => null;
                public T GetComponent<T>() => default;
            }
            public class Transform : Component { public Vector3 position { get; set; } }
            public struct Vector3
            {
                public float x, y, z;
                public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
            }
            public class Behaviour : Component { public bool enabled { get; set; } }
            public class MonoBehaviour : Behaviour {}
            public abstract class StateLike : Object { public virtual void OnEnter(int layer) {} }
            public interface ISerializationCallbackReceiver { void OnBeforeSerialize(); void OnAfterDeserialize(); }
            public class UnityEvent<T> { public void AddListener(System.Action<T> call) {} }
        }
        """;

    /// <summary>Unbound's implementation so far: a subset of the API above, and a generated stub.</summary>
    const string Implementation = """
        namespace Unbound { internal sealed class StubAttribute : System.Attribute {} }
        namespace UnityEngine
        {
            public class Object
            {
                public string name { get; set; }
                [Unbound.Stub] public static void Destroy(Object o) {}
            }
            public class Component : Object
            {
                public Transform transform => null;
                public T GetComponent<T>() => default;
            }
            public class Transform : Component { public Vector3 position => default; }
            public struct Vector3
            {
                public float x, y;
                public Vector3(float x, float y, float z) { this.x = x; this.y = y; }
            }
            public class Behaviour : Component { public bool enabled { get; set; } }
            public class MonoBehaviour : Behaviour {}
            public abstract class StateLike : Object {}
            public interface ISerializationCallbackReceiver { void OnBeforeSerialize(); }
            public class UnityEvent<T> { public void AddListener(System.Action<T> call) {} }
        }
        """;

    const string Game = """
        using UnityEngine;

        public class Player : MonoBehaviour, ISerializationCallbackReceiver
        {
            public void Update()
            {
                var p = transform.position;
                transform.position = new Vector3(1, 2, p.z);
                GetComponent<Rigidbody>().AddForce(p);
                GetComponent<Rigidbody>();
                Object.Destroy(this);
                enabled = false;
                new UnityEvent<int>().AddListener(i => {});
                GetComponent<UnityEngine.UI.Image>().Fill();
            }
            public void OnBeforeSerialize() {}
            public void OnAfterDeserialize() {}
        }

        public class State : StateLike
        {
            public override void OnEnter(int layer) {}
            public override string ToString() => "state";
        }
        """;

    [Fact]
    public void ReportsWhatTheImplementationLacks()
    {
        var root = TestAssemblies.TempDir();
        try
        {
            var unity = Path.Combine(root, "Managed", "UnityEngine");
            var core = TestAssemblies.Compile(unity, "UnityEngine.CoreModule", UnityCore);
            var physics = TestAssemblies.Compile(unity, "UnityEngine.PhysicsModule",
                "namespace UnityEngine { public class Rigidbody : Component { public void AddForce(Vector3 force) {} } }", core);
            // A package compiled from source whose name looks like a module's: not the engine.
            var ui = TestAssemblies.Compile(Path.Combine(root, "game"), "UnityEngine.UI",
                "namespace UnityEngine.UI { public class Image : UnityEngine.MonoBehaviour { public void Fill() {} } }", core);
            var game = TestAssemblies.Compile(Path.Combine(root, "game"), "Game", Game, core, physics, ui);
            var implementation = TestAssemblies.Compile(Path.Combine(root, "impl"), "UnityEngine.CoreModule", Implementation);
            var native = Path.Combine(root, "game", "native.dll");
            File.WriteAllBytes(native, new byte[] { 1, 2, 3 });

            var report = ApiGaps.Analyze(new[] { game, ui, native }, implementation, unity);

            Assert.Equal(new[] { "Game", "UnityEngine.UI" }, report.Assemblies.Order(StringComparer.Ordinal));
            Assert.Single(report.Skipped);

            var missingType = Assert.Single(report.MissingTypes);
            Assert.Equal("UnityEngine.Rigidbody", missingType.Name);
            Assert.Equal("UnityEngine.PhysicsModule", missingType.Module);
            Assert.Equal(new[] { "Game" }, missingType.Assemblies);
            Assert.True(missingType.Uses >= 2); // the reference and its AddForce call

            var missing = report.MissingMembers
                .SelectMany(g => g.Members.Select(m => (g.Type, m.Kind, m.Signature)))
                .OrderBy(m => m.Type, StringComparer.Ordinal).ThenBy(m => m.Signature, StringComparer.Ordinal)
                .ToList();
            Assert.Equal(new[]
            {
                ("UnityEngine.ISerializationCallbackReceiver", "interface", "OnAfterDeserialize() : System.Void"),
                ("UnityEngine.Object", "method", "static Destroy(UnityEngine.Object) : System.Void"),
                ("UnityEngine.Rigidbody", "method", "AddForce(UnityEngine.Vector3) : System.Void"),
                ("UnityEngine.StateLike", "override", "OnEnter(System.Int32) : System.Void"),
                ("UnityEngine.Transform", "method", "set_position(UnityEngine.Vector3) : System.Void"),
                ("UnityEngine.Vector3", "field", "z : System.Single"),
            }, missing);
            Assert.True(report.MissingMembers.Single(g => g.Type == "UnityEngine.Rigidbody").TypeMissing);
            // A stub exists but counts as missing.
            Assert.True(report.MissingMembers.Single(g => g.Type == "UnityEngine.Object").Members.Single().Stub);
            Assert.Equal(1, report.Members.Stubbed);
            Assert.False(report.MissingMembers.Single(g => g.Type == "UnityEngine.Transform").TypeMissing);

            // Satisfied, among others: get_transform, get_position, the Vector3 constructor,
            // GetComponent<T>, set_enabled, UnityEvent<T>'s constructor and AddListener,
            // OnBeforeSerialize; ToString overrides System.Object's slot and is not checked.
            Assert.True(report.Members.Satisfied >= 7, $"satisfied {report.Members.Satisfied}");
            Assert.Equal(6, report.Members.Missing);
            Assert.Equal(report.Members.Referenced, report.Members.Satisfied + report.Members.Missing);
            Assert.Equal(1, report.Types.Missing);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
