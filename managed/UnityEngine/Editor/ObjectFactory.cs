using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace UnityEditor
{
    /// <summary>
    /// Creates GameObjects, components and objects as the Editor's menus do (matches the Unity
    /// 6000.0.71f1 Editor in Play mode, tests/projects/objectfactory): a new GameObject joins the
    /// current undo group ("Create NAME") and raises componentWasAdded for its Transform; each
    /// added component, required ones first, raises it too and takes its GameObject's hide flags;
    /// a component the GameObject cannot take again throws instead of being logged. In Play mode
    /// no component or object gets Reset (Edit mode is unmeasured).
    /// </summary>
    public static partial class ObjectFactory
    {
        public static event Action<Component> componentWasAdded;

        static void CheckTypeValidity(Type type)
        {
            if (type == null) throw Unbound.Runtime.MonoExceptions.NullReference();
            if (type.IsAbstract)
                throw new ArgumentException("Abstract types can't be used in the ObjectFactory : " + type.FullName);
            if (Attribute.GetCustomAttribute(type, typeof(ExcludeFromObjectFactoryAttribute)) != null)
                throw new ArgumentException("The type " + type.FullName + " is not supported by the ObjectFactory.");
        }

        public static T CreateInstance<T>() where T : Object => (T)CreateInstance(typeof(T));

        public static Object CreateInstance(Type type)
        {
            CheckTypeValidity(type);
            if (type == typeof(GameObject))
                throw new ArgumentException("GameObject type must be created using ObjectFactory.CreateGameObject instead : " + type.FullName);
            if (type.IsSubclassOf(typeof(Component)))
                throw new ArgumentException("Component type must be created using ObjectFactory.AddComponent instead : " + type.FullName);
            // A script type that hides its constructor is refused; the engine's own types are made by
            // the engine whatever constructors Unbound gives them.
            if (type.Assembly != typeof(Object).Assembly
                && type.GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic, null, Type.EmptyTypes, null) != null)
                throw new ArgumentException(type.FullName + " constructor is not accessible which prevent this type from being used in ObjectFactory.");
            return CreateDefaultInstance(type);
        }

        /// <summary>A new object of the type as the Editor makes one: unnamed; a ScriptableObject
        /// through CreateInstance, a material with the error shader. Engine types without a public
        /// parameterless constructor are not supported (unmeasured).</summary>
        static Object CreateDefaultInstance(Type type)
        {
            if (type == null) throw new ArgumentNullException(nameof(type));
            Object made;
            if (typeof(ScriptableObject).IsAssignableFrom(type)) made = ScriptableObject.CreateInstance(type);
            else if (type == typeof(Material)) made = new Material(Shader.Find("Hidden/InternalErrorShader"));
            else if (type.GetConstructor(Type.EmptyTypes) != null) made = (Object)Activator.CreateInstance(type);
            else throw new NotSupportedException("Unbound: ObjectFactory.CreateInstance cannot make a default " + type.FullName + " yet");
            made.name = "";
            return made;
        }

        public static T AddComponent<T>(GameObject gameObject) where T : Component => (T)AddComponent(gameObject, typeof(T));

        public static Component AddComponent(GameObject gameObject, Type type)
        {
            CheckTypeValidity(type);
            if (!type.IsSubclassOf(typeof(Component)))
                throw new ArgumentException("Non-Component type must use ObjectFactory.CreateInstance instead : " + type.FullName);
            var component = AddDefaultComponent(gameObject, type);
            if (component != null) Undo.RegisterAddedComponent(component);
            return component;
        }

        /// <summary>Adds the component and those it requires, each taking the GameObject's hide
        /// flags and raising componentWasAdded; a second component of a kind the GameObject holds
        /// one of throws.</summary>
        static Component AddDefaultComponent(GameObject gameObject, Type type)
        {
            if (gameObject == null) throw new ArgumentNullException(nameof(gameObject));
            var existing = gameObject.GetComponent(type);
            if (existing != null && (type == typeof(Transform) || Unbound.Runtime.Compat.NativeComponentIsUnique(type)))
                throw new ArgumentException($"Can't add component '{type.Name}' to {gameObject.name} because such a component is already added to the game object!");
            for (var t = type; t != null && t != typeof(MonoBehaviour) && t != typeof(Component); t = t.BaseType)
            {
                if (t.GetCustomAttribute<DisallowMultipleComponent>(inherit: false) == null) continue;
                var same = gameObject.GetComponent(t);
                if (same != null)
                    throw new ArgumentException($"Can't add '<NULL>' to {gameObject.name} because a '{same.GetType().Name}' is already added to the game object!");
            }
            var before = new HashSet<Component>(gameObject.GetComponents<Component>());
            var component = gameObject.AddComponent(type);
            foreach (var added in gameObject.GetComponents<Component>())
            {
                if (before.Contains(added)) continue;
                added.hideFlags = gameObject.hideFlags;
                componentWasAdded?.Invoke(added);
            }
            return component;
        }

        public static GameObject CreateGameObject(Scene scene, HideFlags hideFlags, string name, params Type[] types)
        {
            if (!scene.IsValid()) throw new ArgumentException("Cannot create a GameObject to a null Scene.");
            var gameObject = CreateGameObject(name, types);
            if (gameObject.scene != scene) SceneManager.MoveGameObjectToScene(gameObject, scene);
            gameObject.hideFlags = hideFlags;
            foreach (var component in gameObject.GetComponents<Component>()) component.hideFlags = hideFlags;
            return gameObject;
        }

        public static GameObject CreateGameObject(string name, params Type[] types)
        {
            var gameObject = CreateDefaultGameObject(name);
            gameObject.SetActive(false);
            foreach (var type in types) AddComponent(gameObject, type);
            gameObject.SetActive(true);
            return gameObject;
        }

        /// <summary>A new GameObject with its Transform, in the current undo group.</summary>
        static GameObject CreateDefaultGameObject(string name)
        {
            var gameObject = new GameObject(name ?? "");
            Undo.RegisterCreatedObjectUndo(gameObject, "Create " + gameObject.name);
            componentWasAdded?.Invoke(gameObject.transform);
            return gameObject;
        }

        /// <summary>A primitive as the Editor's menu makes it: the built-in mesh, the default
        /// material and the shape's collider, each component added as AddComponent adds it.</summary>
        public static GameObject CreatePrimitive(PrimitiveType type)
        {
            var gameObject = CreateGameObject(type.ToString(), typeof(MeshFilter), typeof(MeshRenderer));
            gameObject.SetActive(false);
            var mesh = Unbound.Runtime.Primitives.MeshOf(type);
            gameObject.GetComponent<MeshFilter>().sharedMesh = mesh;
            Unbound.Runtime.Primitives.AddCollider(gameObject, type, mesh, collider => AddComponent(gameObject, collider));
            gameObject.GetComponent<MeshRenderer>().sharedMaterial = Unbound.Runtime.Primitives.DefaultMaterial();
            gameObject.SetActive(true);
            return gameObject;
        }

        /// <summary>Places a new GameObject: under <paramref name="parent"/> at its position; without
        /// one where it is (no Scene view gives a placement).</summary>
        public static void PlaceGameObject(GameObject go, GameObject parent = null)
        {
            if (go is null) throw Unbound.Runtime.MonoExceptions.NullReference();
            var transform = go.transform;
            if (parent == null) return;
            transform.SetParent(parent.transform, false);
            transform.localPosition = Vector3.zero;
        }
    }
}
