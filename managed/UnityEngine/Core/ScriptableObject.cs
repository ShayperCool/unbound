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
            Call("Awake");
            m_Enabled = true;
            Call("OnEnable");
        }

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

        public bool isDone => m_IsDone;
        public float progress => m_IsDone ? 1f : m_Progress;
        public int priority { get; set; }

        public bool allowSceneActivation
        {
            get => m_AllowSceneActivation;
            set
            {
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
                if (m_IsDone) value(this);
                else m_Completed += value;
            }
            remove => m_Completed -= value;
        }

        internal void Complete()
        {
            if (m_IsDone) return;
            m_IsDone = true;
            m_Progress = 1f;
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
            var op = new AsyncOperation();
            op.m_IsDone = true;
            op.m_Progress = 1f;
            return op;
        }
    }
}
