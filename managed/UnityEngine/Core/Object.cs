using System;
using System.Collections.Generic;
using Unbound.Runtime;
using UnityEngine.SceneManagement;

namespace UnityEngine
{
    /// <summary>
    /// Base of every object the engine owns. A destroyed object still exists as a managed object but
    /// compares equal to null, as in Unity ("fake null").
    /// </summary>
    [Serializable]
    public partial class Object
    {
        internal int m_InstanceID;
        internal string m_Name = "";
        /// <summary>Unity's pointer to the native object: zero once it is destroyed (or was never
        /// created). Plugins read it by reflection for fast null checks, so it keeps Unity's name.</summary>
        IntPtr m_CachedPtr = (IntPtr)1;
#pragma warning disable CS0169, CS0649
        /// <summary>The Editor's message for a "fake null" object (read by plugins by reflection).</summary>
        string m_UnityRuntimeErrorString;
#pragma warning restore CS0169, CS0649

        /// <summary>The engine side of the object is gone: destroyed, or never created.</summary>
        internal bool m_Destroyed
        {
            get => m_CachedPtr == IntPtr.Zero;
            set => m_CachedPtr = value ? IntPtr.Zero : (IntPtr)1;
        }
        /// <summary>Destroy() was called; teardown awaits a player-loop delivery point.</summary>
        internal bool m_PendingDestroy;
        bool m_EngineDestroyRequestNotified;
        internal bool m_DestroyAtScriptingPoint;
        HideFlags m_HideFlags;

        public Object()
        {
            m_InstanceID = ObjectRegistry.Register(this);
        }

        public string name
        {
            get
            {
                MainThreadCheck.Check("GetName");
                ThrowIfDestroyed();
                return GetName();
            }
            set
            {
                MainThreadCheck.Check("SetName");
                ThrowIfDestroyed();
                SetName(value ?? "");
            }
        }

        internal virtual string GetName() => m_Name;
        internal virtual void SetName(string value) => m_Name = value;

        public HideFlags hideFlags
        {
            get
            {
                MainThreadCheck.Check("get_hideFlags");
                return m_HideFlags;
            }
            set
            {
                MainThreadCheck.Check("set_hideFlags");
                var previous = m_HideFlags;
                m_HideFlags = value;
                OnHideFlagsChanged(previous);
            }
        }

        internal virtual void OnHideFlagsChanged(HideFlags previous) { }

        public int GetInstanceID()
        {
            MainThreadCheck.EnsureRunningOnMainThread();
            return m_InstanceID;
        }

        public override int GetHashCode() => m_InstanceID;

        public override bool Equals(object other)
        {
            var otherAsObject = other as Object;
            // A non-Object is never equal, unless both are null-like.
            if (otherAsObject == null && other != null && !(other is Object))
                return false;
            return CompareBaseObjects(this, otherAsObject);
        }

        public override string ToString()
        {
            MainThreadCheck.Check("ToString");
            if (m_Destroyed) return "null";
            return $"{GetName()} ({GetType().FullName})";
        }

        /// <summary>True for a live object: neither a null reference nor destroyed.</summary>
        public static implicit operator bool(Object exists) => !CompareBaseObjects(exists, null);

        public static bool operator ==(Object x, Object y) => CompareBaseObjects(x, y);

        public static bool operator !=(Object x, Object y) => !CompareBaseObjects(x, y);

        static bool CompareBaseObjects(Object lhs, Object rhs)
        {
            bool lhsNull = (object)lhs == null;
            bool rhsNull = (object)rhs == null;
            if (rhsNull && lhsNull) return true;
            if (rhsNull) return !IsNativeObjectAlive(lhs);
            if (lhsNull) return !IsNativeObjectAlive(rhs);
            return lhs.m_InstanceID == rhs.m_InstanceID;
        }

        static bool IsNativeObjectAlive(Object o)
        {
            if (!o.m_Destroyed) return true;
            // Unity asks the engine by instance id for a destroyed object that is not a script
            // (GetInstanceID, so only on the main thread).
            if (!(o is MonoBehaviour || o is ScriptableObject)) MainThreadCheck.EnsureRunningOnMainThread();
            return false;
        }

        /// <summary>The Editor names the type with its namespace (6000.0.71f1,
        /// tests/projects/destroyphases immediateAwake: 'UnityEngine.ParticleSystem'; appfacts and
        /// instantiateasync: 'UnityEngine.GameObject').</summary>
        internal void ThrowIfDestroyed()
        {
            if (m_Destroyed)
                throw new MissingReferenceException(
                    $"The object of type '{GetType().FullName}' has been destroyed but you are still trying to access it.\n" +
                    "Your script should either check if it is null or you should not destroy the object.");
        }

