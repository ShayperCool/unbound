using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using Unbound.Runtime;
using UnityEditor.Callbacks;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Internal;
using Object = UnityEngine.Object;

namespace UnityEditor.Callbacks
{
    /// <summary>Whether an <see cref="OnOpenAssetAttribute"/> method opens an asset or tells
    /// whether the Editor opens it.</summary>
    public enum OnOpenAssetAttributeMode
    {
        Execute = 0,
        Validate = 1,
    }

    /// <summary>Calls a static method when an asset is opened (<see cref="AssetDatabase.OpenAsset(int)"/>),
    /// or, in Validate mode, when the Editor is asked whether it opens an asset itself
    /// (<see cref="AssetDatabase.CanOpenAssetInEditor"/>): lower orders first.</summary>
    public sealed partial class OnOpenAssetAttribute : CallbackOrderAttribute
    {
        OnOpenAssetAttributeMode m_AttributeMode;

        public OnOpenAssetAttribute() : this(OnOpenAssetAttributeMode.Execute) { }

        public OnOpenAssetAttribute(OnOpenAssetAttributeMode attributeMode) : this(DefaultOrder, attributeMode) { }

        public OnOpenAssetAttribute(int callbackOrder) : this(callbackOrder, OnOpenAssetAttributeMode.Execute) { }

        public OnOpenAssetAttribute(int callbackOrder, OnOpenAssetAttributeMode attributeMode)
        {
            m_CallbackOrder = callbackOrder;
            m_AttributeMode = attributeMode;
        }

        /// <summary>The order of a method that names none: after order 0, before 3 (matches the
        /// Unity 6000.0.71f1 Editor, tests/projects/openasset open.unhandled:*).</summary>
        internal const int DefaultOrder = 1;

        internal OnOpenAssetAttributeMode attributeMode => m_AttributeMode;
    }
}

namespace UnityEditor
{
    public sealed partial class AssetDatabase
    {
        [ExcludeFromDocs]
        public static bool OpenAsset(int instanceID) => OpenAsset(instanceID, -1);

        public static bool OpenAsset(int instanceID, [DefaultValue("-1")] int lineNumber) => OpenAsset(instanceID, lineNumber, -1);

        public static bool OpenAsset(int instanceID, int lineNumber, int columnNumber) => AssetOpening.Open(instanceID, lineNumber, columnNumber);

        [ExcludeFromDocs]
        public static bool OpenAsset(Object target) => OpenAsset(target, -1);

        public static bool OpenAsset(Object target, [DefaultValue("-1")] int lineNumber) => OpenAsset(target, lineNumber, -1);

        public static bool OpenAsset(Object target, int lineNumber, int columnNumber) =>
            target && OpenAsset(target.GetInstanceID(), lineNumber, columnNumber);

        /// <summary>Opens every object, whatever the earlier ones answered; true when all opened.</summary>
        public static bool OpenAsset(Object[] objects)
        {
            bool all = true;
            foreach (var target in objects)
                if (!OpenAsset(target)) all = false;
            return all;
        }

        public static bool CanOpenAssetInEditor(int instanceID) => AssetOpening.CanOpen(instanceID);
    }

    /// <summary>
    /// Opening an asset as the Unity 6000.0.71f1 Editor does in Play mode (tests/projects/openasset).
    /// <para>An ID no live object has opens nothing. An object that is no asset of the project is
    /// selected and counts as opened, with no method called. An asset goes to the Execute
    /// [OnOpenAsset] methods by ascending order until one answers true, which leaves the
    /// selection alone; when none does, see <see cref="OpenUnhandled"/>.</para>
    /// <para>The Editor opens folders, scenes, assembly definitions, sprite atlases and prefabs
    /// that are no models itself, which the Validate methods are not asked about; for any other
    /// asset they are, by ascending order until one answers true, and when none does the answer
    /// is whether the asset is of the Editor's own format.</para>
    /// </summary>
    static class AssetOpening
    {
        internal static bool Open(int instanceID, int line, int column)
        {
            var target = EditorUtility.InstanceIDToObject(instanceID);
            if (target == null) return false;
            var path = AssetDatabase.GetAssetPath(target);
            if (!InProject(path))
            {
                Selection.activeObject = target;
                return true;
            }
            return Ask(OnOpenAssetAttributeMode.Execute, instanceID, line, column) || OpenUnhandled(target, path);
        }

        internal static bool CanOpen(int instanceID)
        {
            var target = EditorUtility.InstanceIDToObject(instanceID);
            if (target == null) return false;
            var path = AssetDatabase.GetAssetPath(target);
            if (!InProject(path)) return false;
            if (AssetDatabase.IsValidFolder(path) || OpensInEditor(target, path)) return true;
            return Ask(OnOpenAssetAttributeMode.Validate, instanceID, -1, -1) || AssetDatabase.IsNativeAsset(target);
        }

        /// <summary>Whether the path is an asset the project's database lists (a built-in resource
        /// and a project settings object count as none: not measured).</summary>
        static bool InProject(string path) =>
            path.Length > 0 && AssetDatabase.AssetPathToGUID(path, AssetPathToGUIDOptions.OnlyExistingAssets).Length > 0;

        /// <summary>The objects the Editor has its own window or mode for: a scene, a sprite
        /// atlas, a GameObject that is no part of a model, and an assembly definition or reference
        /// (the one object of its file, known by the file's importer).</summary>
        static bool OpensInEditor(Object target, string path) =>
            target is SceneAsset || target is UnityEngine.U2D.SpriteAtlas || target is GameObject && !PrefabUtility.IsPartOfModelPrefab(target)
            || AssetDatabase.ImporterOf(path) is "AssemblyDefinitionImporter" or "AssemblyDefinitionReferenceImporter";

