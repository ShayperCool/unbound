using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UIElements;

// A property's drawers as the Unity 6000.0 Editor finds them for a PropertyField
// (tests/projects/drawerdispatch): the [CustomPropertyDrawer] classes of the drawn type, its base
// types and interfaces (those drawers only with useForChildren) and its generic type definition,
// and of each PropertyAttribute on the field, attributes in their `order`; decorators from the
// decorator drawers of the attributes. A collection's attribute drawers apply to its elements,
// unless the attribute applies to the collection; its decorators apply to the collection. One set
// is kept per target object and property path (array indices left out), so the fields of one
// property share their drawer instances, and IMGUI measures the same drawers.

namespace UnityEditor
{
    public abstract partial class PropertyDrawer
    {
        /// <summary>The property's UI Toolkit GUI; null (the default) makes PropertyField fall back
        /// to the drawer's IMGUI.</summary>
        [Unbound.EditorFaithful("Unity's base implementation")]
        public virtual VisualElement CreatePropertyGUI(SerializedProperty property) => null;
    }

    public abstract partial class DecoratorDrawer
    {
        /// <summary>The decorator's UI Toolkit GUI; null (the default) makes PropertyField fall back
        /// to its IMGUI.</summary>
        [Unbound.EditorFaithful("Unity's base implementation")]
        public virtual VisualElement CreatePropertyGUI() => null;
    }

    /// <summary>The drawers PropertyField builds for a property: its property drawers, one per
    /// nesting level (a drawer that builds a PropertyField of the same property gets the next),
    /// and its decorators.</summary>
    internal sealed class DrawerSet
    {
        internal static readonly DrawerSet Empty = new DrawerSet();

        internal List<PropertyDrawer> PropertyDrawers;
        internal List<DecoratorDrawer> Decorators;

        internal PropertyDrawer DrawerAt(int nestingLevel) =>
            PropertyDrawers != null && nestingLevel < PropertyDrawers.Count ? PropertyDrawers[nestingLevel] : null;
    }

    /// <summary>The drawers being drawn or measured with IMGUI: a drawer that draws or measures
    /// its own property again gets the next drawer of it (the next attribute's, then the
    /// default), as a PropertyField nested in a drawer does; a PropertyField's IMGUI starts at its
    /// own nesting level.</summary>
    internal static class DrawerNesting
    {
        struct Entry
        {
            public int Target;
            public string Path;
            public int Level;
        }

        static readonly List<Entry> s_Stack = new List<Entry>();

        static int TargetOf(SerializedProperty property)
        {
            var target = property.serializedObject?.targetObject;
            return target != null ? target.GetInstanceID() : 0;
        }

        /// <summary>The level of the drawer a call for this property draws or measures with.</summary>
        internal static int LevelFor(SerializedProperty property)
        {
            int target = TargetOf(property);
            string path = property.propertyPath;
            for (int i = s_Stack.Count - 1; i >= 0; i--)
                if (s_Stack[i].Target == target && s_Stack[i].Path == path) return s_Stack[i].Level + 1;
            return 0;
        }

        /// <summary>Marks a property's drawer at <paramref name="level"/> as drawing or measuring it
        /// until disposed.</summary>
        internal static Scope Enter(SerializedProperty property, int level)
        {
            s_Stack.Add(new Entry { Target = TargetOf(property), Path = property.propertyPath, Level = level });
            return new Scope(s_Stack.Count);
        }

        internal readonly struct Scope : IDisposable
        {
            readonly int m_Depth;

            public Scope(int depth) => m_Depth = depth;

            public void Dispose()
            {
                if (s_Stack.Count >= m_Depth) s_Stack.RemoveRange(m_Depth - 1, s_Stack.Count - m_Depth + 1);
            }
        }

        /// <summary>A drawer the game's code wrote (the engine's own drawers have UI Toolkit GUI
        /// only; IMGUI draws their attributes itself).</summary>
        internal static bool IsGameDrawer(GUIDrawer drawer) => drawer != null && drawer.GetType().Assembly != typeof(UnityEngine.Object).Assembly;

