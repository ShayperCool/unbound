using System;
using System.Collections.Generic;
using Unbound.Runtime;
using UnityEngine.SceneManagement;

namespace UnityEngine
{
    public sealed partial class GameObject : Object
    {
        internal readonly List<Component> m_Components = new List<Component>(4);
        internal Transform m_Transform;
        /// <summary>Creation order for the existing application callback traversal, independent
        /// of the public instance ID and retained when the source scene is unloaded.</summary>
        internal long m_CreationOrder;
        internal int m_Layer;
        internal string m_Tag = "Untagged";
        internal bool m_IsActive = true;
        /// <summary>The serialized StaticEditorFlags (m_StaticEditorFlags): ContributeGI (1)
        /// places a renderer in the realtime GI precompute.</summary>
        internal uint m_StaticEditorFlags;
        internal uint m_NavMeshLayer;
        internal Texture2D m_Icon;
        [NonSerialized] internal int m_ActivationTransitionDepth;
        // Scene-authored objects are loaded but inactive during BeforeSceneLoad.
        internal bool m_AwaitingSceneAwake;
        internal SceneHandle m_Scene;
        /// <summary>A GameObject of an asset (a prefab's contents): never awake, never in a scene.</summary>
        internal bool m_IsPrefabAsset;

        public GameObject(string name)
        {
            m_Name = name ?? "";
            Internal_Create(this, null);
        }

        public GameObject() : this("New Game Object")
        {
        }

        public GameObject(string name, params Type[] components)
        {
            m_Name = name ?? "";
            Internal_Create(this, components);
        }

        /// <summary>Used by loaders that add the transform themselves.</summary>
        internal GameObject(string name, bool bare)
        {
            m_Name = name ?? "";
        }

        /// <summary>A hidden object the engine keeps for its own work (a capture camera): it is in
        /// no scene, so no scene lists it among its roots and it takes no sibling index there.</summary>
        internal static GameObject CreateDetached(string name)
        {
            var go = new GameObject(name, bare: true);
            Internal_Create(go, null, SceneRegistry.Detached);
            go.hideFlags = HideFlags.HideAndDontSave;
            return go;
        }

        static void Internal_Create(GameObject go, Type[] components, SceneData scene = null)
        {
            go.CheckMainThreadCreation("Internal_CreateGameObject");
            // A GameObject a project's importer makes is part of the asset it imports, as a loaded
            // prefab's are: in no scene, never awake (measured: tests/projects/svgsprites).
            if (Unbound.Runtime.ScriptedImports.Importing)
            {
                go.m_IsPrefabAsset = true;
                go.AddTransform(typeof(Transform));
                if (components != null)
                    foreach (var type in components)
                        go.AddComponent(type);
                return;
            }
            // During a Single load's teardown this is the incoming scene, not the active one, and
            // it is set before any component wakes up.
            go.m_Scene = (scene ?? SceneRegistry.DefaultCreationScene()).Handle;
            go.AddTransform(typeof(Transform));
            SceneRegistry.AddRoot(go);
            if (components != null)
                foreach (var type in components)
                    go.AddComponent(type);
        }

        internal Transform AddTransform(Type type)
        {
            var t = (Transform)ComponentFactory.Create(type, this);
            m_Transform = t;
            m_Components.Insert(0, t);
            return t;
        }

        internal override void OnHideFlagsChanged(HideFlags previous)
        {
            if (!Compat.HideFlagsRootOwnershipMeasured) return;
            var flags = hideFlags;
            // The Transform can send callbacks before later components receive their flags.
            for (int i = 0; i < m_Components.Count; i++)
            {
                var component = m_Components[i];
                if (!component.m_Destroyed) component.hideFlags = flags;
            }
        }

        public Transform transform
        {
            get
            {
                MainThreadCheck.Check("get_transform");
                ThrowIfDestroyed();
                return m_Transform;
            }
        }

        public GameObject gameObject => this;

        public int layer
        {
            get
            {
                MainThreadCheck.Check("get_layer");
                return m_Layer;
            }
            set
            {
                MainThreadCheck.Check("set_layer");
                if (value < 0 || value > 31)
                {
                    Debug.LogError("A game object can only be in one layer. The layer needs to be in the range [0...31]");
                    return;
                }
                m_Layer = value;
            }
        }

        public string tag
        {
            get
            {
                MainThreadCheck.Check("get_tag");
                return m_Tag;
            }
            set
            {
                MainThreadCheck.Check("set_tag");
                if (!TagManager.IsDefined(value))
                    throw new UnityException($"Tag: {value} is not defined.");
                m_Tag = value;
            }
        }

