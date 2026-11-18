using System;
using System.Collections.Generic;
using System.Reflection;
using Unbound.Runtime;
using UnityEngine;

namespace UnityEditor
{
    internal partial struct Annotation
    {
        public int iconEnabled;
        public int gizmoEnabled;
        public int flags;
        public int classID;
        public string scriptClass;
    }

    /// <summary>
    /// The Scene view's gizmo and icon settings per class (what the Gizmos menu lists): the engine's
    /// classes that draw a gizmo or an icon, then the scripts that draw gizmos (a MonoBehaviour with
    /// OnDrawGizmos or OnDrawGizmosSelected; class 114 and the script's name), all enabled at
    /// first. Setting a flag a class does not have changes nothing; a class that is not listed logs
    /// "Warning: Annotation not found!" (Unity 6000.0.71f1, tests/projects/gameinterop annotation.*).
    /// The engine's list is measured for the 6000.0 family only; other families list scripts alone.
    /// </summary>
    internal static partial class AnnotationUtility
    {
        internal enum Flags
        {
            kHasIcon = 1 << 0,
            kHasGizmo = 1 << 1,
            kIsDisabled = 1 << 2,
        }

        const int MonoBehaviourClassId = 114;

        static List<Annotation> s_Annotations;
        static int s_Generation = -1;

        static List<Annotation> Table()
        {
            if (s_Annotations != null && s_Generation == GameDomain.Generation) return s_Annotations;
            var previous = s_Annotations;
            s_Generation = GameDomain.Generation;
            s_Annotations = new List<Annotation>();
            if (Compat.AnnotationTableMeasured)
                foreach (var (classId, flags) in Compat.BuiltinAnnotations)
                    s_Annotations.Add(new Annotation { classID = classId, scriptClass = "", flags = flags, iconEnabled = 1, gizmoEnabled = 1 });
            else
                Stubs.NotImplemented("AnnotationUtility: the engine classes' gizmo annotations of this release");
            var scripts = new SortedSet<string>(StringComparer.Ordinal);
            foreach (var assembly in GameDomain.Assemblies())
                foreach (var type in GameDomain.SafeTypes(assembly))
                    if (DrawsGizmos(type)) scripts.Add(type.Name);
            foreach (var script in scripts)
                s_Annotations.Add(new Annotation { classID = MonoBehaviourClassId, scriptClass = script, flags = (int)Flags.kHasGizmo, iconEnabled = 1, gizmoEnabled = 1 });
            // A reload keeps what was set for the classes that are still there.
            if (previous != null)
                for (int i = 0; i < s_Annotations.Count; i++)
                {
                    var a = s_Annotations[i];
                    var old = previous.Find(p => p.classID == a.classID && p.scriptClass == a.scriptClass);
                    if (old.scriptClass == null) continue;
                    a.iconEnabled = old.iconEnabled;
                    a.gizmoEnabled = old.gizmoEnabled;
                    s_Annotations[i] = a;
                }
            return s_Annotations;
        }

        static bool DrawsGizmos(Type type)
        {
            try
            {
                if (type.IsAbstract || type.IsGenericTypeDefinition || !typeof(MonoBehaviour).IsAssignableFrom(type)) return false;
                const BindingFlags declared = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
                for (var t = type; t != null && t != typeof(MonoBehaviour); t = t.BaseType)
                    if (t.GetMethod("OnDrawGizmos", declared, null, Type.EmptyTypes, null) != null
                        || t.GetMethod("OnDrawGizmosSelected", declared, null, Type.EmptyTypes, null) != null) return true;
            }
            catch (Exception)
            {
                // A type whose base does not load draws nothing.
            }
            return false;
        }

        internal static Annotation[] GetAnnotations() => Table().ToArray();

        internal static Annotation GetAnnotation(int classID, string scriptClass)
        {
            var i = Find(classID, scriptClass);
            return i < 0 ? default : s_Annotations[i];
        }

        static int Find(int classID, string scriptClass)
        {
            var table = Table();
            for (int i = 0; i < table.Count; i++)
                if (table[i].classID == classID && table[i].scriptClass == (scriptClass ?? "")) return i;
            return -1;
        }

        internal static void SetIconEnabled(int classID, string scriptClass, int iconEnabled) => Set(classID, scriptClass, Flags.kHasIcon, iconEnabled);

        internal static void SetGizmoEnabled(int classID, string scriptClass, int gizmoEnabled, bool addToMostRecentChanged) => Set(classID, scriptClass, Flags.kHasGizmo, gizmoEnabled);

        static void Set(int classID, string scriptClass, Flags flag, int value)
        {
            var i = Find(classID, scriptClass);
            if (i < 0)
            {
                Debug.Log("Warning: Annotation not found!");
                return;
            }
            var a = s_Annotations[i];
            if ((a.flags & (int)flag) == 0) return;
            if (flag == Flags.kHasIcon) a.iconEnabled = value;
            else a.gizmoEnabled = value;
            s_Annotations[i] = a;
        }
    }
}