        /// <summary>The game drawer an IMGUI call for this property draws with, or null.</summary>
        internal static PropertyDrawer GameDrawerFor(SerializedProperty property, out int level)
        {
            level = LevelFor(property);
            var drawer = DrawerDispatch.For(property).DrawerAt(level);
            return IsGameDrawer(drawer) ? drawer : null;
        }
    }

    internal static class DrawerDispatch
    {
        readonly struct DrawerEntry
        {
            internal readonly Type Drawer;
            internal readonly Type[] Pipelines;
            internal readonly bool ForChildren;

            internal DrawerEntry(Type drawer, Type[] pipelines, bool forChildren)
            {
                Drawer = drawer;
                Pipelines = pipelines;
                ForChildren = forChildren;
            }
        }

        static Dictionary<Type, DrawerEntry[]> s_Table;
        static int s_TableGeneration = -1;
        static readonly Dictionary<(Type, Type, Type, bool), Type> s_DrawerForType = new Dictionary<(Type, Type, Type, bool), Type>();
        static readonly Dictionary<(int, string, int), DrawerSet> s_Sets = new Dictionary<(int, string, int), DrawerSet>();
        static readonly Regex s_ArrayIndex = new Regex(@"\.Array\.data\[[0-9]+\]", RegexOptions.Compiled);

        // The drawer classes by the type they draw, built from the loaded assemblies (again after
        // the game's code changes).
        static Dictionary<Type, DrawerEntry[]> Table
        {
            get
            {
                if (s_Table != null && s_TableGeneration == Unbound.Runtime.GameDomain.Generation) return s_Table;
                var table = new Dictionary<Type, List<DrawerEntry>>();
                foreach (var drawer in TypeCache.GetTypesDerivedFrom<GUIDrawer>())
                {
                    var pipelines = drawer.GetCustomAttribute<SupportedOnRenderPipelineAttribute>()?.renderPipelineTypes;
                    foreach (var attribute in drawer.GetCustomAttributes<CustomPropertyDrawer>(false))
                    {
                        if (attribute.m_Type == null) continue;
                        if (!table.TryGetValue(attribute.m_Type, out var list)) table[attribute.m_Type] = list = new List<DrawerEntry>();
                        list.Add(new DrawerEntry(drawer, pipelines, attribute.m_UseForChildren));
                    }
                }
                s_Table = table.ToDictionary(p => p.Key, p => p.Value.ToArray());
                s_TableGeneration = Unbound.Runtime.GameDomain.Generation;
                s_DrawerForType.Clear();
                s_Sets.Clear();
                return s_Table;
            }
        }

        /// <summary>The drawers of a property; none without a target. A native object's properties
        /// have only the Editor's built-in attributes (a TextMesh's text is [Multiline]). (Unbound
        /// has no debug inspector, where the Editor uses no drawers.)</summary>
        internal static DrawerSet For(SerializedProperty property)
        {
            if (property == null || !property.isValid) return DrawerSet.Empty;
            var serializedObject = property.serializedObject;
            var target = serializedObject.targetObject;
            if (target == null) return DrawerSet.Empty;
            bool script = target is MonoBehaviour || target is ScriptableObject;
            if (!script && !(target is TextMesh && property.propertyPath == "m_Text")) return DrawerSet.Empty;
            var table = Table;
            int referenceId = property.propertyType == SerializedPropertyType.ObjectReference ? property.objectReferenceInstanceIDValue : 0;
            var key = (target.GetInstanceID(), s_ArrayIndex.Replace(property.propertyPath, ".Array.data[]"), referenceId);
            if (s_Sets.TryGetValue(key, out var set)) return set;

            set = new DrawerSet();
            var field = script ? property.UnboundDeclaringField : null;
            var drawnType = DrawnType(property, field);
            var (pipelines, pipelineOwner) = PipelinesFor(target);
            IEnumerable<PropertyAttribute> attributes = script
                ? field?.GetCustomAttributes<PropertyAttribute>(true).OrderBy(a => a.order) // stable in `order`, as the Editor sorts them
                : new PropertyAttribute[] { new MultilineAttribute() };
            if (attributes != null)
            {
                foreach (var attribute in attributes)
                {
                    if (attribute is TooltipAttribute || attribute is ContextMenuItemAttribute) continue;
                    bool collection = drawnType != null && IsArrayOrList(drawnType);
                    bool element = property.propertyPath.EndsWith("]", StringComparison.Ordinal);
                    if (!attribute.applyToCollection ? collection : !collection && element) continue;
                    AddDrawnType(set, property, attribute.GetType(), drawnType, field, attribute, pipelines, pipelineOwner, table);
                }
            }
            if (drawnType != null) AddDrawnType(set, property, drawnType, drawnType, field, null, pipelines, pipelineOwner, table);
            s_Sets[key] = set.PropertyDrawers == null && set.Decorators == null ? DrawerSet.Empty : set;
            return s_Sets[key];
        }

