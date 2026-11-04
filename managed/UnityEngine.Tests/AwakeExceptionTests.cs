using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Unbound.Runtime;
using Xunit;

namespace UnityEngine.Tests
{
    // Unity 6000.0 MonoBehaviour.Awake documents that an exception disables the component.
    // Reentrant cleanup and manual recovery below are Unbound preservation controls, not
    // additional Native claims about callback order or exception-log timing.
    [Collection("Frames")]
    public class AwakeExceptionTests
    {
        const string Failure = "awake-exception regression sentinel";

        public class Actor : MonoBehaviour
        {
            internal bool Fail = true, ToggleBeforeFailure, DestroyBeforeFailure, DeactivateBeforeFailure;
            internal int Awakes, Enables, Starts, Updates, Lates, Fixed;
            void Awake()
            {
                Awakes++;
                if (ToggleBeforeFailure) { enabled = false; enabled = true; }
                if (DeactivateBeforeFailure) gameObject.SetActive(false);
                if (DestroyBeforeFailure) Object.DestroyImmediate(gameObject);
                if (Fail) throw new InvalidOperationException(Failure);
            }
            void OnEnable() => Enables++;
            void Start() => Starts++;
            void Update() => Updates++;
            void LateUpdate() => Lates++;
            void FixedUpdate() => Fixed++;
            void OnGUI() { }
            void OnRenderObject() { }
        }

        sealed class Logs : ILogHandler
        {
            readonly ILogHandler m_Next;
            internal readonly List<(Exception Error, Object Context)> Exceptions = new();
            internal Logs(ILogHandler next) => m_Next = next;
            public void LogFormat(LogType type, Object context, string format, params object[] args) =>
                m_Next.LogFormat(type, context, format, args);
            public void LogException(Exception error, Object context)
            {
                Exceptions.Add((error, context));
                m_Next.LogException(error, context);
            }
        }

        sealed class SavedCollection
        {
            readonly object m_Value;
            readonly object[] m_Items;
            readonly MethodInfo m_Clear, m_Add;
            internal SavedCollection(object value)
            {
                m_Value = value;
                m_Items = ((IEnumerable)value).Cast<object>().ToArray();
                m_Clear = value.GetType().GetMethod("Clear", Type.EmptyTypes) ?? throw new MissingMethodException("Clear");
                m_Add = value.GetType().GetMethod("Add") ?? throw new MissingMethodException("Add");
            }
            internal void Clear() => m_Clear.Invoke(m_Value, null);
            internal void Restore()
            {
                Clear();
                foreach (var item in m_Items) m_Add.Invoke(m_Value, new[] { item });
            }
        }

        sealed class Scope : IDisposable
        {
            const BindingFlags Static = BindingFlags.Static | BindingFlags.NonPublic;
            const BindingFlags Instance = BindingFlags.Instance | BindingFlags.NonPublic;
            readonly List<GameObject> m_Objects = new();
            readonly List<SavedCollection> m_Collections = new();
            readonly List<(object List, FieldInfo Field, object Value)> m_Removed = new();
            readonly FieldInfo m_Sequence;
            readonly object m_OldSequence, m_FixedList;
            readonly MethodInfo m_RunFixed;
            readonly bool m_Playing = EditorSession.IsPlaying;
            readonly ILogger m_Logger = Debug.unityLogger;
            readonly ILogHandler m_Handler;
            readonly bool m_LogEnabled;
            readonly LogType m_LogFilter;
            internal readonly Logs Log;

            static object Field(Type type, string name, object owner = null) =>
                (type.GetField(name, owner == null ? Static : Instance) ?? throw new MissingFieldException(name)).GetValue(owner);