        /// <summary>Throws on a destroyed GameObject (measured in the Editor after the destruction
        /// is delivered: tests/projects/destroycontract), like <see cref="activeInHierarchy"/> and
        /// GetComponent. Pending request reads are measured. OnDestroy-time reads retain the
        /// previous flag timing; their accessibility is not measured.</summary>
        public bool activeSelf
        {
            get
            {
                MainThreadCheck.Check("get_activeSelf");
                ThrowIfDestroyed();
                return m_IsActive;
            }
        }

        public bool activeInHierarchy
        {
            get
            {
                MainThreadCheck.Check("get_activeInHierarchy");
                ThrowIfDestroyed();
                return ActiveInHierarchyNoThrow;
            }
        }

        /// <summary>The engine's own check: false for a destroyed GameObject instead of throwing.</summary>
        internal bool ActiveInHierarchyNoThrow
        {
            get
            {
                if (m_Destroyed || m_IsPrefabAsset) return false;
                for (var t = m_Transform; t != null; t = t.m_Parent)
                    if (!t.m_GameObject.m_IsActive || t.m_GameObject.m_AwaitingSceneAwake) return false;
                return true;
            }
        }

        [Obsolete("gameObject.active is obsolete. Use GameObject.SetActive(), GameObject.activeSelf or GameObject.activeInHierarchy.")]
        public bool active
        {
            get => activeInHierarchy;
            set => SetActive(value);
        }

        public bool isStatic
        {
            get => m_StaticEditorFlags != 0;
            set => m_StaticEditorFlags = value ? int.MaxValue : 0u;
        }

        public Scene scene
        {
            get
            {
                MainThreadCheck.Check("get_scene");
                return new Scene(m_Scene);
            }
        }

        public ulong sceneCullingMask => ulong.MaxValue;

        public void SetActive(bool value)
        {
            MainThreadCheck.Check("SetActive");
            ThrowIfDestroyed();
            if (m_IsActive == value) return;
            if (Compat.DestroyPhasesMeasured && m_ActivationTransitionDepth != 0)
            {
                Debug.LogError("GameObject is already being activated or deactivated.", this);
                return;
            }
            bool wasActive = ActiveInHierarchyNoThrow;
            m_IsActive = value;
            bool isActive = ActiveInHierarchyNoThrow;
            if (wasActive != isActive)
                Lifecycle.OnHierarchyActiveChanged(this, isActive);
        }

        [Obsolete("gameObject.SetActiveRecursively() is obsolete. Use GameObject.SetActive(), which is now inherited by children.")]
        public void SetActiveRecursively(bool state) => SetActive(state);

        public bool CompareTag(string tag)
        {
            MainThreadCheck.Check("CompareTag_Internal");
            return m_Tag == tag;
        }

        public bool CompareTag(TagHandle tag)
        {
            MainThreadCheck.Check("CompareTagHandle_Internal");
            return m_Tag == tag.Name;
        }

        // ------------------------------------------------------------------ components

        public Component AddComponent(Type componentType)
        {
            MainThreadCheck.Check("Internal_AddComponentWithType");
            ThrowIfDestroyed();
            return ComponentFactory.Add(this, componentType);
        }

        public T AddComponent<T>() where T : Component => (T)AddComponent(typeof(T));

        [Obsolete("GameObject.AddComponent with string argument has been deprecated. Use GameObject.AddComponent<T>() instead.", true)]
        public Component AddComponent(string className) => throw new NotSupportedException("AddComponent(string) is not supported.");

        /// <summary>Throws on a destroyed GameObject (measured for the generic overload in the
        /// Editor after the destruction is delivered: tests/projects/destroycontract). Type and
        /// string overloads use the same guard by implementation choice and are unmeasured.
        /// A live GameObject without the component gives null.</summary>
        public Component GetComponent(Type type)
        {
            MainThreadCheck.Check("GetComponent");
            ThrowIfDestroyed();
            return FindComponent(type);
        }

        public T GetComponent<T>()
        {
            MainThreadCheck.Check("GetComponentFastPath");
            ThrowIfDestroyed();
            return FindComponent<T>();
        }

        /// <summary>The engine's own lookup: the first live component of the type, without the
        /// destroyed-object check of GetComponent.</summary>
        internal Component FindComponent(Type type)
        {
            if (type == null) return null;
            foreach (var c in m_Components)
                if (!c.m_Destroyed && type.IsInstanceOfType(c))
                    return c;
            return null;
        }