        static void AddDrawnType(DrawerSet set, SerializedProperty property, Type drawnType, Type propertyType, FieldInfo field,
            PropertyAttribute attribute, Type[] pipelines, Type pipelineOwner, Dictionary<Type, DrawerEntry[]> table)
        {
            var drawerType = DrawerForType(drawnType, pipelines, pipelineOwner, property.propertyType == SerializedPropertyType.ManagedReference, table);
            if (drawerType == null) return;
            if (typeof(PropertyDrawer).IsAssignableFrom(drawerType))
            {
                if (propertyType != null && IsArrayOrList(propertyType) && (attribute == null || !attribute.applyToCollection)) return;
                var drawer = (PropertyDrawer)Activator.CreateInstance(drawerType);
                ReportDefaultReferences(drawerType);
                drawer.m_FieldInfo = field;
                drawer.m_Attribute = attribute;
                (set.PropertyDrawers ??= new List<PropertyDrawer>()).Add(drawer);
            }
            else if (typeof(DecoratorDrawer).IsAssignableFrom(drawerType)
                && (field == null || !IsArrayOrList(field.FieldType) || (propertyType != null && IsArrayOrList(propertyType))))
            {
                var decorator = (DecoratorDrawer)Activator.CreateInstance(drawerType);
                decorator.m_Attribute = attribute;
                (set.Decorators ??= new List<DecoratorDrawer>()).Add(decorator);
            }
        }

        static readonly HashSet<Type> s_Checked = new HashSet<Type>();

        // The Editor sets a new drawer's object fields to the default references its script's import
        // settings name; Unbound does not read those, so a drawer that has such fields is reported.
        static void ReportDefaultReferences(Type drawerType)
        {
            if (!s_Checked.Add(drawerType)) return;
            foreach (var f in drawerType.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (!typeof(UnityEngine.Object).IsAssignableFrom(f.FieldType)) continue;
                if (!f.IsPublic && !f.IsDefined(typeof(SerializeField), false)) continue;
                Unbound.Runtime.Stubs.NotImplemented($"UnityEditor.PropertyDrawer (default references of {drawerType.Name})");
                return;
            }
        }

        /// <summary>The type a property draws: its field's type, an element's element type, a
        /// managed reference's instance type.</summary>
        internal static Type DrawnType(SerializedProperty property, FieldInfo field)
        {
            if (field == null) return null;
            var type = field.FieldType;
            if (field != property.UnboundField && IsArrayOrList(type)) type = ElementType(type);
            if (property.propertyType == SerializedPropertyType.ManagedReference && property.managedReferenceValue is object value)
                type = value.GetType();
            return type;
        }

        // The render pipelines a target's drawers are chosen for: the target type's own
        // [SupportedOnRenderPipeline] (the type keys the lookup cache), else the active pipeline.
        static (Type[] pipelines, Type owner) PipelinesFor(UnityEngine.Object target)
        {
            var own = target?.GetType().GetCustomAttribute<SupportedOnRenderPipelineAttribute>()?.renderPipelineTypes;
            if (own != null) return (own, target.GetType());
            var current = GraphicsSettings.currentRenderPipelineAssetType;
            return (GraphicsSettings.isScriptableRenderPipelineEnabled && current != null ? new[] { current } : null, null);
        }

        internal static bool IsArrayOrList(Type type) =>
            type.IsArray || (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>));

        static Type ElementType(Type type) => type.IsArray ? type.GetElementType() : type.GetGenericArguments()[0];

