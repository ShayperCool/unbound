using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Unbound.Runtime;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>
    /// The object API's errors as the Unity 6000.0.71f1 Editor gives them (tests/projects/objecterrors):
    /// what is thrown, what is logged (and with which type) and what is answered, with a control on
    /// a release without the measurement where the earlier answer stays.
    /// </summary>
    public sealed class ObjectErrorsTests : IDisposable
    {
        readonly List<Object> m_Owned = new List<Object>();
        readonly List<string> m_Logs = new List<string>();

        public ObjectErrorsTests() => Application.logMessageReceived += Record;

        public void Dispose()
        {
            Application.logMessageReceived -= Record;
            foreach (var o in m_Owned) if (o != null) Object.DestroyImmediate(o);
        }

        void Record(string message, string stack, LogType type) => m_Logs.Add(type + ": " + message);

        GameObject Make(string name)
        {
            var go = new GameObject(name);
            m_Owned.Add(go);
            return go;
        }

        /// <summary>The logs an action writes.</summary>
        string[] Logs(Action action)
        {
            m_Logs.Clear();
            action();
            return m_Logs.ToArray();
        }

        public sealed class Probe : MonoBehaviour { }
        public sealed class Asset : ScriptableObject { }
        public abstract class AbstractAsset : ScriptableObject { }
        public interface INone { }

        [Fact]
        public void AddComponentRefusesWhatCannotBeAddedWithTheEnginesDiagnostic()
        {
            using var release = TestRelease.Pin(TestRelease.Measured);
            var go = Make("Holder");
            go.AddComponent<Probe>();
            Assert.Equal(new[] { "Warning: AddComponent asking for invalid type" }, Logs(() => Assert.Null(go.AddComponent((Type)null))));
            Assert.Equal(new[] { "Warning: AddComponent asking for \"String\" which is not a Unity engine type." },
                Logs(() => Assert.Null(go.AddComponent(typeof(string)))));
            Assert.Equal(new[] { "Log: Can't add component because 'GameObject' is not derived from Component." },
                Logs(() => Assert.Null(go.AddComponent(typeof(GameObject)))));
            Assert.Equal(new[] { "Warning: AddComponent asking for invalid type using [ExtensionOfNativeClass] in its ancestor but cannot be found in the assembly" },
                Logs(() => Assert.Null(go.AddComponent(typeof(Asset)))));
            foreach (var type in new[] { typeof(Component), typeof(Behaviour), typeof(Transform) })
                Assert.Equal(new[] { $"Log: Can't add component '{type.Name}' to Holder because such a component is already added to the game object!" },
                    Logs(() => Assert.Null(go.AddComponent(type))));
            // Without a collider the abstract class is refused as abstract.
            Assert.Equal(new[] { "Log: Cannot add component of type 'Collider' because it is abstract. Add component of type that is derived from 'Collider' instead." },
                Logs(() => Assert.Null(go.AddComponent(typeof(Collider)))));
        }

        [Fact]
        public void AnUnmeasuredReleaseKeepsTheEarlierAddComponentExceptions()
        {
            using var release = TestRelease.Pin(TestRelease.Unmeasured);
            var go = Make("Holder");
            Assert.Throws<ArgumentNullException>(() => go.AddComponent((Type)null));
            Assert.Throws<ArgumentException>(() => go.AddComponent(typeof(string)));
        }

        [Fact]
        public void ComponentQueriesRefuseNoTypeAndTypesThatAreNotComponents()
        {
            var go = Make("Holder");
            const string NotComponent = "GetComponent requires that the requested component 'String' derives from MonoBehaviour or Component or is an interface.";
            Assert.Equal("Type cannot be null.", Assert.Throws<ArgumentException>(() => go.GetComponent((Type)null)).Message);
            Assert.Equal(NotComponent, Assert.Throws<ArgumentException>(() => go.GetComponent(typeof(string))).Message);
            Assert.Equal(NotComponent, Assert.Throws<ArgumentException>(() => go.GetComponent<string>()).Message);
            Assert.Equal("GetComponent requires that the requested component 'GameObject' derives from MonoBehaviour or Component or is an interface.",
                Assert.Throws<ArgumentException>(() => go.GetComponent(typeof(GameObject))).Message);
            Assert.Equal(NotComponent, Assert.Throws<ArgumentException>(() => go.GetComponents(typeof(string))).Message);
            Assert.Equal(NotComponent, Assert.Throws<ArgumentException>(() => go.GetComponentInChildren(typeof(string))).Message);
            foreach (Action query in new Action[]
            {
                () => go.GetComponentInChildren(null), () => go.GetComponentInParent(null), () => go.GetComponentsInChildren(null),
                () => go.GetComponentsInParent(null), () => go.TryGetComponent(null, out _), () => go.GetComponents((Type)null),
            })
                Assert.Equal("Type cannot be null.", Assert.Throws<ArgumentException>(query).Message);
            // An interface is a valid query; no list is no answer.
            Assert.Null(go.GetComponent<INone>());
            go.GetComponents<Transform>(null);
        }

        [Fact]
        public void TagsLogUndefinedNamesExceptInLookups()
        {
            var go = Make("Tagged");
            Assert.Equal(new[] { "Error: Tag: NoSuchTag is not defined." }, Logs(() => go.tag = "NoSuchTag"));
            Assert.Equal("Untagged", go.tag);
            Assert.Equal(new[] { "Error: Tag: tag name is null or empty." }, Logs(() => go.tag = null));
            Assert.Equal(new[] { "Error: Tag: NoSuchTag is not defined." }, Logs(() => Assert.False(go.CompareTag("NoSuchTag"))));
            Assert.Equal(new[] { "Error: Tag: tag name is null or empty." }, Logs(() => Assert.False(go.CompareTag(""))));
            Assert.Empty(Logs(() => Assert.True(go.CompareTag("Untagged"))));
            Assert.Equal("Tag: tag name is null or empty.", Assert.Throws<ArgumentException>(() => GameObject.FindWithTag(null)).Message);
            Assert.Equal("Tag: tag name is null or empty.", Assert.Throws<ArgumentException>(() => GameObject.FindGameObjectsWithTag("")).Message);
            Assert.Equal("Tag: NoSuchTag is not defined.", Assert.Throws<UnityException>(() => GameObject.FindWithTag("NoSuchTag")).Message);
        }

        [Fact]
        public void CreateInstanceAnswersEachKindOfType()
        {
            Assert.Empty(Logs(() => Assert.Null(ScriptableObject.CreateInstance((Type)null))));
            Assert.Throws<InvalidCastException>(() => ScriptableObject.CreateInstance(typeof(Probe)));
            Assert.Equal(new[]
            {
                "Assert: 'System.String' is missing the class attribute 'ExtensionOfNativeClass'!",
                "Warning: The class named 'System.String' is not derived from MonoBehaviour or ScriptableObject!",
            }, Logs(() => Assert.Null(ScriptableObject.CreateInstance(typeof(string)))));
            string name = typeof(AbstractAsset).FullName;
            Assert.Equal(new[] { $"Warning: The class named '{name}' is abstract. The script class can't be abstract!",
                    $"Warning: The class named '{name}' is abstract. The script class can't be abstract!" },
                Logs(() => Assert.Null(ScriptableObject.CreateInstance(typeof(AbstractAsset)))));
        }

        [Fact]
        public void FindersLogTypesThatAreNotObjects()
        {
            Assert.Equal(new[] { "Error: FindAllObjectsOfType: Invalid Type" },
                Logs(() => Assert.Null(Object.FindObjectsByType(null, FindObjectsSortMode.None))));
            Assert.Equal(new[] { "Error: FindAllObjectsOfType: The type has to be derived from UnityEngine.Object. Type is String." },
                Logs(() => Assert.Null(Object.FindObjectsByType(typeof(string), FindObjectsSortMode.None))));
            // The single-object finder reads the missing array: the Mono runtime's null reference.
            Assert.Equal("Object reference not set to an instance of an object",
                Assert.Throws<NullReferenceException>(() => Object.FindFirstObjectByType(null)).Message);
        }

        [Fact]
        public void ADeadObjectThrowsItsOwnMessageAsAReceiverAndAnArgument()
        {
            var doomed = Make("Doomed");
            Object.DestroyImmediate(doomed);
            const string Destroyed = "The object of type 'UnityEngine.GameObject' has been destroyed but you are still trying to access it.\n" +
                                     "Your script should either check if it is null or you should not destroy the object.";
            Assert.Equal(Destroyed, Assert.Throws<MissingReferenceException>(() => doomed.hideFlags).Message);
            Assert.Equal(Destroyed, Assert.Throws<MissingReferenceException>(() => doomed.CompareTag("Untagged")).Message);
            Assert.Equal(Destroyed + " Parameter name: data", Assert.Throws<MissingReferenceException>(() => Object.Instantiate(doomed)).Message);
            Assert.Equal(Destroyed + " Parameter name: target", Assert.Throws<MissingReferenceException>(() => Object.DontDestroyOnLoad(doomed)).Message);
            Assert.Equal("The Object you want to instantiate is null.", Assert.Throws<ArgumentException>(() => Object.Instantiate((Object)null)).Message);

            // An object the engine never made reads its error string: "Kind:message".
            var placeholder = (GameObject)RuntimeHelpers.GetUninitializedObject(typeof(GameObject));
            placeholder.m_UnityRuntimeErrorString = "UnassignedReferenceException:The variable v of C has not been assigned.";
            Assert.IsType<UnassignedReferenceException>(ObjectErrors.Receiver(placeholder));
            Assert.Equal("The variable v of C has not been assigned. Parameter name: data", ObjectErrors.Argument(placeholder, "data").Message);
            placeholder.m_UnityRuntimeErrorString = null;
            Assert.IsType<NullReferenceException>(ObjectErrors.Receiver(placeholder));
            Assert.Equal("data", Assert.IsType<ArgumentNullException>(ObjectErrors.Argument(placeholder, "data")).ParamName);
        }

        [Fact]
        public void TheReviewedEdgesOfTheArgumentChecks()
        {
            // Measured on 6000.0 (other families keep their earlier, unmeasured behaviour).
            using var release = TestRelease.Pin(TestRelease.Measured);
            // The managed checks of an overload come before the engine's dead-object check
            // (the release's reference assemblies): a ScriptableObject with a pose is refused first.
            var asset = ScriptableObject.CreateInstance<Asset>();
            Object.DestroyImmediate(asset);
            Assert.Equal("Cannot instantiate a ScriptableObject with a position and rotation",
                Assert.Throws<ArgumentException>(() => Object.Instantiate(asset, Vector3.zero, Quaternion.identity)).Message);
            Assert.Equal("target", Assert.Throws<ArgumentNullException>(() => Object.DontDestroyOnLoad(null)).ParamName);
            var doomed = Make("Doomed");
            Object.DestroyImmediate(doomed);
            Assert.Throws<MissingReferenceException>(() => doomed.hideFlags = HideFlags.DontSave);
            var go = Make("Lists");
            go.GetComponentsInChildren<Transform>(false, null);
            go.GetComponentsInParent<Transform>(false, null);
            // A second RectTransform is "already added" (tests/projects/objectfactory addComponent.rectTwice).
            var rect = Make("Rect");
            rect.AddComponent<RectTransform>();
            Assert.Equal(new[] { "Log: Can't add component 'RectTransform' to Rect because such a component is already added to the game object!" },
                Logs(() => Assert.Null(rect.AddComponent<RectTransform>())));
        }

        [Fact]
        public void ParentingToItselfChangesNothingQuietly()
        {
            var go = Make("Self");
            Assert.Empty(Logs(() => go.transform.SetParent(go.transform)));
            Assert.Null(go.transform.parent);
        }
    }
}