        internal T FindComponent<T>()
        {
            foreach (var c in m_Components)
                if (!c.m_Destroyed && c is T t)
                    return t;
            return default;
        }

        /// <summary>The first component whose class is named so, by its name or its full name
        /// with the namespace (as data binding libraries store them).</summary>
        public Component GetComponent(string type)
        {
            ThrowIfDestroyed();
            foreach (var c in m_Components)
            {
                if (c.m_Destroyed) continue;
                var t = c.GetType();
                if (t.Name == type || t.FullName == type)
                    return c;
            }
            return null;
        }

        // TryGetComponent on a destroyed GameObject is not measured: it keeps answering false.
        public bool TryGetComponent(Type type, out Component component)
        {
            component = FindComponent(type);
            return component != null;
        }

        public bool TryGetComponent<T>(out T component)
        {
            component = FindComponent<T>();
            return component != null && !(component is Object o && o.m_Destroyed);
        }

        public Component GetComponentInChildren(Type type) => GetComponentInChildren(type, false);

        public Component GetComponentInChildren(Type type, bool includeInactive) =>
            ComponentQueries.InChildren(this, type, includeInactive);

        public T GetComponentInChildren<T>() => GetComponentInChildren<T>(false);

        public T GetComponentInChildren<T>(bool includeInactive) =>
            (T)(object)ComponentQueries.InChildren(this, typeof(T), includeInactive);

        public Component GetComponentInParent(Type type) => GetComponentInParent(type, false);

        public Component GetComponentInParent(Type type, bool includeInactive) =>
            ComponentQueries.InParent(this, type, includeInactive);

        public T GetComponentInParent<T>() => GetComponentInParent<T>(false);

        public T GetComponentInParent<T>(bool includeInactive) =>
            (T)(object)ComponentQueries.InParent(this, typeof(T), includeInactive);

        public Component[] GetComponents(Type type)
        {
            MainThreadCheck.Check("GetComponentsInternal");
            var list = new List<Component>();
            ComponentQueries.OnSelf(this, type, list);
            return list.ToArray();
        }

        public T[] GetComponents<T>()
        {
            var list = new List<T>();
            GetComponents(list);
            return list.ToArray();
        }

        public void GetComponents(Type type, List<Component> results)
        {
            MainThreadCheck.Check("GetComponentsInternal");
            results.Clear();
            ComponentQueries.OnSelf(this, type, results);
        }

        public void GetComponents<T>(List<T> results)
        {
            MainThreadCheck.Check("GetComponentsInternal");
            results.Clear();
            foreach (var c in m_Components)
                if (!c.m_Destroyed && c is T t)
                    results.Add(t);
        }

        public Component[] GetComponentsInChildren(Type type) => GetComponentsInChildren(type, false);

        public Component[] GetComponentsInChildren(Type type, [UnityEngine.Internal.DefaultValue("false")] bool includeInactive)
        {
            var list = new List<Component>();
            ComponentQueries.AllInChildren(this, type, includeInactive, list);
            return list.ToArray();
        }

        public T[] GetComponentsInChildren<T>() => GetComponentsInChildren<T>(false);

        public T[] GetComponentsInChildren<T>(bool includeInactive)
        {
            var list = new List<T>();
            GetComponentsInChildren(includeInactive, list);
            return list.ToArray();
        }

        public void GetComponentsInChildren<T>(List<T> results) => GetComponentsInChildren(false, results);

        public void GetComponentsInChildren<T>(bool includeInactive, List<T> results)
        {
            results.Clear();
            var list = new List<Component>();
            ComponentQueries.AllInChildren(this, typeof(T), includeInactive, list);
            foreach (var c in list) results.Add((T)(object)c);
        }

        public Component[] GetComponentsInParent(Type type) => GetComponentsInParent(type, false);

        public Component[] GetComponentsInParent(Type type, [UnityEngine.Internal.DefaultValue("false")] bool includeInactive)
        {
            var list = new List<Component>();
            ComponentQueries.AllInParent(this, type, includeInactive, list);
            return list.ToArray();
        }

        public T[] GetComponentsInParent<T>() => GetComponentsInParent<T>(false);

        public T[] GetComponentsInParent<T>(bool includeInactive)
        {
            var list = new List<T>();
            GetComponentsInParent(includeInactive, list);
            return list.ToArray();
        }

        public void GetComponentsInParent<T>(bool includeInactive, List<T> results)
        {
            results.Clear();
            var list = new List<Component>();
            ComponentQueries.AllInParent(this, typeof(T), includeInactive, list);
            foreach (var c in list) results.Add((T)(object)c);
        }

        public int GetComponentCount() => m_Components.Count;

