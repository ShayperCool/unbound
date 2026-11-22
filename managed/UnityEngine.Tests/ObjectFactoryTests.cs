using System;
using System.Collections.Generic;
using UnityEditor;
using Xunit;

namespace UnityEngine.Tests
{
    [RequireComponent(typeof(Rigidbody))]
    public class FactoryNeedsBody : MonoBehaviour { }

    [DisallowMultipleComponent]
    public class FactoryOnlyOne : MonoBehaviour { }

    /// <summary>ObjectFactory, one-of-a-kind engine components and undo group names as the Unity
    /// 6000.0.71f1 Editor has them in Play mode (tests/projects/objectfactory,
    /// tests/projects/uniquecomponents, tests/projects/editor).</summary>
    [Collection("Frames")]
    public sealed class ObjectFactoryTests
    {
        [Fact]
        public void AddedComponentsRaiseTheEventRequiredOnesFirstAndTakeTheHideFlags()
        {
            var added = new List<string>();
            Action<Component> onAdded = c => added.Add(c.GetType().Name);
            ObjectFactory.componentWasAdded += onAdded;
            var holder = new GameObject("Scene probe");
            GameObject go = null;
            try
            {
                go = ObjectFactory.CreateGameObject(holder.scene, HideFlags.DontSave, "C", typeof(Light));
                Assert.Equal(new[] { "Transform", "Light" }, added);
                added.Clear();
                var body = ObjectFactory.AddComponent<FactoryNeedsBody>(go);
                Assert.Equal(new[] { "Rigidbody", "FactoryNeedsBody" }, added);
                Assert.Equal(HideFlags.DontSave, body.hideFlags);
                Assert.Equal(HideFlags.DontSave, go.GetComponent<Rigidbody>().hideFlags);
            }
            finally
            {
                ObjectFactory.componentWasAdded -= onAdded;
                if (go != null) Object.DestroyImmediate(go);
                Object.DestroyImmediate(holder);
            }
        }

        [Fact]
        public void ASecondComponentOfAOneOfAKindTypeThrowsWithoutALog()
        {
            var go = new GameObject("A");
            try
            {
                var logged = new List<string>();
                Application.LogCallback onLog = (message, stack, type) => logged.Add(type + ": " + message);
                Application.logMessageReceived += onLog;
                try
                {
                ObjectFactory.AddComponent<Rigidbody>(go);
                var native = Assert.Throws<ArgumentException>(() => ObjectFactory.AddComponent<Rigidbody>(go));
                Assert.Equal("Can't add component 'Rigidbody' to A because such a component is already added to the game object!", native.Message);
                ObjectFactory.AddComponent<FactoryOnlyOne>(go);
                var script = Assert.Throws<ArgumentException>(() => ObjectFactory.AddComponent<FactoryOnlyOne>(go));
                Assert.Equal("Can't add '<NULL>' to A because a 'FactoryOnlyOne' is already added to the game object!", script.Message);
                }
                finally { Application.logMessageReceived -= onLog; }
                Assert.Empty(logged);
                // Colliders are not one of a kind.
                ObjectFactory.AddComponent<BoxCollider>(go);
                ObjectFactory.AddComponent<BoxCollider>(go);
                Assert.Equal(2, go.GetComponents<BoxCollider>().Length);
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Fact]
        public void GameObjectAddComponentRefusesASecondOneOfAKindComponent()
        {
            var go = new GameObject("U");
            try
            {
                var logged = new List<string>();
                Application.LogCallback onLog = (message, stack, type) => logged.Add(type + ": " + message);
                Application.logMessageReceived += onLog;
                try
                {
                    Assert.NotNull(go.AddComponent<Camera>());
                    Assert.Null(go.AddComponent<Camera>());
                }
                finally { Application.logMessageReceived -= onLog; }
                Assert.Single(go.GetComponents<Camera>());
                Assert.Equal(new[] { "Log: Can't add component 'Camera' to U because such a component is already added to the game object!" }, logged);
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Fact]
        public void AGroupIsNamedByItsLatestNamedRecordingAndKeepsItWhenCollapsed()
        {
            Undo.IncrementCurrentGroup();
            var a = ObjectFactory.CreateGameObject("First");
            var b = ObjectFactory.CreateGameObject("Second");
            // An added component does not name the group.
            Undo.AddComponent<BoxCollider>(b);
            Assert.Equal("Create Second", Undo.GetCurrentGroupName());
            int group = Undo.GetCurrentGroup();
            Undo.IncrementCurrentGroup();
            Undo.RecordObject(b.transform, "Later");
            Undo.CollapseUndoOperations(group);
            var names = new List<string>();
            Undo.UndoRedoEventCallback onUndo = (in UndoRedoInfo info) => names.Add(info.undoName);
            Undo.undoRedoEvent += onUndo;
            try { Undo.PerformUndo(); }
            finally { Undo.undoRedoEvent -= onUndo; }
            // The collapsed group keeps the name of the group it was collapsed into.
            Assert.Equal(new[] { "Create Second" }, names);
            Assert.True(a == null && b == null);
        }
    }
}