        // ------------------------------------------------------------------ Instantiate

        public static Object Instantiate(Object original)
        {
            CheckNullArgument(original, "The Object you want to instantiate is null.");
            MainThreadCheck.Check("Internal_CloneSingle");
            return Instantiation.Clone(original, null, false, null, null);
        }

        public static Object Instantiate(Object original, Transform parent) =>
            Instantiate(original, parent, false);

        public static Object Instantiate(Object original, Transform parent, bool instantiateInWorldSpace)
        {
            CheckNullArgument(original, "The Object you want to instantiate is null.");
            MainThreadCheck.Check(parent == null ? "Internal_CloneSingle" : "Internal_CloneSingleWithParent");
            return Instantiation.Clone(original, parent, instantiateInWorldSpace, null, null);
        }

        public static Object Instantiate(Object original, Vector3 position, Quaternion rotation)
        {
            CheckNullArgument(original, "The Object you want to instantiate is null.");
            MainThreadCheck.Check("Internal_InstantiateSingle");
            if (original is ScriptableObject)
                throw new ArgumentException("Cannot instantiate a ScriptableObject with a position and rotation");
            return Instantiation.Clone(original, null, false, position, rotation);
        }

        public static Object Instantiate(Object original, Vector3 position, Quaternion rotation, Transform parent)
        {
            CheckNullArgument(original, "The Object you want to instantiate is null.");
            MainThreadCheck.Check(parent == null ? "Internal_InstantiateSingle" : "Internal_InstantiateSingleWithParent");
            return Instantiation.Clone(original, parent, true, position, rotation);
        }

        public static Object Instantiate(Object original, Scene scene)
        {
            CheckNullArgument(original, "The Object you want to instantiate is null.");
            MainThreadCheck.Check("Internal_CloneSingleWithScene");
            var clone = Instantiation.Clone(original, null, false, null, null);
            if (clone is GameObject go)
                SceneManager.MoveGameObjectToScene(go, scene);
            else if (clone is Component c)
                SceneManager.MoveGameObjectToScene(c.gameObject, scene);
            return clone;
        }

        public static T Instantiate<T>(T original) where T : Object => (T)Instantiate((Object)original);

        public static T Instantiate<T>(T original, Vector3 position, Quaternion rotation) where T : Object =>
            (T)Instantiate((Object)original, position, rotation);

        public static T Instantiate<T>(T original, Vector3 position, Quaternion rotation, Transform parent) where T : Object =>
            (T)Instantiate((Object)original, position, rotation, parent);

        public static T Instantiate<T>(T original, Transform parent) where T : Object =>
            (T)Instantiate((Object)original, parent, false);

        public static T Instantiate<T>(T original, Transform parent, bool worldPositionStays) where T : Object =>
            (T)Instantiate((Object)original, parent, worldPositionStays);

        // ------------------------------------------------------------------ Destroy

        public static void Destroy(Object obj, [UnityEngine.Internal.DefaultValue("0.0F")] float t)
        {
            MainThreadCheck.Check("Destroy");
            if ((object)obj == null || obj.m_Destroyed) return;
            if (Compat.DestroyPhasesMeasured && obj.IsPersistentAsset)
            {
                Debug.LogError("Destroying assets is not permitted to avoid data loss.\n" +
                    "If you really want to remove an asset use DestroyImmediate (theObject, true);", obj);
                return;
            }
            Destruction.Destroy(obj, t);
        }

        public static void Destroy(Object obj) => Destroy(obj, 0f);

        public static void DestroyImmediate(Object obj, [UnityEngine.Internal.DefaultValue("false")] bool allowDestroyingAssets)
        {
            MainThreadCheck.Check("DestroyImmediate");
            if ((object)obj == null || obj.m_Destroyed) return;
            if (!Compat.DestroyPhasesMeasured && Unbound.Runtime.PhysicsWorld.IsStepping && obj is GameObject)
            {
                Debug.LogError("Destroying GameObjects immediately is not permitted during physics trigger/contact, animation event callbacks, rendering callbacks or OnValidate. You must use Destroy instead.");
                return;
            }
            if (obj.IsPersistentAsset && !allowDestroyingAssets)
            {
                Debug.LogError("Destroying assets is not permitted to avoid data loss.\n" +
                               "If you really want to remove an asset use DestroyImmediate (theObject, true);");
                return;
            }
            Destruction.DestroyImmediate(obj);
        }