        public Component GetComponentAtIndex(int index)
        {
            if (index < 0 || index >= m_Components.Count)
                throw new ArgumentOutOfRangeException(nameof(index), "Valid range is 0 to GetComponentCount() - 1.");
            return m_Components[index];
        }

        public T GetComponentAtIndex<T>(int index) where T : Component => (T)GetComponentAtIndex(index);

        public int GetComponentIndex(Component component) => m_Components.IndexOf(component);

        // ------------------------------------------------------------------ messages

        public void SendMessageUpwards(string methodName, [UnityEngine.Internal.DefaultValue("null")] object value, [UnityEngine.Internal.DefaultValue("SendMessageOptions.RequireReceiver")] SendMessageOptions options)
        {
            for (var t = m_Transform; t != null; t = t.m_Parent)
                Messaging.Send(t.m_GameObject, methodName, value, SendMessageOptions.DontRequireReceiver, out _);
        }

        public void SendMessageUpwards(string methodName, object value) => SendMessageUpwards(methodName, value, SendMessageOptions.RequireReceiver);
        public void SendMessageUpwards(string methodName) => SendMessageUpwards(methodName, null, SendMessageOptions.RequireReceiver);
        public void SendMessageUpwards(string methodName, SendMessageOptions options) => SendMessageUpwards(methodName, null, options);

        public void SendMessage(string methodName, [UnityEngine.Internal.DefaultValue("null")] object value, [UnityEngine.Internal.DefaultValue("SendMessageOptions.RequireReceiver")] SendMessageOptions options) =>
            Messaging.Send(this, methodName, value, options, out _);

        public void SendMessage(string methodName, object value) => SendMessage(methodName, value, SendMessageOptions.RequireReceiver);
        public void SendMessage(string methodName) => SendMessage(methodName, null, SendMessageOptions.RequireReceiver);
        public void SendMessage(string methodName, SendMessageOptions options) => SendMessage(methodName, null, options);

        public void BroadcastMessage(string methodName, [UnityEngine.Internal.DefaultValue("null")] object parameter, [UnityEngine.Internal.DefaultValue("SendMessageOptions.RequireReceiver")] SendMessageOptions options) =>
            Messaging.Broadcast(this, methodName, parameter, options);

        public void BroadcastMessage(string methodName, object parameter) => BroadcastMessage(methodName, parameter, SendMessageOptions.RequireReceiver);
        public void BroadcastMessage(string methodName) => BroadcastMessage(methodName, null, SendMessageOptions.RequireReceiver);
        public void BroadcastMessage(string methodName, SendMessageOptions options) => BroadcastMessage(methodName, null, options);

        // ------------------------------------------------------------------ statics

        public static GameObject Find(string name)
        {
            MainThreadCheck.Check("Find");
            return SceneRegistry.FindByPath(name);
        }

        public static GameObject FindWithTag(string tag) => FindGameObjectWithTag(tag);

        public static GameObject FindGameObjectWithTag(string tag)
        {
            MainThreadCheck.Check("FindGameObjectWithTag");
            if (!TagManager.IsDefined(tag))
                throw new UnityException($"Tag: {tag} is not defined.");
            foreach (var go in SceneRegistry.AllGameObjects(includeInactive: false))
                if (go.m_Tag == tag) return go;
            return null;
        }

        public static GameObject[] FindGameObjectsWithTag(string tag)
        {
            MainThreadCheck.Check("FindGameObjectsWithTag");
            if (!TagManager.IsDefined(tag))
                throw new UnityException($"Tag: {tag} is not defined.");
            var list = new List<GameObject>();
            foreach (var go in SceneRegistry.AllGameObjects(includeInactive: false))
                if (go.m_Tag == tag) list.Add(go);
            return list.ToArray();
        }

        public static GameObject CreatePrimitive(PrimitiveType type) => Primitives.Create(type);

        internal override void OnEngineDestroyRequested()
        {
            foreach (var component in m_Components) component.NotifyEngineDestroyRequested();
            foreach (var child in m_Transform.m_Children) child.m_GameObject.NotifyEngineDestroyRequested();
        }

        internal override void OnEngineDestroy()
        {
            SceneRegistry.RemoveRoot(this);
        }
    }



    public partial struct TagHandle
    {
        internal string Name;
        public static TagHandle GetExistingTag(string tagName)
        {
            if (!TagManager.IsDefined(tagName))
                throw new ArgumentException($"Tag \"{tagName}\" is not defined.");
            return new TagHandle { Name = tagName };
        }
        public override string ToString() => Name;
    }
}
