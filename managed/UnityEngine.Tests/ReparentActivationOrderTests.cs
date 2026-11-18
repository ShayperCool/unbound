using System;
using System.Collections.Generic;
using UnityEngine.SceneManagement;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>A SetParent that changes an object's activity runs OnDisable or OnEnable before
    /// OnTransformParentChanged, the object already in its new parent's scene (matches the Unity
    /// 6000.0.71f1 Editor, tests/projects/localphysics reparent.setParent, reparent.sameScene and
    /// reparent.toActive).</summary>
    public sealed class ReparentActivationOrderTests : IDisposable
    {
        static readonly List<string> s_Events = new List<string>();
        readonly List<GameObject> m_Made = new List<GameObject>();
        readonly List<Scene> m_Scenes = new List<Scene>();
        static int s_Created;

        sealed class Recorder : MonoBehaviour
        {
            void Add(string message) => s_Events.Add(message + "@" + gameObject.scene.name);
            void OnDisable() => Add("OnDisable");
            void OnEnable() => Add("OnEnable");
            void OnTransformParentChanged() => Add("OnTransformParentChanged");
        }

        public void Dispose()
        {
            foreach (var go in m_Made)
                if (go != null) Object.DestroyImmediate(go);
            foreach (var scene in m_Scenes)
                if (scene.isLoaded) SceneManager.UnloadSceneAsync(scene);
            s_Events.Clear();
        }

        GameObject Made(string name)
        {
            var go = new GameObject(name);
            m_Made.Add(go);
            return go;
        }

        [Fact]
        public void UnderAnInactiveParentOfAnotherSceneOnDisableComesFirstInTheNewScene()
        {
            var scene = SceneManager.CreateScene("ReparentOrder" + s_Created++);
            m_Scenes.Add(scene);
            var parent = Made("InactiveParent");
            SceneManager.MoveGameObjectToScene(parent, scene);
            parent.SetActive(false);
            var child = Made("Child");
            child.AddComponent<Recorder>();
            s_Events.Clear();
            child.transform.SetParent(parent.transform, true);
            Assert.Equal(new[] { "OnDisable@" + scene.name, "OnTransformParentChanged@" + scene.name }, s_Events);
        }

        [Fact]
        public void InOneSceneTheActivityChangesBeforeTheParentMessage()
        {
            var inactive = Made("InactiveParent");
            inactive.SetActive(false);
            var active = Made("ActiveParent");
            var child = Made("Child");
            child.AddComponent<Recorder>();
            string here = child.scene.name;
            s_Events.Clear();
            child.transform.SetParent(inactive.transform, true);
            child.transform.SetParent(active.transform, true);
            Assert.Equal(new[]
            {
                "OnDisable@" + here, "OnTransformParentChanged@" + here,
                "OnEnable@" + here, "OnTransformParentChanged@" + here,
            }, s_Events);
            // A move that keeps the activity sends the message alone.
            s_Events.Clear();
            child.transform.SetParent(null, true);
            Assert.Equal(new[] { "OnTransformParentChanged@" + here }, s_Events);
        }
    }
}