        public static void DestroyImmediate(Object obj) => DestroyImmediate(obj, false);

        [Obsolete("use Object.Destroy instead.")]
        public static void DestroyObject(Object obj, [UnityEngine.Internal.DefaultValue("0.0F")] float t) => Destroy(obj, t);

        [Obsolete("use Object.Destroy instead.")]
        public static void DestroyObject(Object obj) => Destroy(obj, 0f);

        public static void DontDestroyOnLoad(Object target)
        {
            if ((object)target == null) return;
            GameObject go = target as GameObject ?? (target as Component)?.gameObject;
            if (go == null)
                return;
            if (go.transform.parent != null)
            {
                Debug.LogWarning("DontDestroyOnLoad only works for root GameObjects or components on root GameObjects.");
                return;
            }
            SceneManager.MoveToDontDestroyOnLoad(go);
        }

        /// <summary>Loaded from an asset file (not a scene or runtime object).</summary>
        internal bool IsPersistentAsset;

        internal static bool IsPersistent(Object obj) => obj != null && obj.IsPersistentAsset;

        /// <summary>A live object has the id (a destroyed one does not).</summary>
        internal static bool DoesObjectWithInstanceIDExist(int instanceID) => ObjectRegistry.FindById(instanceID) != null;

        internal static Object FindObjectFromInstanceID(int instanceID) => ObjectRegistry.FindById(instanceID);

        /// <summary>Every asset is loaded with its scene or file, so the id's object is already there.</summary>
        internal static Object ForceLoadFromInstanceID(int instanceID) => ObjectRegistry.FindById(instanceID);

        // ------------------------------------------------------------------ Find

        public static Object[] FindObjectsOfType(Type type) => FindObjectsOfType(type, false);

        public static Object[] FindObjectsOfType(Type type, bool includeInactive)
        {
            MainThreadCheck.Check("FindObjectsByType");
            return ObjectRegistry.TypedArray(type, ObjectRegistry.FindSceneObjects(type, includeInactive, sorted: true));
        }

        public static Object[] FindObjectsByType(Type type, FindObjectsSortMode sortMode) =>
            FindObjectsByType(type, FindObjectsInactive.Exclude, sortMode);

        public static Object[] FindObjectsByType(Type type, FindObjectsInactive findObjectsInactive, FindObjectsSortMode sortMode)
        {
            MainThreadCheck.Check("FindObjectsByType");
            return ObjectRegistry.TypedArray(type, ObjectRegistry.FindSceneObjects(type, findObjectsInactive == FindObjectsInactive.Include,
                sorted: sortMode == FindObjectsSortMode.InstanceID));
        }

        public static T[] FindObjectsOfType<T>() where T : Object => Cast<T>(FindObjectsOfType(typeof(T), false));

        public static T[] FindObjectsOfType<T>(bool includeInactive) where T : Object =>
            Cast<T>(FindObjectsOfType(typeof(T), includeInactive));

        public static T[] FindObjectsByType<T>(FindObjectsSortMode sortMode) where T : Object =>
            Cast<T>(FindObjectsByType(typeof(T), FindObjectsInactive.Exclude, sortMode));

        public static T[] FindObjectsByType<T>(FindObjectsInactive findObjectsInactive, FindObjectsSortMode sortMode) where T : Object =>
            Cast<T>(FindObjectsByType(typeof(T), findObjectsInactive, sortMode));

        public static Object FindObjectOfType(Type type) => FindObjectOfType(type, false);

        public static Object FindObjectOfType(Type type, bool includeInactive)
        {
            MainThreadCheck.Check("FindObjectsByType");
            return ObjectRegistry.FindFirstSceneObject(type, includeInactive);
        }

        public static T FindObjectOfType<T>() where T : Object => (T)FindObjectOfType(typeof(T), false);

        public static T FindObjectOfType<T>(bool includeInactive) where T : Object =>
            (T)FindObjectOfType(typeof(T), includeInactive);

        public static T FindFirstObjectByType<T>() where T : Object => (T)FindObjectOfType(typeof(T), false);

        public static T FindAnyObjectByType<T>() where T : Object => (T)FindObjectOfType(typeof(T), false);

        public static T FindFirstObjectByType<T>(FindObjectsInactive findObjectsInactive) where T : Object =>
            (T)FindObjectOfType(typeof(T), findObjectsInactive == FindObjectsInactive.Include);

