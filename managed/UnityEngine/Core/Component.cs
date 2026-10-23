using System;
using System.Collections;
using System.Collections.Generic;
using Unbound.Runtime;

namespace UnityEngine
{
    public partial class Component : Object
    {
        internal GameObject m_GameObject;

        public Component()
        {
            // A component exists only on a GameObject. `new MyBehaviour()` makes an object that
            // compares equal to null, as Unity does (with its warning).
            if (!ComponentFactory.IsCreating)
            {
                m_Destroyed = true;
                if (this is MonoBehaviour)
                    Debug.LogWarning($"You are trying to create a MonoBehaviour using the 'new' keyword.  This is not allowed.  MonoBehaviours can only be added using AddComponent(). Alternatively, your script can inherit from ScriptableObject or no base class at all");
            }
        }

        internal override string GetName() => m_GameObject != null ? m_GameObject.m_Name : m_Name;

        internal override void SetName(string value)
        {
            if (m_GameObject != null) m_GameObject.m_Name = value;
            else m_Name = value;
        }

        public Transform transform
        {
            get
            {
                ThrowIfDestroyed();
                return m_GameObject.m_Transform;
            }
        }

        public GameObject gameObject
        {
            get
            {
                ThrowIfDestroyed();
                return m_GameObject;
            }
        }

        public string tag
        {
            get => gameObject.tag;
            set => gameObject.tag = value;
        }

        public bool CompareTag(string tag) => gameObject.CompareTag(tag);

        public bool CompareTag(TagHandle tag) => gameObject.CompareTag(tag);

        public Component GetComponent(Type type) => gameObject.GetComponent(type);

        public T GetComponent<T>() => gameObject.GetComponent<T>();

        public Component GetComponent(string type) => gameObject.GetComponent(type);

        public bool TryGetComponent(Type type, out Component component) => gameObject.TryGetComponent(type, out component);

        public bool TryGetComponent<T>(out T component) => gameObject.TryGetComponent(out component);

        public Component GetComponentInChildren(Type t, bool includeInactive) => gameObject.GetComponentInChildren(t, includeInactive);

        public Component GetComponentInChildren(Type t) => GetComponentInChildren(t, false);

        public T GetComponentInChildren<T>([UnityEngine.Internal.DefaultValue("false")] bool includeInactive) =>
            gameObject.GetComponentInChildren<T>(includeInactive);

        public T GetComponentInChildren<T>() => GetComponentInChildren<T>(false);

        public Component[] GetComponentsInChildren(Type t, bool includeInactive) => gameObject.GetComponentsInChildren(t, includeInactive);

        public Component[] GetComponentsInChildren(Type t) => GetComponentsInChildren(t, false);

        public T[] GetComponentsInChildren<T>(bool includeInactive) => gameObject.GetComponentsInChildren<T>(includeInactive);

        public void GetComponentsInChildren<T>(bool includeInactive, List<T> result) => gameObject.GetComponentsInChildren(includeInactive, result);

        public T[] GetComponentsInChildren<T>() => GetComponentsInChildren<T>(false);

        public void GetComponentsInChildren<T>(List<T> results) => GetComponentsInChildren(false, results);

        public Component GetComponentInParent(Type t, bool includeInactive) => gameObject.GetComponentInParent(t, includeInactive);

        public Component GetComponentInParent(Type t) => gameObject.GetComponentInParent(t, false);

        public T GetComponentInParent<T>([UnityEngine.Internal.DefaultValue("false")] bool includeInactive) =>
            gameObject.GetComponentInParent<T>(includeInactive);

        public T GetComponentInParent<T>() => gameObject.GetComponentInParent<T>(false);

        public Component[] GetComponentsInParent(Type t, [UnityEngine.Internal.DefaultValue("false")] bool includeInactive) =>
            gameObject.GetComponentsInParent(t, includeInactive);

        public Component[] GetComponentsInParent(Type t) => GetComponentsInParent(t, false);

