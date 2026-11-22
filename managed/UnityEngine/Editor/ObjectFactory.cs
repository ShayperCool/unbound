using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEditor.SceneManagement;
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
            if (type.FullName == "UnityEngine.ProceduralMaterial")
                throw new ArgumentException("'type' parameter is not a valid UnityEngine.Object type : " + type.Name);
            // An engine type the Editor makes without a public parameterless constructor is in the
            // measured table; any other engine type without one, and a script type that hides its
            // parameterless constructor, is refused (tests/projects/factoryinstances).
            bool engine = type.Assembly == typeof(Object).Assembly;
            if (engine ? !s_EngineDefaults.Contains(type.FullName) && type.GetConstructor(Type.EmptyTypes) == null
                       : type.GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic, null, Type.EmptyTypes, null) != null)
                throw new ArgumentException(type.FullName + " constructor is not accessible which prevent this type from being used in ObjectFactory.");
            return CreateDefaultInstance(type);
        }

        /// <summary>The engine types without a public parameterless constructor the Unity
        /// 6000.0.71f1 Editor's factory makes (tests/projects/factoryinstances).</summary>
        static readonly HashSet<string> s_EngineDefaults = new HashSet<string>(StringComparer.Ordinal)
        {
            "UnityEngine.Texture2D", "UnityEngine.Texture3D", "UnityEngine.Texture2DArray", "UnityEngine.Cubemap",
            "UnityEngine.CubemapArray", "UnityEngine.CustomRenderTexture", "UnityEngine.SparseTexture",
            "UnityEngine.SpeedTreeWindAsset", "UnityEditor.Presets.Preset", "UnityEngine.Material",
        };

        /// <summary>A new object of the type as the Editor makes one: unnamed; a ScriptableObject
        /// through CreateInstance, a material with the error shader, an engine type through its
        /// parameterless constructor. An engine type Unbound has no parameterless constructor for
        /// (SparseTexture, SpeedTreeWindAsset, Preset) is not supported yet and says so.</summary>
        static Object CreateDefaultInstance(Type type)
        {
            if (type == null) throw new ArgumentNullException(nameof(type));
            Object made;
            if (typeof(ScriptableObject).IsAssignableFrom(type)) made = ScriptableObject.CreateInstance(type);
            else if (type == typeof(Material)) made = new Material(Shader.Find("Hidden/InternalErrorShader"));
            else if (type.GetConstructor(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null) != null)
                made = (Object)Activator.CreateInstance(type, nonPublic: true);
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
            return AddDefaultComponent(gameObject, type);
        }

        /// <summary>Adds the component and those it requires, each recorded for undo, taking the
        /// GameObject's hide flags and raising componentWasAdded. A component the GameObject
        /// cannot take again throws (the measured texts for one-of-a-kind engine components and
        /// DisallowMultipleComponent classes; the add's own text for other limits, unmeasured).</summary>
        static Component AddDefaultComponent(GameObject gameObject, Type type)
        {
            if (gameObject == null) throw new ArgumentNullException(nameof(gameObject));
            var duplicate = Unbound.Runtime.ComponentFactory.Duplicate(gameObject, type, out var existing);
            if (duplicate == Unbound.Runtime.ComponentFactory.DuplicateKind.DisallowMultiple)
                throw new ArgumentException($"Can't add '<NULL>' to {gameObject.name} because a '{existing.GetType().Name}' is already added to the game object!");
            if (duplicate != Unbound.Runtime.ComponentFactory.DuplicateKind.None)
                throw new ArgumentException(Unbound.Runtime.ComponentFactory.DuplicateMessage(gameObject, type, duplicate, existing));
            var before = new HashSet<Component>(gameObject.GetComponents<Component>());
            var component = gameObject.AddComponent(type);
            foreach (var added in gameObject.GetComponents<Component>())
            {
                if (before.Contains(added)) continue;
                added.hideFlags = gameObject.hideFlags;
                Undo.RegisterCreatedObjectUndo(added, "Add " + added.GetType().Name);
                componentWasAdded?.Invoke(added);
            }
            return component;
        }

        public static GameObject CreateGameObject(Scene scene, HideFlags hideFlags, string name, params Type[] types)
        {
            if (!scene.IsValid()) throw new ArgumentException("Cannot create a GameObject to a null Scene.");
            return Create(scene, hideFlags, name, types);
        }

        public static GameObject CreateGameObject(string name, params Type[] types) => Create(null, HideFlags.None, name, types);

        /// <summary>A new GameObject (in <paramref name="scene"/>, else in the active scene) with
        /// its hide flags set before the components are added, so each takes them, and the
        /// components added while it is inactive.</summary>
        static GameObject Create(Scene? scene, HideFlags hideFlags, string name, Type[] types)
        {
            var gameObject = CreateDefaultGameObject(name);
            if (scene is Scene target && gameObject.scene != target) SceneManager.MoveGameObjectToScene(gameObject, target);
            if (hideFlags != HideFlags.None)
            {
                gameObject.hideFlags = hideFlags;
                gameObject.transform.hideFlags = hideFlags;
            }
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

        /// <summary>Places a new GameObject as the Editor's create menus do: under
        /// <paramref name="parent"/> (reparented for undo, at the parent's origin with no rotation
        /// and unit scale, on the parent's layer, with a RectTransform under a RectTransform
        /// parent); without a parent where it is (no Scene view gives it a place; in the current
        /// stage). Its name is then made unique among its siblings, the undo group named after it
        /// and the object selected.</summary>
        public static void PlaceGameObject(GameObject go, GameObject parent = null)
        {
            if (go is null) throw Unbound.Runtime.MonoExceptions.NullReference();
            if (parent != null)
            {
                var transform = go.transform;
                Undo.SetTransformParent(transform, parent.transform, "Reparenting");
                transform.localPosition = Vector3.zero;
                transform.localRotation = Quaternion.identity;
                transform.localScale = Vector3.one;
                go.layer = parent.layer;
                if (parent.GetComponent<RectTransform>() != null) AddComponent<RectTransform>(go);
            }
            else
            {
                StageUtility.PlaceGameObjectInCurrentStage(go);
            }
            GameObjectUtility.EnsureUniqueNameForSibling(go);
            Undo.SetCurrentGroupName("Create " + go.name);
            Selection.activeGameObject = go;
        }
    }
}