            internal Scope()
            {
                // Resolve and snapshot everything before clearing anything. No failed reflection
                // can leave earlier dispatch collections cleared by a half-built scope.
                m_Sequence = typeof(Lifecycle).GetField("s_Sequence", Static) ?? throw new MissingFieldException("s_Sequence");
                m_OldSequence = m_Sequence.GetValue(null);
                foreach (string name in new[] { "s_Update", "s_LateUpdate", "s_FixedUpdate" })
                {
                    var list = Field(typeof(Lifecycle), name);
                    foreach (string field in new[] { "m_Items", "m_Members" })
                        m_Collections.Add(new SavedCollection(Field(list.GetType(), field, list)));
                    var removed = list.GetType().GetField("m_Removed", Instance) ?? throw new MissingFieldException("m_Removed");
                    m_Removed.Add((list, removed, removed.GetValue(list)));
                }
                foreach (string name in new[] { "s_PendingStart", "s_OnGUI", "s_OnRenderObject" })
                    m_Collections.Add(new SavedCollection(Field(typeof(Lifecycle), name)));
                m_FixedList = Field(typeof(Lifecycle), "s_FixedUpdate");
                m_RunFixed = m_FixedList.GetType().GetMethod("Run", Instance) ?? throw new MissingMethodException("Run");
                m_Handler = m_Logger.logHandler;
                m_LogEnabled = m_Logger.logEnabled;
                m_LogFilter = m_Logger.filterLogType;
                Log = new Logs(m_Handler);
                foreach (var saved in m_Collections) saved.Clear();
                foreach (var removed in m_Removed) removed.Field.SetValue(removed.List, 0);
                EditorSession.IsPlaying = true;
                m_Logger.logHandler = Log;
                m_Logger.logEnabled = true;
                m_Logger.filterLogType = LogType.Log;
            }

            internal GameObject Make(bool active)
            {
                var go = new GameObject("awake exception");
                m_Objects.Add(go);
                if (!active) go.SetActive(false);
                return go;
            }

            internal void Pump()
            {
                Lifecycle.RunPendingStarts();
                Lifecycle.RunUpdate();
                Lifecycle.RunLateUpdate();
                // Exercise the fixed list without invoking the global constant-force hook.
                m_RunFixed.Invoke(m_FixedList, new object[] { "FixedUpdate", null });
            }

            internal void ExceptionFor(Actor actor)
            {
                var error = Assert.Single(Log.Exceptions);
                Assert.Contains(Failure, error.Error.ToString());
                Assert.Same(actor, error.Context);
            }

            internal void Unregistered(Actor actor)
            {
                foreach (string name in new[] { "s_Update", "s_LateUpdate", "s_FixedUpdate" })
                {
                    var list = Field(typeof(Lifecycle), name);
                    var members = (IEnumerable)Field(list.GetType(), "m_Members", list);
                    Assert.DoesNotContain(actor, members.Cast<object>());
                }
                Assert.DoesNotContain(actor, Lifecycle.GuiBehaviours);
                Assert.DoesNotContain(actor, Lifecycle.RenderObjectBehaviours);
            }

            public void Dispose()
            {
                try
                {
                    foreach (var go in m_Objects) if (go != null) Object.DestroyImmediate(go);
                }
                finally
                {
                    try
                    {
                        foreach (var saved in m_Collections) saved.Restore();
                        foreach (var removed in m_Removed) removed.Field.SetValue(removed.List, removed.Value);
                        m_Sequence.SetValue(null, m_OldSequence);
                    }
                    finally
                    {
                        EditorSession.IsPlaying = m_Playing;
                        m_Logger.logHandler = m_Handler;
                        m_Logger.logEnabled = m_LogEnabled;
                        m_Logger.filterLogType = m_LogFilter;
                    }
                }
            }
        }

        static void NoFrameMessages(Actor actor)
        {
            Assert.Equal(0, actor.Starts);
            Assert.Equal(0, actor.Updates);
            Assert.Equal(0, actor.Lates);
            Assert.Equal(0, actor.Fixed);
        }

        [Fact]
        public void ActiveAddDisablesAComponentWhoseAwakeThrows()
        {
            using var scope = new Scope();
            var actor = scope.Make(true).AddComponent<Actor>();
            Assert.False(actor.enabled);
            Assert.True(actor.didAwake);
            Assert.Equal(1, actor.Awakes);
            Assert.Equal(0, actor.Enables);
            scope.Pump();
            NoFrameMessages(actor);
            scope.Unregistered(actor);
            scope.ExceptionFor(actor);
        }