        public T[] GetComponentsInParent<T>(bool includeInactive) => gameObject.GetComponentsInParent<T>(includeInactive);

        public void GetComponentsInParent<T>(bool includeInactive, List<T> results) => gameObject.GetComponentsInParent(includeInactive, results);

        public T[] GetComponentsInParent<T>() => GetComponentsInParent<T>(false);

        public Component[] GetComponents(Type type) => gameObject.GetComponents(type);

        public void GetComponents(Type type, List<Component> results) => gameObject.GetComponents(type, results);

        public void GetComponents<T>(List<T> results) => gameObject.GetComponents(results);

        public T[] GetComponents<T>() => gameObject.GetComponents<T>();

        public int GetComponentIndex() => gameObject.GetComponentIndex(this);

        public void SendMessageUpwards(string methodName, [UnityEngine.Internal.DefaultValue("null")] object value, [UnityEngine.Internal.DefaultValue("SendMessageOptions.RequireReceiver")] SendMessageOptions options) =>
            gameObject.SendMessageUpwards(methodName, value, options);

        public void SendMessageUpwards(string methodName, object value) => SendMessageUpwards(methodName, value, SendMessageOptions.RequireReceiver);
        public void SendMessageUpwards(string methodName) => SendMessageUpwards(methodName, null, SendMessageOptions.RequireReceiver);
        public void SendMessageUpwards(string methodName, SendMessageOptions options) => SendMessageUpwards(methodName, null, options);

        public void SendMessage(string methodName, object value) => SendMessage(methodName, value, SendMessageOptions.RequireReceiver);
        public void SendMessage(string methodName) => SendMessage(methodName, null, SendMessageOptions.RequireReceiver);
        public void SendMessage(string methodName, object value, SendMessageOptions options) => gameObject.SendMessage(methodName, value, options);
        public void SendMessage(string methodName, SendMessageOptions options) => SendMessage(methodName, null, options);

        public void BroadcastMessage(string methodName, [UnityEngine.Internal.DefaultValue("null")] object parameter, [UnityEngine.Internal.DefaultValue("SendMessageOptions.RequireReceiver")] SendMessageOptions options) =>
            gameObject.BroadcastMessage(methodName, parameter, options);

        public void BroadcastMessage(string methodName, object parameter) => BroadcastMessage(methodName, parameter, SendMessageOptions.RequireReceiver);
        public void BroadcastMessage(string methodName) => BroadcastMessage(methodName, null, SendMessageOptions.RequireReceiver);
        public void BroadcastMessage(string methodName, SendMessageOptions options) => BroadcastMessage(methodName, null, options);

        // Engine-side hooks of built-in components.

        /// <summary>The component was added to its GameObject (before Awake).</summary>
        internal virtual void OnAttached() { }

        /// <summary>The component became active and enabled (engine components register here).</summary>
        internal virtual void OnEngineEnable() { }

        internal virtual void OnEngineDisable() { }

        /// <summary>The transform of the GameObject or one of its parents changed.</summary>
        internal virtual void OnTransformChanged() { }

        /// <summary>Instantiate: copies the engine state of a built-in component; references to
        /// objects of the cloned hierarchy go through <paramref name="remap"/>.</summary>
        internal virtual void CopyFrom(Component source, Func<Object, Object> remap) { }

        /// <summary>Reads the engine state of a built-in component from its serialized document.</summary>
        internal virtual void ReadSerialized(Unbound.Runtime.YNode body, Unbound.Runtime.IReferenceResolver resolver) { }
    }

    public partial class Behaviour : Component
    {
        internal bool m_Enabled = true;

        public bool enabled
        {
            get => m_Enabled;
            set
            {
                ThrowIfDestroyed();
                if (m_Enabled == value) return;
                m_Enabled = value;
                Lifecycle.OnBehaviourEnabledChanged(this, value);
            }
        }

        public bool isActiveAndEnabled => !m_Destroyed && m_Enabled && m_GameObject != null && m_GameObject.activeInHierarchy;
    }
}