        public static T FindAnyObjectByType<T>(FindObjectsInactive findObjectsInactive) where T : Object =>
            (T)FindObjectOfType(typeof(T), findObjectsInactive == FindObjectsInactive.Include);

        public static Object FindFirstObjectByType(Type type) => FindObjectOfType(type, false);

        public static Object FindAnyObjectByType(Type type) => FindObjectOfType(type, false);

        public static Object FindFirstObjectByType(Type type, FindObjectsInactive findObjectsInactive) =>
            FindObjectOfType(type, findObjectsInactive == FindObjectsInactive.Include);

        public static Object FindAnyObjectByType(Type type, FindObjectsInactive findObjectsInactive) =>
            FindObjectOfType(type, findObjectsInactive == FindObjectsInactive.Include);

        [Obsolete("Please use Resources.FindObjectsOfTypeAll instead")]
        public static Object[] FindObjectsOfTypeAll(Type type) => Resources.FindObjectsOfTypeAll(type);

        [Obsolete("warning use Object.FindObjectsByType instead.")]
        public static Object[] FindObjectsOfTypeIncludingAssets(Type type) => Resources.FindObjectsOfTypeAll(type);

        static T[] Cast<T>(Object[] objects) where T : Object
        {
            var result = new T[objects.Length];
            for (int i = 0; i < objects.Length; i++)
                result[i] = (T)objects[i];
            return result;
        }

        static void CheckNullArgument(object arg, string message)
        {
            if (arg == null || (arg is Object o && o.m_Destroyed))
                throw new ArgumentException(message);
        }

        /// <summary>A constructor off the main thread: Unity throws before the engine object exists,
        /// so the managed one is left destroyed and unregistered.</summary>
        internal void CheckMainThreadCreation(string binding)
        {
            if (MainThreadCheck.Allowed) return;
            m_Destroyed = true;
            ObjectRegistry.Unregister(this);
            MainThreadCheck.Throw(binding);
        }

        internal void NotifyEngineDestroyRequested()
        {
            if (m_EngineDestroyRequestNotified) return;
            m_EngineDestroyRequestNotified = true;
            OnEngineDestroyRequested();
        }

        /// <summary>Deferred destruction was requested; the object remains valid until delivery.</summary>
        internal virtual void OnEngineDestroyRequested() { }

        // Called when the engine object is gone; subclasses release what they hold.
        internal virtual void OnEngineDestroy() { }

        /// <summary>Marks the engine side destroyed and forgets the instance id.</summary>
        internal void MarkDestroyed()
        {
            if (m_Destroyed) return;
            m_Destroyed = true;
            OnEngineDestroy();
            ObjectRegistry.Unregister(this);
        }
    }




    public partial class MissingReferenceException : SystemException
    {
        public MissingReferenceException() : base("A Unity Runtime error occurred!") { }
        public MissingReferenceException(string message) : base(message) { }
        public MissingReferenceException(string message, Exception innerException) : base(message, innerException) { }
    }

    public partial class MissingComponentException : SystemException
    {
        public MissingComponentException() : base("A Unity Runtime error occurred!") { }
        public MissingComponentException(string message) : base(message) { }
        public MissingComponentException(string message, Exception innerException) : base(message, innerException) { }
    }

    public partial class UnassignedReferenceException : SystemException
    {
        public UnassignedReferenceException() : base("A Unity Runtime error occurred!") { }
        public UnassignedReferenceException(string message) : base(message) { }
        public UnassignedReferenceException(string message, Exception innerException) : base(message, innerException) { }
    }

    public partial class UnityException : SystemException
    {
        public UnityException() : base("A Unity Runtime error occurred!") { }
        public UnityException(string message) : base(message) { }
        public UnityException(string message, Exception innerException) : base(message, innerException) { }
    }
}

namespace UnityEngine.Internal
{
    [AttributeUsage(AttributeTargets.Parameter | AttributeTargets.ReturnValue)]
    public partial class DefaultValueAttribute : Attribute
    {
        readonly object DefaultValue;
        public DefaultValueAttribute(string value) { DefaultValue = value; }
        public object Value => DefaultValue;
        public override bool Equals(object obj) => obj is DefaultValueAttribute a && Equals(a.DefaultValue, DefaultValue);
        public override int GetHashCode() => DefaultValue?.GetHashCode() ?? 0;
    }

    [Serializable]
    public partial class ExcludeFromDocsAttribute : Attribute
    {
    }
}
