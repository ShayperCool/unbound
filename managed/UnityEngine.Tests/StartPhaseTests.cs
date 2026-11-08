using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Unbound.Runtime;
using Xunit;
using PL = UnityEngine.PlayerLoop;

namespace UnityEngine.Tests
{
    /// <summary>Default-loop Start boundaries measured by the phase markers in instantiateasync
    /// and the Update/coroutine/LateUpdate ordering in lifecycle, plus editorcallbacks/SceneLoop's
    /// coroutine/LateUpdate-created Start-only controls. These tests run only the driver's
    /// and Lifecycle's actions at the selected nodes, without rendering, clocks or async schedulers.</summary>
    [Collection("Frames")]
    public class StartPhaseTests
    {
        const BindingFlags PrivateStatic = BindingFlags.NonPublic | BindingFlags.Static;
        const BindingFlags PrivateInstance = BindingFlags.NonPublic | BindingFlags.Instance;
        static readonly List<string> Calls = new List<string>();

        public class Actor : MonoBehaviour
        {
            void Awake() => Calls.Add(name + ".Awake");
            void OnEnable() => Calls.Add(name + ".OnEnable");
            void Start() => Calls.Add(name + ".Start");
            void Update() => Calls.Add(name + ".Update");
            void LateUpdate() => Calls.Add(name + ".LateUpdate");
        }

        public class StartOnlyActor : MonoBehaviour
        {
            void Awake() => Calls.Add(name + ".Awake");
            void OnEnable() => Calls.Add(name + ".OnEnable");
            void Start() => Calls.Add(name + ".Start");
        }

        public class LateCreator : MonoBehaviour
        {
            internal Action Next;
            void LateUpdate()
            {
                var next = Next;
                Next = null;
                next?.Invoke();
            }
        }

        public class Creator : MonoBehaviour
        {
            internal Action Next;
            void Update()
            {
                Calls.Add("creator.Update");
                var next = Next;
                Next = null;
                next?.Invoke();
            }
        }

        static Dictionary<Type, List<Action>> Engine =>
            (Dictionary<Type, List<Action>>)typeof(PlayerLoopDriver).GetField("s_Engine", PrivateStatic).GetValue(null);

        static void RunDriverNode(Type node)
        {
            foreach (var action in Engine[node])
            {
                var owner = action.Method.DeclaringType;
                if (owner == typeof(Lifecycle) || owner == typeof(PlayerLoopDriver) || owner?.DeclaringType == typeof(PlayerLoopDriver))
                    action();
            }
        }

        // The assembly also contains tests that leave behaviours pending. Temporarily empty the
        // dispatch collections rather than running their callbacks or resetting their objects.
        // Restore the same collections, their contents and scalar counters after destroying ours.
        sealed class CollectionState
        {
            readonly object m_Collection;
            readonly object[] m_Items;
            readonly MethodInfo m_Clear;
            readonly MethodInfo m_Add;

            internal CollectionState(object collection)
            {
                m_Collection = collection;
                m_Items = ((IEnumerable)collection).Cast<object>().ToArray();
                m_Clear = collection.GetType().GetMethod("Clear", Type.EmptyTypes);
                m_Add = collection.GetType().GetMethod("Add");
                m_Clear.Invoke(collection, null);
            }

            internal void Restore()
            {
                m_Clear.Invoke(m_Collection, null);
                foreach (var item in m_Items) m_Add.Invoke(m_Collection, new[] { item });
            }
        }

        sealed class Scope : IDisposable
        {
            readonly List<GameObject> m_Objects = new List<GameObject>();
            readonly List<CollectionState> m_Collections = new List<CollectionState>();
            readonly List<(object List, FieldInfo Field, object Value)> m_Removed = new List<(object, FieldInfo, object)>();
            readonly FieldInfo m_Sequence = typeof(Lifecycle).GetField("s_Sequence", PrivateStatic);
            readonly object m_OldSequence;
            readonly int m_Frame = Time.s_FrameCount;
            readonly string[] m_Calls = Calls.ToArray();

            internal Scope()
            {
                _ = Engine; // Complete the driver's registrations before isolating its queues.
                m_OldSequence = m_Sequence.GetValue(null);
                foreach (string name in new[] { "s_Update", "s_LateUpdate", "s_FixedUpdate" })
                {
                    var list = typeof(Lifecycle).GetField(name, PrivateStatic).GetValue(null);
                    foreach (string field in new[] { "m_Items", "m_Members" })
                        m_Collections.Add(new CollectionState(list.GetType().GetField(field, PrivateInstance).GetValue(list)));
                    var removed = list.GetType().GetField("m_Removed", PrivateInstance);
                    m_Removed.Add((list, removed, removed.GetValue(list)));
                    removed.SetValue(list, 0);
                }
                foreach (string name in new[] { "s_PendingStart", "s_OnGUI", "s_OnRenderObject" })
                    Save(typeof(Lifecycle), name);
                Save(typeof(Coroutines), "s_Running");
                Save(typeof(Invokes), "s_Entries");
                Calls.Clear();
            }

            void Save(Type type, string field) =>
                m_Collections.Add(new CollectionState(type.GetField(field, PrivateStatic).GetValue(null)));

            internal GameObject Make(string name)
            {
                var go = new GameObject(name);
                m_Objects.Add(go);
                return go;
            }

            internal GameObject Keep(GameObject go)
            {
                m_Objects.Add(go);
                return go;
            }