        [Fact]
        public void FirstHierarchyActivationDisablesTheFailingComponentWithoutRetryingAwake()
        {
            using var scope = new Scope();
            var go = scope.Make(false);
            var actor = go.AddComponent<Actor>();
            Assert.Equal(0, actor.Awakes);
            go.SetActive(true);
            Assert.False(actor.enabled);
            go.SetActive(false);
            go.SetActive(true);
            Assert.Equal(1, actor.Awakes);
            scope.Pump();
            NoFrameMessages(actor);
            scope.ExceptionFor(actor);
        }

        [Fact]
        public void AnInitiallyDisabledScriptStillGetsAwakeAndStaysDisabledAfterFailure()
        {
            using var scope = new Scope();
            var go = scope.Make(false);
            var actor = go.AddComponent<Actor>();
            actor.enabled = false;
            go.SetActive(true);
            Assert.False(actor.enabled);
            Assert.Equal(1, actor.Awakes);
            Assert.Equal(0, actor.Enables);
            scope.Pump();
            NoFrameMessages(actor);
            scope.ExceptionFor(actor);
        }

        [Fact]
        public void ExplicitReenableUsesTheNormalStartPathWithoutAnotherAwake()
        {
            using var scope = new Scope();
            var actor = scope.Make(true).AddComponent<Actor>();
            Assert.False(actor.enabled);
            scope.Pump();
            NoFrameMessages(actor);
            // Unmeasured recovery preservation: this fix does not make failure permanently fatal.
            actor.enabled = true;
            scope.Pump();
            scope.Pump();
            Assert.Equal(1, actor.Awakes);
            Assert.Equal(1, actor.Enables);
            Assert.Equal(1, actor.Starts);
            Assert.Equal(2, actor.Updates);
            Assert.Equal(2, actor.Lates);
            Assert.Equal(2, actor.Fixed);
            scope.ExceptionFor(actor);
        }

        [Fact]
        public void ReentrantEnableBeforeFailureIsRemovedFromEveryDispatchList()
        {
            using var scope = new Scope();
            var go = scope.Make(false);
            var actor = go.AddComponent<Actor>();
            actor.ToggleBeforeFailure = true;
            go.SetActive(true);
            Assert.False(actor.enabled);
            Assert.False(actor.m_EnabledState);
            scope.Unregistered(actor);
            scope.Pump();
            NoFrameMessages(actor);
            Assert.False(actor.didStart);
            scope.ExceptionFor(actor);
        }

        [Fact]
        public void DeactivatingItsObjectDoesNotKeepTheFailingScriptEnabled()
        {
            using var scope = new Scope();
            var go = scope.Make(false);
            var actor = go.AddComponent<Actor>();
            actor.DeactivateBeforeFailure = true;
            go.SetActive(true);
            Assert.False(go.activeSelf);
            Assert.False(actor.enabled);
            actor.DeactivateBeforeFailure = false;
            go.SetActive(true);
            Assert.Equal(1, actor.Awakes);
            scope.Pump();
            NoFrameMessages(actor);
            scope.ExceptionFor(actor);
        }

        [Fact]
        public void AComponentRemovedInsideAwakeIsNotDisabledThroughADestroyedReceiver()
        {
            using var scope = new Scope();
            var go = scope.Make(false);
            var actor = go.AddComponent<Actor>();
            actor.DestroyBeforeFailure = true;
            Assert.Null(Record.Exception(() => go.SetActive(true)));
            Assert.True(actor == null);
            Assert.Equal(1, actor.Awakes);
            scope.Pump();
            NoFrameMessages(actor);
            scope.ExceptionFor(actor);
        }

        [Fact]
        public void SuccessfulAwakeKeepsNormalEnableStartAndUpdateBehavior()
        {
            using var scope = new Scope();
            var go = scope.Make(false);
            var actor = go.AddComponent<Actor>();
            actor.Fail = false;
            go.SetActive(true);
            Assert.True(actor.enabled);
            scope.Pump();
            Assert.Equal(1, actor.Awakes);
            Assert.Equal(1, actor.Enables);
            Assert.Equal(1, actor.Starts);
            Assert.Equal(1, actor.Updates);
            Assert.Equal(1, actor.Lates);
            Assert.Equal(1, actor.Fixed);
            Assert.Empty(scope.Log.Exceptions);
        }
    }
}