        /// <summary>An asset no method opened. A folder, and an asset of the Editor's own format,
        /// is selected and counts as opened (the Editor also shows a folder in its Project window
        /// and an Audio Mixer in its window, which Unbound has not). A scene the Editor loads and a
        /// prefab it opens in Prefab Mode: Unbound does neither and says so. Any other file goes to
        /// an application outside, as from the Editor (not measured: the probe starts none).</summary>
        static bool OpenUnhandled(Object target, string path)
        {
            if (AssetDatabase.IsValidFolder(path) || AssetDatabase.IsNativeAsset(target))
            {
                if (target is UnityEngine.Audio.AudioMixer) Stubs.Ignored("AssetDatabase.OpenAsset of an AudioMixer", "opens no Audio Mixer window");
                Selection.activeObject = target;
                return true;
            }
            if (target is SceneAsset)
            {
                Debug.LogWarning($"{Log.DiagnosticPrefix}AssetDatabase.OpenAsset does not load the scene '{path}' yet; use EditorSceneManager.OpenScene.");
                return false;
            }
            if (target is GameObject && !PrefabUtility.IsPartOfModelPrefab(target))
            {
                Debug.LogWarning($"{Log.DiagnosticPrefix}AssetDatabase.OpenAsset does not open the prefab '{path}': Unbound has no Prefab Mode.");
                return false;
            }
            return DesktopOpener.Open(AssetDatabase.FileOf(path), "AssetDatabase.OpenAsset");
        }

        sealed class Callback
        {
            internal MethodInfo Method;
            internal int Order;
            internal OnOpenAssetAttributeMode Mode;
            /// <summary>What is logged for a method of another shape, which is never called.</summary>
            internal string ShapeError;
        }

        static Callback[] s_Callbacks;
        static int s_Generation = -1;

        /// <summary>The [OnOpenAsset] methods of the loaded scripts by ascending order (methods of
        /// one order as the scripts list them; Unity leaves their order open), found once per
        /// game domain.</summary>
        static Callback[] Callbacks()
        {
            if (s_Callbacks != null && s_Generation == GameDomain.Generation) return s_Callbacks;
            var found = new List<Callback>();
            foreach (var method in TypeCache.GetMethodsWithAttribute<OnOpenAssetAttribute>())
            {
                var attribute = method.GetCustomAttribute<OnOpenAssetAttribute>();
                found.Add(new Callback
                {
                    Method = method, Order = attribute.callbackOrder, Mode = attribute.attributeMode,
                    ShapeError = HasShape(method) ? null : ShapeError(method),
                });
            }
            // A stable sort: List.Sort is not.
            var sorted = new Callback[found.Count];
            for (int i = 0; i < found.Count; i++)
            {
                int at = i;
                while (at > 0 && sorted[at - 1].Order > found[i].Order) { sorted[at] = sorted[at - 1]; at--; }
                sorted[at] = found[i];
            }
            s_Generation = GameDomain.Generation;
            return s_Callbacks = sorted;
        }

        /// <summary>The shapes the attribute names, in either mode: a static method answering a
        /// bool that takes the instance ID, and after it the line, and after that the column (a
        /// generic method, which cannot be called so, counts as another shape: not measured).</summary>
        static bool HasShape(MethodInfo method)
        {
            if (!method.IsStatic || method.ReturnType != typeof(bool) || method.ContainsGenericParameters) return false;
            var parameters = method.GetParameters();
            if (parameters.Length < 1 || parameters.Length > 3) return false;
            foreach (var parameter in parameters)
                if (parameter.ParameterType != typeof(int)) return false;
            return true;
        }

        static string ShapeError(MethodInfo method)
        {
            var text = new StringBuilder("Error: ");
            UnityStackTrace.AppendMethod(text, method);
            text.Append(" does not match any of [OnOpenAssetAttribute] expected signatures.\nValid signatures are: ");
            for (int count = 1; count <= 3; count++)
            {
                if (count > 1) text.Append(" , ");
                text.Append("static Boolean ").Append(method.Name).Append('(');
                for (int i = 0; i < count; i++) text.Append(i > 0 ? ", Int32" : "Int32");
                text.Append(')');
            }
            return text.ToString();
        }

        /// <summary>Asks the methods of one mode in order until one answers true. A method of
        /// another shape is reported, on every call, and passed over; one that throws is logged
        /// and counts as answering false.</summary>
        static bool Ask(OnOpenAssetAttributeMode mode, int instanceID, int line, int column)
        {
            var callbacks = Callbacks();
            foreach (var callback in callbacks)
                if (callback.Mode == mode && callback.ShapeError != null) Debug.LogError(callback.ShapeError);
            foreach (var callback in callbacks)
            {
                if (callback.Mode != mode || callback.ShapeError != null) continue;
                int count = callback.Method.GetParameters().Length;
                var arguments = count == 1 ? new object[] { instanceID }
                    : count == 2 ? new object[] { instanceID, line }
                    : new object[] { instanceID, line, column };
                try
                {
                    if ((bool)callback.Method.Invoke(null, arguments)) return true;
                }
                catch (TargetInvocationException e) when (e.InnerException != null)
                {
                    var thrown = UnityStackTrace.ExceptionText(e.InnerException);
                    var text = new StringBuilder("Exception thrown while invoking [OnOpenAssetAttribute] method '");
                    UnityStackTrace.AppendMethod(text, callback.Method);
                    text.Append("' : ").Append(thrown.Message);
                    Log.Write(LogType.Exception, text.ToString(), thrown.Trace, null, e.InnerException);
                }
            }
            return false;
        }
    }
}
