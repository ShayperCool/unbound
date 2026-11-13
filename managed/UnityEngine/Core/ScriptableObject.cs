using System;
using System.Reflection;
using Unbound.Runtime;

namespace UnityEngine
{
    /// <summary>A data object independent of GameObjects. Awake and OnEnable run when one is created
    /// or loaded; OnDisable and OnDestroy when it is destroyed.</summary>
    public partial class ScriptableObject : Object
    {
        bool m_Enabled;

        public ScriptableObject()
        {
        }

        [Obsolete("Use EditorUtility.SetDirty instead")]
        public void SetDirty()
        {
            if (this == null) throw new NullReferenceException();
            UnityEditor.EditorUtility.SetDirty(this);
        }

        public static ScriptableObject CreateInstance(string className)
        {
            MainThreadCheck.Check("CreateScriptableObjectInstanceFromName");
            var type = GameDomain.FindTypeByName(className);
            if (type == null || !typeof(ScriptableObject).IsAssignableFrom(type))
            {
                Debug.LogError($"Instance of {className} couldn't be created because there is no script with that name.");
                return null;
            }
            return CreateInstance(type);
        }

        public static ScriptableObject CreateInstance(Type type)
        {
            MainThreadCheck.Check("CreateScriptableObjectInstanceFromType");
            var instance = CreateInstanceWithoutCallbacks(type);
            instance?.RunCreationCallbacks();
            return instance;
        }

        public static T CreateInstance<T>() where T : ScriptableObject => (T)CreateInstance(typeof(T));

        internal static ScriptableObject CreateInstanceWithoutCallbacks(Type type)
        {
            if (type == null || type.IsAbstract || !typeof(ScriptableObject).IsAssignableFrom(type))
            {
                Debug.LogError($"Instance of {type?.Name} couldn't be created because it is abstract or not a ScriptableObject.");
                return null;
            }
            try
            {
                using var construction = MainThreadCheck.Construct(type);
                return (ScriptableObject)Activator.CreateInstance(type, nonPublic: true);
            }
            catch (TargetInvocationException e)
            {
                throw e.InnerException ?? e;
            }
        }

        /// <summary>Awake then OnEnable, as for a new or loaded instance.</summary>
        internal void RunCreationCallbacks()
        {
            PrepareCreationCallbacks();
            Call("Awake");
            m_Enabled = true;
            Call("OnEnable");
        }

        internal virtual void PrepareCreationCallbacks() { }

        internal void RunDestroyCallbacks()
        {
            if (m_Enabled)
            {
                m_Enabled = false;
                Call("OnDisable");
            }
            Call("OnDestroy");
        }

        void Call(string message)
        {
            var method = FindMessage(GetType(), message);
            if (method == null) return;
            try
            {
                method.Invoke(this, null);
            }
            catch (TargetInvocationException e)
            {
                Debug.LogException(e.InnerException ?? e, this);
            }
        }

        static MethodInfo FindMessage(Type type, string name)
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
            for (var t = type; t != null && t != typeof(ScriptableObject); t = t.BaseType)
            {
                var m = t.GetMethod(name, flags, null, Type.EmptyTypes, null);
                if (m != null) return m;
            }
            return null;
        }
    }

    public partial class AsyncOperation : YieldInstruction
    {
        internal bool m_IsDone;
        internal float m_Progress;
        bool m_AllowSceneActivation = true;
        Action<AsyncOperation> m_Completed;
        internal Action m_OnAllowActivation;
        /// <summary>The first coroutine that yielded the operation while it ran: it resumes when the
        /// operation completes, before the completed handlers; others yielding it resume at their
        /// next coroutine point (measured: tests/projects/navbaked resume.*).</summary>
        internal Coroutine m_Waiter;

        [Flags]
        internal enum OperationCapabilities { None = 0, Priority = 1, SceneActivation = 2 }
        bool m_HasOperation;
        int m_Priority;
        float m_CompletionProgress = 1f;
        OperationCapabilities m_Capabilities = OperationCapabilities.Priority | OperationCapabilities.SceneActivation;

        internal virtual bool HasOperation => m_HasOperation;
        internal virtual OperationCapabilities Capabilities => m_Capabilities;
        internal void RequireOperation()
        {
            if (Compat.UncreatedObjectWrappersMeasured && !HasOperation) throw new NullReferenceException();
        }

        internal static T Bind<T>(T operation, OperationCapabilities capabilities = OperationCapabilities.Priority,
            float completionProgress = 1f) where T : AsyncOperation
        {
            operation.m_HasOperation = true;
            operation.m_Capabilities = Compat.UncreatedObjectWrappersMeasured ? capabilities
                : OperationCapabilities.Priority | OperationCapabilities.SceneActivation;
            operation.m_CompletionProgress = completionProgress;
            return operation;
        }

        public bool isDone { get { RequireOperation(); return m_IsDone; } }
        public float progress { get { RequireOperation(); return Compat.UncreatedObjectWrappersMeasured ? m_Progress : m_IsDone ? 1f : m_Progress; } }
        public int priority
        {
            get { RequireOperation(); return (Capabilities & OperationCapabilities.Priority) != 0 ? m_Priority : 0; }
            set { RequireOperation(); if ((Capabilities & OperationCapabilities.Priority) != 0) m_Priority = value; }
        }

        public bool allowSceneActivation
        {
            get { RequireOperation(); return (Capabilities & OperationCapabilities.SceneActivation) == 0 || m_AllowSceneActivation; }
            set
            {
                RequireOperation();
                if ((Capabilities & OperationCapabilities.SceneActivation) == 0) return;
                bool changed = !m_AllowSceneActivation && value;
                m_AllowSceneActivation = value;
                if (changed) m_OnAllowActivation?.Invoke();
            }
        }

        /// <summary>Raised when the operation completes; a handler added after completion runs at once.</summary>
        public event Action<AsyncOperation> completed
        {
            add
            {
                if (isDone) value(this);
                else m_Completed += value;
            }
            remove => m_Completed -= value;
        }

        internal void Complete()
        {
            if (m_IsDone) return;
            m_IsDone = true;
            m_Progress = Compat.UncreatedObjectWrappersMeasured ? m_CompletionProgress : 1f;
            var waiter = m_Waiter;
            m_Waiter = null;
            if (waiter != null) Unbound.Runtime.Coroutines.Resume(waiter, this);
            var handlers = m_Completed;
            m_Completed = null;
            if (handlers != null)
            {
                try { handlers(this); }
                catch (Exception e) { Debug.LogException(e); }
            }
        }

        internal static AsyncOperation Completed()
        {
            var op = Bind(new AsyncOperation());
            op.m_IsDone = true;
            op.m_Progress = 1f;
            return op;
        }
    }
}
