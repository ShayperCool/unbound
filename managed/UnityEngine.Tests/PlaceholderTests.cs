using System;
using Unbound.Runtime;
using UnityEditor;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>
    /// The Editor's placeholder objects (Unity 6000.0.71f1, tests/projects/objecterrors): unassigned
    /// serialized references of script objects read from data, and components GetComponent does not
    /// find, compare equal to null without being null references and throw their own exception.
    /// </summary>
    public sealed class PlaceholderTests
    {
        public sealed class Sample : ScriptableObject
        {
            public GameObject go;
            public Transform t;
            public MonoBehaviour behaviour;
            public ScriptableObject scriptable;
            public GameObject[] items;
            public Inner inner;
            [Serializable] public class Inner { public GameObject target; }
        }

        static Sample Read()
        {
            var sample = ScriptableObject.CreateInstance<Sample>();
            new YamlReader(null).ReadScriptObject(sample, TestYaml.DocumentBody(@"
MonoBehaviour:
  go: {fileID: 0}
  t: {fileID: 0}
  behaviour: {fileID: 0}
  scriptable: {fileID: 0}
  items:
  - {fileID: 0}
  inner:
    target: {fileID: 0}
"));
            return sample;
        }

        [Fact]
        public void UnassignedReferencesOfEngineClassesReadAsPlaceholders()
        {
            var sample = Read();
            try
            {
                Assert.True(sample.go == null);
                Assert.False(ReferenceEquals(sample.go, null));
                Assert.Same(sample.go, sample.go ?? new GameObject("unused"));
                Assert.IsType<GameObject>(sample.go);
                Assert.Equal(0, sample.go.GetInstanceID());
                Assert.Equal("The variable go of Sample has not been assigned.\nYou probably need to assign the go variable of the Sample script in the inspector.",
                    Assert.IsType<UnassignedReferenceException>(ObjectErrors.Receiver(sample.go)).Message);
                Assert.IsType<Transform>(sample.t);
                // Script classes stay null references.
                Assert.True(ReferenceEquals(sample.behaviour, null));
                Assert.True(ReferenceEquals(sample.scriptable, null));
                // An item names its array; a nested class's reference has no message.
                Assert.Contains("The variable items of Sample", ObjectErrors.Receiver(sample.items[0]).Message);
                Assert.False(ReferenceEquals(sample.inner.target, null));
                Assert.IsType<NullReferenceException>(ObjectErrors.Receiver(sample.inner.target));
                // Written out, a placeholder is no reference.
                Assert.Contains("\"go\":{\"instanceID\":0}", JsonUtility.ToJson(sample));
                Assert.Contains("go: {fileID: 0}", UnityYamlWriter.Write(new Object[] { sample }));
            }
            finally { Object.DestroyImmediate(sample); }
        }

        public sealed class Styled : ScriptableObject
        {
            public GUIStyle style;
            public Rigidbody keyless;
        }

        [Fact]
        public void EngineClassesKeepNullReferencesAndUnwrittenFieldsGetPlaceholders()
        {
            var styled = ScriptableObject.CreateInstance<Styled>();
            try
            {
                new YamlReader(null).ReadScriptObject(styled, TestYaml.DocumentBody(@"
MonoBehaviour:
  style:
    m_Name: s
    m_Normal:
      m_Background: {fileID: 0}
    m_Font: {fileID: 0}
"));
                // GUIStyle's own data is the engine's: its references stay null.
                Assert.True(ReferenceEquals(styled.style.normal.background, null));
                Assert.True(ReferenceEquals(styled.style.font, null));
                // A field the data does not name is unassigned all the same.
                Assert.False(ReferenceEquals(styled.keyless, null));
                Assert.Contains("The variable keyless of Styled", ObjectErrors.Receiver(styled.keyless).Message);
            }
            finally { Object.DestroyImmediate(styled); }
        }

        [Fact]
        public void SerializedPropertiesReadAPlaceholderAsNoReference()
        {
            var sample = Read();
            try
            {
                using var so = new SerializedObject(sample);
                Assert.True(ReferenceEquals(so.FindProperty("go").objectReferenceValue, null));
            }
            finally { Object.DestroyImmediate(sample); }
        }

        [Fact]
        public void AnObjectMadeAtRuntimeHasNullReferences()
        {
            var sample = ScriptableObject.CreateInstance<Sample>();
            try { Assert.True(ReferenceEquals(sample.go, null)); }
            finally { Object.DestroyImmediate(sample); }
        }

        public interface INone { }

        [Fact]
        public void GetComponentAnswersAPlaceholderForAnEngineComponentTheObjectLacks()
        {
            var go = new GameObject("Holder");
            try
            {
                var rigidbody = Lifetime.MissingComponent<Rigidbody>(null, go);
                Assert.False(ReferenceEquals(rigidbody, null));
                Assert.True(rigidbody == null);
                Assert.Equal("There is no 'Rigidbody' attached to the \"Holder\" game object, but a script is trying to access it.\n" +
                             "You probably need to add a Rigidbody to the game object \"Holder\". Or your script needs to check if the component is attached before using it.",
                    Assert.IsType<MissingComponentException>(ObjectErrors.Receiver(rigidbody)).Message);
                Assert.IsType<Collider>(Lifetime.MissingComponent(null, go.transform, typeof(Collider)));
                Assert.IsType<Rigidbody>(Lifetime.MissingComponentNamed(null, go, "Rigidbody"));
                // A found component, a script class, an interface and an unknown name answer as they are.
                Assert.Same(go.transform, Lifetime.MissingComponent<Transform>(go.transform, go));
                Assert.Null(Lifetime.MissingComponent<ObjectErrorsTests.Probe>(null, go));
                Assert.Null(Lifetime.MissingComponent<INone>(null, go));
                Assert.Null(Lifetime.MissingComponentNamed(null, go, "NoSuchComponent"));
            }
            finally { Object.DestroyImmediate(go); }
        }
    }
}