            public void Dispose()
            {
                try
                {
                    foreach (var go in m_Objects) Object.DestroyImmediate(go);
                }
                finally
                {
                    foreach (var collection in m_Collections) collection.Restore();
                    foreach (var saved in m_Removed) saved.Field.SetValue(saved.List, saved.Value);
                    m_Sequence.SetValue(null, m_OldSequence);
                    Time.s_FrameCount = m_Frame;
                    Calls.Clear();
                    Calls.AddRange(m_Calls);
                }
            }
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void UpdateCreatedBehavioursStartAtTheFollowingDelayedNode(bool clone)
        {
            using var scope = new Scope();
            var template = scope.Make("template");
            template.SetActive(false);
            template.AddComponent<Actor>();
            var creator = scope.Make("creator").AddComponent<Creator>();
            Actor made = null;
            creator.Next = () =>
            {
                if (clone)
                {
                    var go = scope.Keep(Object.Instantiate(template));
                    go.name = "new";
                    go.SetActive(true);
                    made = go.GetComponent<Actor>();
                }
                else made = scope.Make("new").AddComponent<Actor>();
            };

            RunDriverNode(typeof(PL.EarlyUpdate.ScriptRunDelayedStartupFrame));
            Calls.Clear();
            RunDriverNode(typeof(PL.Update.ScriptRunBehaviourUpdate));
            Assert.NotNull(made);
            Assert.False(made.didStart);
            Assert.Equal(new[] { "creator.Update", "new.Awake", "new.OnEnable" }, Calls);

            RunDriverNode(typeof(PL.Update.ScriptRunDelayedDynamicFrameRate));
            Assert.True(made.didStart);
            Assert.Equal("new.Start", Calls[Calls.Count - 1]);
            Assert.DoesNotContain("new.Update", Calls);
            RunDriverNode(typeof(PL.PreLateUpdate.ScriptRunBehaviourLateUpdate));
            Assert.Equal("new.LateUpdate", Calls[Calls.Count - 1]);
            RunDriverNode(typeof(PL.Update.ScriptRunBehaviourUpdate));
            Assert.Equal(1, Calls.Count(s => s == "new.Start"));
            Assert.Equal(1, Calls.Count(s => s == "new.Update"));
        }

        [Fact]
        public void EarlyPendingStartsStillPrecedeTheFirstUpdate()
        {
            using var scope = new Scope();
            var actor = scope.Make("early").AddComponent<Actor>();
            Assert.False(actor.didStart);
            RunDriverNode(typeof(PL.EarlyUpdate.ScriptRunDelayedStartupFrame));
            Assert.True(actor.didStart);
            RunDriverNode(typeof(PL.Update.ScriptRunBehaviourUpdate));
            Assert.Equal(new[] { "early.Awake", "early.OnEnable", "early.Start", "early.Update" }, Calls);
        }

        static IEnumerator ActivateAfterYield(GameObject go)
        {
            yield return null;
            go.SetActive(true);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void ScriptsWithoutALateUpdateVisitStartAtThePostLateScriptingPoint(bool fromLateUpdate)
        {
            using var scope = new Scope();
            string release = ProjectInfo.UnityVersion;
            ProjectInfo.UnityVersion = "6000.0.71f1";
            try
            {
                var host = scope.Make("creator").AddComponent<Creator>();
                var lateCreator = scope.Make("lateCreator").AddComponent<LateCreator>();
                var go = scope.Make("pending");
                go.SetActive(false);
                var actor = go.AddComponent<StartOnlyActor>();
                RunDriverNode(typeof(PL.EarlyUpdate.ScriptRunDelayedStartupFrame));
                if (fromLateUpdate) lateCreator.Next = () => go.SetActive(true);
                else host.StartCoroutine(ActivateAfterYield(go));
                Time.s_FrameCount++;
                Calls.Clear();
                RunDriverNode(typeof(PL.Update.ScriptRunDelayedDynamicFrameRate));
                RunDriverNode(typeof(PL.PreLateUpdate.ScriptRunBehaviourLateUpdate));
                Assert.True(actor.isActiveAndEnabled);
                Assert.False(actor.didStart);
                Assert.Equal(new[] { "pending.Awake", "pending.OnEnable" }, Calls);
                RunDriverNode(typeof(PL.PostLateUpdate.ScriptRunDelayedDynamicFrameRate));
                Assert.True(actor.didStart);
                Assert.Equal(new[] { "pending.Awake", "pending.OnEnable", "pending.Start" }, Calls);
            }
            finally { ProjectInfo.UnityVersion = release; }
        }

        [Fact]
        public void CoroutineEnabledBehavioursStillStartOnTheirLateUpdateVisit()
        {
            using var scope = new Scope();
            var host = scope.Make("creator").AddComponent<Creator>();
            var go = scope.Make("late");
            go.SetActive(false);
            var actor = go.AddComponent<Actor>();
            RunDriverNode(typeof(PL.EarlyUpdate.ScriptRunDelayedStartupFrame));
            host.StartCoroutine(ActivateAfterYield(go));
            Time.s_FrameCount++;
            Calls.Clear();
            RunDriverNode(typeof(PL.Update.ScriptRunBehaviourUpdate));
            RunDriverNode(typeof(PL.Update.ScriptRunDelayedDynamicFrameRate));
            Assert.True(actor.isActiveAndEnabled);
            Assert.False(actor.didStart);
            Assert.Equal(new[] { "creator.Update", "late.Awake", "late.OnEnable" }, Calls);
            RunDriverNode(typeof(PL.PreLateUpdate.ScriptRunBehaviourLateUpdate));
            Assert.Equal(new[] { "creator.Update", "late.Awake", "late.OnEnable", "late.Start", "late.LateUpdate" }, Calls);
        }
    }
}