        // The drawer class of a type: the type's own, else (useForChildren drawers only) a base
        // type's, nearest first, with the interfaces the type adds at each level, the least derived
        // first and by name; a generic type also by its definition.
        static Type DrawerForType(Type propertyType, Type[] pipelines, Type pipelineOwner, bool managedReference, Dictionary<Type, DrawerEntry[]> table)
        {
            var current = GraphicsSettings.currentRenderPipelineAssetType;
            var key = (propertyType, current, pipelineOwner, managedReference);
            if (s_DrawerForType.TryGetValue(key, out var cached)) return cached;
            Type[] interfaces = null;
            bool[] tried = null;
            Type found = null;
            for (var type = propertyType; type != null && found == null; type = type.BaseType)
            {
                bool requested = type == propertyType;
                if (TryDrawer(requested, pipelines, managedReference, type, current, table, out found)) break;
                if (requested)
                {
                    interfaces = type.GetInterfaces();
                    tried = new bool[interfaces.Length];
                    var levels = new Dictionary<Type, int>();
                    foreach (var i in interfaces) InheritanceLevel(i, 0, levels);
                    Array.Sort(interfaces, (x, y) =>
                    {
                        int byLevel = levels[x].CompareTo(levels[y]);
                        return byLevel != 0 ? byLevel : string.CompareOrdinal(x.Name, y.Name);
                    });
                }
                if ((!requested || type.IsInterface) && interfaces != null && interfaces.Length != 0)
                {
                    var own = type.IsInterface ? null : type.GetInterfaces();
                    for (int i = 0; i < interfaces.Length && found == null; i++)
                    {
                        if (tried[i]) continue;
                        if (own != null && own.Contains(interfaces[i])) continue;
                        tried[i] = true;
                        TryDrawer(requested, pipelines, managedReference, interfaces[i], current, table, out found);
                    }
                }
            }
            s_DrawerForType[key] = found;
            return found;
        }

        static int InheritanceLevel(Type interfaceType, int level, Dictionary<Type, int> levels)
        {
            var bases = interfaceType.GetInterfaces();
            int result = level;
            if (bases.Length != 0)
            {
                int lowest = int.MaxValue;
                foreach (var b in bases) lowest = Math.Min(lowest, InheritanceLevel(b, level + 1, levels));
                result = lowest - 1;
            }
            if (levels.TryGetValue(interfaceType, out var existing) && existing >= result) return existing;
            levels[interfaceType] = result;
            return result;
        }

        static bool TryDrawer(bool requested, Type[] pipelines, bool managedReference, Type type, Type current,
            Dictionary<Type, DrawerEntry[]> table, out Type drawer)
        {
            if (TryDrawerOf(requested, pipelines, managedReference, type, current, table, out drawer)) return true;
            return type.IsGenericType && TryDrawerOf(requested, pipelines, managedReference, type.GetGenericTypeDefinition(), current, table, out drawer);
        }

        static bool TryDrawerOf(bool requested, Type[] pipelines, bool managedReference, Type type, Type current,
            Dictionary<Type, DrawerEntry[]> table, out Type drawer)
        {
            drawer = null;
            if (!table.TryGetValue(type, out var entries)) return false;
            if (!TryPick(pipelines, entries, current, out var entry)) return false;
            if (!requested && !entry.ForChildren && !managedReference) return false;
            drawer = entry.Drawer;
            return true;
        }

        // A drawer for the active render pipeline first, then one its base pipeline supports, then
        // the first without a pipeline.
        static bool TryPick(Type[] pipelines, DrawerEntry[] entries, Type current, out DrawerEntry picked)
        {
            DrawerEntry? supported = null, byBase = null, any = null;
            foreach (var entry in entries)
            {
                if (entry.Pipelines == null)
                {
                    any ??= entry;
                    continue;
                }
                if (pipelines == null) continue;
                foreach (var pipeline in pipelines)
                {
                    switch (SupportedOnRenderPipelineAttribute.GetSupportedMode(entry.Pipelines, pipeline))
                    {
                        case SupportedOnRenderPipelineAttribute.SupportedMode.Supported:
                            if (supported == null || pipeline == current) supported = entry;
                            break;
                        case SupportedOnRenderPipelineAttribute.SupportedMode.SupportedByBaseClass:
                            byBase ??= entry;
                            break;
                    }
                }
            }
            picked = supported ?? byBase ?? any ?? default;
            return supported != null || byBase != null || any != null;
        }
    }
}
