using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using Unbound.Runtime;
using UnityEngine;
using Object = UnityEngine.Object;

namespace UnityEditor
{
    /// <summary>
    /// An editable view of the serialized data of one or more objects, as the Editor gives it to
    /// editor and importer code. The data is read into a buffer (on construction and Update); writes
    /// through SerializedProperty change the buffer only, and ApplyModifiedProperties writes the
    /// changed properties back into every target object.
    /// </summary>
    public partial class SerializedObject : IDisposable
    {
        Object[] m_Targets;
        Object m_Context;
        internal SerializedNode m_UnboundRoot;
        /// <summary>Bumped whenever nodes may have moved (Update, array changes); properties then
        /// find their node again by path.</summary>
        internal int m_UnboundTreeVersion;
        uint m_ObjectVersion;
        bool m_Modified;
        bool m_IsDifferentCacheDirty;
        SerializedNode[] m_OtherRoots;

        public SerializedObject(Object obj) : this(new[] { obj }, null) { }

        public SerializedObject(Object obj, Object context) : this(new[] { obj }, context) { }

        public SerializedObject(Object[] objs) : this(objs, null) { }

        public SerializedObject(Object[] objs, Object context)
        {
            if (objs == null) throw new ArgumentNullException(nameof(objs));
            if (objs.Length == 0) throw new ArgumentException("No objects to serialize.", nameof(objs));
            for (int i = 0; i < objs.Length; i++)
                if (objs[i] == null) throw new ArgumentException($"Object at index {i} is null");
            m_Targets = (Object[])objs.Clone();
            m_Context = context;
            m_NativeObjectPtr = new IntPtr(System.Threading.Interlocked.Increment(ref s_NextNativeObject));
            Read();
        }

        /// <summary>Non-zero while the object is usable, zero once disposed (measured:
        /// tests/projects/scenesave nativePtr; editor code checks it to tell a disposed one).</summary>
        internal IntPtr m_NativeObjectPtr;
        static long s_NextNativeObject;

        internal uint objectVersion => m_ObjectVersion;

        internal bool isValid => m_NativeObjectPtr != IntPtr.Zero && m_Targets != null && m_Targets.Length > 0 && m_Targets[0] != null;

        void Read()
        {
            var previous = m_UnboundRoot;
            var tree = TypeTreeTargets.Of(m_Targets[0]);
            m_UnboundRoot = tree != null ? TypeTreeSerialized.BuildRoot(tree, m_Targets[0]) : SerializedTree.BuildRoot(m_Targets[0], callbacks: true);
            m_UnboundTreeVersion++;
            m_Modified = false;
            bool dataChanged = previous == null || !SerializedTree.DataEquals(previous, m_UnboundRoot);
            if (dataChanged || m_IsDifferentCacheDirty)
            {
                m_ObjectVersion++;
                m_OtherRoots = null;
                m_IsDifferentCacheDirty = false;
            }
        }

        internal void MarkModified() => m_Modified = true;

        internal void InvalidateNodes() => m_UnboundTreeVersion++;

        public Object targetObject => m_Targets[0];

        public Object[] targetObjects => (Object[])m_Targets.Clone();

        public Object context => m_Context;

        public bool hasModifiedProperties => m_Modified;

        public bool isEditingMultipleObjects => m_Targets.Length > 1;

        public int maxArraySizeForMultiEditing { get; set; } = 1000;

        public bool forceChildVisibility { get; set; }

        public SerializedProperty GetIterator() => new SerializedProperty(this, m_UnboundRoot);

        public SerializedProperty FindProperty(string propertyPath)
        {
            var node = SerializedTree.Find(m_UnboundRoot, propertyPath);
            return node == null || node.Kind == SerializedNodeKind.Root ? null : new SerializedProperty(this, node);
        }

        /// <summary>Re-reads the targets' data, dropping changes not applied.</summary>
        public void Update() => Read();

        /// <summary>Re-reads the data if the target changed since the last read or is a script
        /// (which Unbound cannot track, so always).</summary>
        public bool UpdateIfRequiredOrScript()
        {
            Read();
            return true;
        }

        [global::System.@ObsoleteAttribute("UpdateIfDirtyOrScript has been deprecated. Use UpdateIfRequiredOrScript instead.", false)]
        public void UpdateIfDirtyOrScript() => UpdateIfRequiredOrScript();

        public bool ApplyModifiedProperties() => Apply(recordUndo: true);

        public bool ApplyModifiedPropertiesWithoutUndo() => Apply(recordUndo: false);

        /// <summary>Applies without marking the targets dirty (a prefab instance taking its
        /// prefab's values: Unbound's own write, not an edit).</summary>
        internal bool ApplyQuietly() => Apply(recordUndo: false, dirty: false);

        bool Apply(bool recordUndo = false, bool dirty = true)
        {
            if (!m_Modified) return false;
            // The recorded changes reach Undo.postprocessModifications when Undo's records are
            // flushed, after the values are applied (measured: tests/projects/scenesave postprocess,
            // filter.*, throw.*). They are the changes as this apply makes them; how the Editor
            // reports an object changed again before the flush (OnValidate, a second apply, a direct
            // set), or undone before it, is unmeasured; a handler subscribed after the apply gets
            // nothing of it.
            // An apply recorded for Undo is one undo record of its targets, named by what it
            // changes (measured: tests/projects/serializedundo).
            if (recordUndo)
            {
                var changes = Changes(out var befores);
                Undo.RecordApply(m_Targets, befores, ChangedPaths(), UndoName(changes));
                if (Undo.postprocessModifications != null) Undo.QueueModifications(Modifications(changes));
            }
            for (int i = 0; i < m_Targets.Length; i++)
            {
                var target = m_Targets[i];
                if (target == null) continue;
                var tree = TypeTreeTargets.Of(target);
                if (tree == null) SerializedTree.Apply(m_UnboundRoot, target, primary: i == 0);
                else if (i == 0) TypeTreeSerialized.Apply(m_UnboundRoot, tree);
                else
                {
                    // Another target takes what changed, over its own data.
                    var own = TypeTreeSerialized.BuildRoot(tree, target);
                    SerializedTree.CopyChanged(m_UnboundRoot, own);
                    TypeTreeSerialized.Apply(own, tree);
                }
            }
            SerializedTree.ClearDirty(m_UnboundRoot);
            m_Modified = false;
            m_ObjectVersion++;
            m_OtherRoots = null;
            m_IsDifferentCacheDirty = false;
            // Applying marks the targets dirty (Scripting API, ApplyModifiedProperties; measured:
            // tests/projects/scenesave asset.serialized), records the changes of a prefab
            // instance's objects as its overrides and, recorded for Undo, publishes them
            // (measured for a script of an instance: tests/projects/scenesave prefab-writes
            // propRevert.before.mods, propRevert.event).
            if (dirty)
                foreach (var target in m_Targets)
                {
                    if (target == null) continue;
                    EditorUtility.SetDirty(target);
                    if (Unbound.Runtime.PrefabLinks.Of(target) != null) Unbound.Runtime.PrefabRecords.Record(target);
                    if (recordUndo) ObjectChangeEvents.NoteProperties(target);
                }
            foreach (var target in m_Targets)
            {
                if (target == null || !SerializedTree.IsScript(target)) continue;
                if (target is ISerializationCallbackReceiver receiver)
                {
                    try { receiver.OnAfterDeserialize(); }
                    catch (Exception e) { Debug.LogException(e); }
                }
                CallOnValidate(target);
            }
            return true;
        }

        static void CallOnValidate(Object target) => ScriptMessages.Validate(target);

        /// <summary>One changed property of one target: its value before the apply (none when the
        /// apply adds it) and after.</summary>
        readonly struct Change
        {
            public readonly Object Target;
            public readonly string Path;
            public readonly bool Existed;
            public readonly object Before;
            public readonly object After;

            public Change(Object target, string path, bool existed, object before, object after)
            {
                Target = target;
                Path = path;
                Existed = existed;
                Before = before;
                After = after;
            }
        }

        /// <summary>The changed properties of each target, target by target, and each target's data
        /// before the apply (null for a missing target).</summary>
        List<Change> Changes(out SerializedNode[] befores)
        {
            befores = new SerializedNode[m_Targets.Length];
            var changed = new List<SerializedNode>();
            void Leaves(SerializedNode node)
            {
                if (!node.Dirty && !node.Replaced) return;
                if (node.Children.Count == 0) { changed.Add(node); return; }
                foreach (var child in node.Children) Leaves(child);
            }
            Leaves(m_UnboundRoot);
            var list = new List<Change>();
            for (int i = 0; i < m_Targets.Length; i++)
            {
                var target = m_Targets[i];
                if (target == null) continue;
                var before = befores[i] = ReadRoot(target);
                foreach (var node in changed)
                {
                    var old = SerializedTree.Find(before, node.Path);
                    list.Add(new Change(target, node.Path, old != null, old == null ? null : SerializedTree.GetValue(old), SerializedTree.GetValue(node)));
                }
            }
            return list;
        }

        /// <summary>A target's serialized data as Unbound models it.</summary>
        internal static SerializedNode ReadRoot(Object target)
        {
            var tree = TypeTreeTargets.Of(target);
            return tree != null ? TypeTreeSerialized.BuildRoot(tree, target) : SerializedTree.BuildRoot(target, callbacks: false);
        }

        /// <summary>Writes a target's data (the changed nodes of <paramref name="root"/>).</summary>
        internal static void WriteRoot(Object target, SerializedNode root)
        {
            var tree = TypeTreeTargets.Of(target);
            if (tree == null) SerializedTree.Apply(root, target, primary: true);
            else TypeTreeSerialized.Apply(root, tree);
        }

        /// <summary>The paths this apply changes: each changed value, a replaced value or a resized
        /// array whole.</summary>
        List<string> ChangedPaths()
        {
            var paths = new List<string>();
            void Walk(SerializedNode node)
            {
                if (!node.Dirty && !node.Replaced) return;
                if (node.Parent != null && (node.Replaced || node.Children.Count == 0
                    || (node.Kind == SerializedNodeKind.Array && SerializedTree.IsResized(node))))
                {
                    paths.Add(node.Path);
                    return;
                }
                foreach (var child in node.Children) Walk(child);
            }
            Walk(m_UnboundRoot);
            return paths;
        }

        /// <summary>The changes an apply records for Undo: per target, each changed property's value
        /// before and after (Undo.postprocessModifications sees them; measured:
        /// tests/projects/scenesave postprocess).</summary>
        static UndoPropertyModification[] Modifications(List<Change> changes)
        {
            var list = new UndoPropertyModification[changes.Count];
            for (int i = 0; i < changes.Count; i++)
                list[i] = new UndoPropertyModification
                {
                    previousValue = Modification(changes[i].Target, changes[i].Path, changes[i].Before),
                    currentValue = Modification(changes[i].Target, changes[i].Path, changes[i].After),
                };
            return list;
        }

        /// <summary>"Modified {property} in {object}": the one property whose value changed (its
        /// path, nicified), else their count ("3 properties"); the target's name, else the count of
        /// targets ("2 Objects"). An array's size counts, the elements it adds or removes do not
        /// (measured: tests/projects/serializedundo).</summary>
        string UndoName(List<Change> changes)
        {
            Object first = null;
            int targets = 0;
            foreach (var target in m_Targets)
            {
                if (target == null) continue;
                first ??= target;
                targets++;
            }
            string path = null;
            int count = 0;
            foreach (var change in changes)
            {
                if (!ReferenceEquals(change.Target, first) || !change.Existed || Equals(change.Before, change.After)) continue;
                path = change.Path;
                count++;
            }
            string property = count == 1 ? ObjectNames.NicifyVariableName(path) : count + " properties";
            string objects = targets > 1 ? targets + " Objects" : first != null ? first.name : "";
            return "Modified " + property + " in " + objects;
        }

        static PropertyModification Modification(Object target, string path, object value) => new PropertyModification
        {
            target = target,
            propertyPath = path,
            value = value is Object ? "" : value switch
            {
                null => "",
                bool b => b ? "1" : "0",
                float f => FloatText.Format(f),
                double d => FloatText.Format(d),
                IFormattable x => x.ToString(null, System.Globalization.CultureInfo.InvariantCulture),
                _ => value.ToString(),
            },
            objectReference = value as Object,
        };

        public void SetIsDifferentCacheDirty()
        {
            m_IsDifferentCacheDirty = true;
            m_OtherRoots = null;
        }

        public void CopyFromSerializedProperty(SerializedProperty prop)
        {
            if (prop == null) throw new ArgumentNullException(nameof(prop));
            CopyFrom(prop);
        }

        public bool CopyFromSerializedPropertyIfDifferent(SerializedProperty prop)
        {
            if (prop == null) throw new ArgumentNullException(nameof(prop));
            var target = SerializedTree.Find(m_UnboundRoot, prop.propertyPath);
            if (target != null && SerializedTree.DataEquals(target, prop.UnboundNode)) return false;
            return CopyFrom(prop);
        }

        bool CopyFrom(SerializedProperty prop)
        {
            var source = prop.UnboundNode;
            var target = SerializedTree.Find(m_UnboundRoot, source.Path);
            if (target == null || !SerializedTree.CopyData(target, source)) return false;
            m_Modified = true;
            m_ObjectVersion++;
            InvalidateNodes();
            return true;
        }

        [Unbound.EditorFaithful("a managed tree: nothing native to release, it only stops being usable")]
        [global::UnityEngine.@ThreadAndSerializationSafeAttribute]
        public void Dispose() => m_NativeObjectPtr = IntPtr.Zero;

        /// <summary>The trees of the other targets, read on demand to compare values.</summary>
        internal SerializedNode[] OtherRoots()
        {
            if (m_OtherRoots != null) return m_OtherRoots;
            var roots = new SerializedNode[m_Targets.Length - 1];
            for (int i = 1; i < m_Targets.Length; i++)
            {
                var target = m_Targets[i];
                var tree = target != null ? TypeTreeTargets.Of(target) : null;
                roots[i - 1] = target == null ? null : tree != null ? TypeTreeSerialized.BuildRoot(tree, target) : SerializedTree.BuildRoot(target, callbacks: true);
            }
            return m_OtherRoots = roots;
        }
    }

    /// <summary>
    /// A position in a SerializedObject's data: one property, by path. It reads and writes that
    /// property's value and moves through the properties in Unity's order (Next, NextVisible).
    /// </summary>
    public partial class SerializedProperty : IDisposable
    {
        internal SerializedObject m_SerializedObject;
        SerializedNode m_UnboundNode;
        string m_UnboundPath;
        int m_UnboundVersion;
        bool m_UnboundAtEnd;

        static readonly ConditionalWeakTable<Object, Dictionary<string, bool>> s_Expanded = new ConditionalWeakTable<Object, Dictionary<string, bool>>();

        internal SerializedProperty() { }

        internal SerializedProperty(SerializedObject serializedObject, SerializedNode node)
        {
            m_SerializedObject = serializedObject;
            MoveTo(node);
        }

        void MoveTo(SerializedNode node)
        {
            m_UnboundNode = node;
            m_UnboundAtEnd = node == null;
            m_UnboundPath = node?.Path;
            m_UnboundVersion = m_SerializedObject.m_UnboundTreeVersion;
        }

        /// <summary>The node this property is on; throws past the end, as Unity does.</summary>
        internal SerializedNode UnboundNode
        {
            get
            {
                if (m_UnboundAtEnd)
                    throw new InvalidOperationException("The operation is not possible when moved past all properties (Next returned false)");
                if (m_UnboundVersion != m_SerializedObject.m_UnboundTreeVersion)
                {
                    m_UnboundNode = SerializedTree.Find(m_SerializedObject.m_UnboundRoot, m_UnboundPath);
                    m_UnboundVersion = m_SerializedObject.m_UnboundTreeVersion;
                }
                if (m_UnboundNode == null)
                    throw new InvalidOperationException($"The property '{m_UnboundPath}' no longer exists in its SerializedObject.");
                return m_UnboundNode;
            }
        }

        SerializedNode N => UnboundNode;

        public SerializedObject serializedObject => m_SerializedObject;

        [Unbound.EditorFaithful("a managed tree: nothing native to release")]
        [global::UnityEngine.@ThreadAndSerializationSafeAttribute]
        public void Dispose() { }

        // ------------------------------------------------------------ description

        public string name => N.Name;

        public string displayName => N.DisplayName;

        public string propertyPath => N.Path;

        internal int hashCodeForPropertyPath => ComputePathHash(propertyPath);

        internal bool isValid
        {
            get
            {
                if (m_SerializedObject == null || !m_SerializedObject.isValid || m_UnboundAtEnd) return false;
                if (m_UnboundVersion != m_SerializedObject.m_UnboundTreeVersion)
                    return SerializedTree.Find(m_SerializedObject.m_UnboundRoot, m_UnboundPath) != null;
                return m_UnboundNode != null;
            }
        }

        static int ComputePathHash(string path)
        {
            if (string.IsNullOrEmpty(path)) return 0;
            unchecked
            {
                uint hash = 2166136261u;
                for (int i = 0; i < path.Length; i++)
                {
                    hash ^= path[i];
                    hash *= 16777619u;
                }
                return hash == 0u ? 1 : (int)hash;
            }
        }

        public string type => N.TypeName;

        public string tooltip => N.Tooltip ?? "";

        public int depth => N.Depth;

        public SerializedPropertyType propertyType => N.PropertyType;

        public SerializedPropertyNumericType numericType => SerializedTree.NumericTypeOf(N);

        public bool editable
        {
            get
            {
                _ = N;
                var target = m_SerializedObject.targetObject;
                return target != null && (target.hideFlags & HideFlags.NotEditable) == 0;
            }
        }

        [Unbound.EditorFaithful("only the Animation window's preview or recording animates properties; it is not open while playing")]
        public bool isAnimated => false;

        /// <summary>A property of an object of a prefab instance (measured: tests/projects/editorcallbacks).</summary>
        public bool isInstantiatedPrefab
        {
            get
            {
                _ = N;
                return Unbound.Runtime.PrefabLinks.Of(m_SerializedObject.targetObject) != null;
            }
        }

        /// <summary>The instance modifies the property or a part of it.</summary>
        public bool prefabOverride
        {
            get => UnityEditor.PrefabUtility.IsOverride(m_SerializedObject.targetObject, N.Path);
            set => Unbound.Runtime.Stubs.Ignored("SerializedProperty.prefabOverride", "is not honoured yet: overrides are not reverted or recorded in Play mode");
        }

        /// <summary>A property every instance overrides: the instance root's names and local pose.</summary>
        public bool isDefaultOverride => UnityEditor.PrefabUtility.IsDefaultOverrideProperty(m_SerializedObject.targetObject, N.Path);

        public bool isExpanded
        {
            get
            {
                var path = N.Path;
                var target = m_SerializedObject.targetObject;
                return target is not null && s_Expanded.TryGetValue(target, out var map) && map.TryGetValue(path, out var expanded) && expanded;
            }
            set
            {
                var path = N.Path;
                var target = m_SerializedObject.targetObject;
                if (target is null) return;
                s_Expanded.GetOrCreateValue(target)[path] = value;
            }
        }

        public bool hasChildren => N.Children.Count > 0;

        public bool hasVisibleChildren => SerializedTree.HasVisibleChildren(N);

        public bool hasMultipleDifferentValues
        {
            get
            {
                var n = N;
                if (!m_SerializedObject.isEditingMultipleObjects) return false;
                foreach (var root in m_SerializedObject.OtherRoots())
                {
                    if (root == null) continue;
                    if (!SerializedTree.DataEquals(n, SerializedTree.Find(root, n.Path))) return true;
                }
                return false;
            }
        }

        public uint contentHash => SerializedTree.ContentHash(N);

        // ------------------------------------------------------------ iteration

        public SerializedProperty Copy()
        {
            var copy = new SerializedProperty
            {
                m_SerializedObject = m_SerializedObject,
                m_UnboundNode = m_UnboundNode,
                m_UnboundPath = m_UnboundPath,
                m_UnboundVersion = m_UnboundVersion,
                m_UnboundAtEnd = m_UnboundAtEnd,
                unsafeMode = unsafeMode,
            };
            return copy;
        }

        public bool Next(bool enterChildren)
        {
            if (m_UnboundAtEnd) return false;
            var next = SerializedTree.Next(N, enterChildren);
            MoveTo(next);
            return next != null;
        }

        public bool NextVisible(bool enterChildren)
        {
            if (m_UnboundAtEnd) return false;
            var next = SerializedTree.NextVisible(N, enterChildren);
            MoveTo(next);
            return next != null;
        }

        /// <summary>Moves back to the start, before the first property.</summary>
        public void Reset() => MoveTo(m_SerializedObject.m_UnboundRoot);

        public int CountRemaining()
        {
            var p = Copy();
            int count = 0;
            while (p.NextVisible(true)) count++;
            return count;
        }

        public int CountInProperty()
        {
            var p = Copy();
            var end = GetEndProperty();
            int count = 1;
            while (p.NextVisible(true) && !EqualContents(p, end)) count++;
            return count;
        }

        public SerializedProperty GetEndProperty() => GetEndProperty(false);

        public SerializedProperty GetEndProperty(bool includeInvisible)
        {
            var end = Copy();
            if (includeInvisible) end.Next(false);
            else end.NextVisible(false);
            return end;
        }

        public IEnumerator GetEnumerator()
        {
            if (N.Kind == SerializedNodeKind.Array)
            {
                for (int i = 0; i < arraySize; i++)
                    yield return GetArrayElementAtIndex(i);
            }
            else
            {
                var end = GetEndProperty();
                while (NextVisible(true) && !EqualContents(this, end))
                    yield return this;
            }
        }

        public SerializedProperty FindPropertyRelative(string relativePropertyPath)
        {
            var node = SerializedTree.Find(N, relativePropertyPath);
            return node == null || node == N ? null : new SerializedProperty(m_SerializedObject, node);
        }

        /// <summary>Whether both are at the same property of the same SerializedObject.</summary>
        public static bool EqualContents(SerializedProperty x, SerializedProperty y)
        {
            if (x is null || y is null) return false;
            if (x.m_SerializedObject != y.m_SerializedObject) return false;
            if (x.m_UnboundAtEnd || y.m_UnboundAtEnd) return x.m_UnboundAtEnd && y.m_UnboundAtEnd;
            return x.m_UnboundPath == y.m_UnboundPath;
        }

        /// <summary>Whether both properties hold the same data (type and values).</summary>
        public static bool DataEquals(SerializedProperty x, SerializedProperty y)
        {
            if (x is null || y is null) return false;
            return SerializedTree.DataEquals(x.N, y.N);
        }

        // ------------------------------------------------------------ arrays

        public bool isArray => N.Kind == SerializedNodeKind.Array || N.PropertyType == SerializedPropertyType.String
            || (N.TypeTree && N.Kind == SerializedNodeKind.ArrayContainer);

        public bool isFixedBuffer
        {
            get
            {
                Unbound.Runtime.Stubs.Ignored("SerializedProperty.isFixedBuffer", "is not honoured yet: Unbound serializes no fixed buffers");
                return false;
            }
        }

        public int fixedBufferSize
        {
            get
            {
                Unbound.Runtime.Stubs.Ignored("SerializedProperty.fixedBufferSize", "is not honoured yet: Unbound serializes no fixed buffers");
                return 0;
            }
        }

        /// <summary>The smallest size of the array across the targets.</summary>
        public int minArraySize
        {
            get
            {
                var n = N;
                if (n.Kind != SerializedNodeKind.Array) return 0;
                int min = n.ArrayCount;
                if (m_SerializedObject.isEditingMultipleObjects)
                    foreach (var root in m_SerializedObject.OtherRoots())
                    {
                        var other = root == null ? null : SerializedTree.Find(root, n.Path);
                        if (other != null && other.Kind == SerializedNodeKind.Array) min = Math.Min(min, other.ArrayCount);
                    }
                return min;
            }
        }

        public string arrayElementType
        {
            get
            {
                var n = N;
                // A type tree's array: its element's type name.
                if (n.Kind == SerializedNodeKind.Array && n.TypeTree && n.Schema?.Child("Array")?.Child("data") is TypeTreeNode element)
                    return element.TypeName;
                if (n.Kind == SerializedNodeKind.Array)
                    return n.ElementShape == Shape.ManagedReference
                        ? SerializedTree.TypeNameOf(n.ElementType, Shape.ManagedReference)
                        : n.ElementShape == Shape.Composite ? n.ElementType.Name : SerializedTree.TypeNameOf(n.ElementType, n.ElementShape);
                if (n.PropertyType == SerializedPropertyType.String) return "char";
                return "";
            }
        }

        public int arraySize
        {
            get
            {
                var n = N;
                if (n.Kind == SerializedNodeKind.Array) return m_SerializedObject.isEditingMultipleObjects ? minArraySize : n.ArrayCount;
                if (n.TypeTree && n.Kind == SerializedNodeKind.ArrayContainer) return n.Children.Count - 1;
                if (n.PropertyType == SerializedPropertyType.String && n.Kind == SerializedNodeKind.Leaf) return ((string)n.Value).Length;
                return 0;
            }
            set
            {
                var n = N;
                if (n.Kind != SerializedNodeKind.Array)
                {
                    Debug.LogError("Property is not an array.");
                    return;
                }
                var otherRoots = m_SerializedObject.isEditingMultipleObjects ? m_SerializedObject.OtherRoots() : null;
                if (SerializedTree.Resize(n, value, otherRoots)) Changed(structural: true);
            }
        }

        SerializedNode ArrayNode()
        {
            var n = N;
            if (n.Kind == SerializedNodeKind.Array) return n;
            Debug.LogError($"Property '{n.Path}' is not an array.");
            return null;
        }

        /// <summary>Unbound serializes no fixed buffers (C# `fixed` arrays), so there are no elements.</summary>
        public SerializedProperty GetFixedBufferElementAtIndex(int index)
        {
            Unbound.Runtime.Stubs.Ignored("SerializedProperty.GetFixedBufferElementAtIndex", "is not honoured yet: Unbound serializes no fixed buffers");
            return null;
        }

        public SerializedProperty GetArrayElementAtIndex(int index)
        {
            var n = N;
            if (n.Kind != SerializedNodeKind.Array || index < 0 || index >= n.ArrayCount) return null;
            return new SerializedProperty(m_SerializedObject, n.Element(index));
        }

        public void InsertArrayElementAtIndex(int index)
        {
            var array = ArrayNode();
            if (array != null && SerializedTree.InsertElement(array, index)) Changed(structural: true);
        }

        public void DeleteArrayElementAtIndex(int index)
        {
            var array = ArrayNode();
            if (array != null && SerializedTree.DeleteElement(array, index)) Changed(structural: true);
        }

        public bool MoveArrayElement(int srcIndex, int dstIndex)
        {
            var array = ArrayNode();
            if (array == null || !SerializedTree.MoveElement(array, srcIndex, dstIndex)) return false;
            Changed(structural: true);
            return true;
        }

        public void ClearArray()
        {
            var array = ArrayNode();
            if (array != null && SerializedTree.Resize(array, 0)) Changed(structural: true);
        }

        /// <summary>Duplicates the array element this property is on.</summary>
        public bool DuplicateCommand()
        {
            var n = N;
            if (!n.IsElement || !SerializedTree.InsertElement(n.Parent.Parent, n.Index)) return false;
            Changed(structural: true);
            return true;
        }

        /// <summary>Deletes the array element this property is on.</summary>
        public bool DeleteCommand()
        {
            var n = N;
            if (!n.IsElement || !SerializedTree.DeleteElement(n.Parent.Parent, n.Index)) return false;
            Changed(structural: true);
            return true;
        }

        void Changed(bool structural)
        {
            m_SerializedObject.MarkModified();
            if (structural) m_SerializedObject.InvalidateNodes();
        }

        // ------------------------------------------------------------ values

        static void Unsupported(string kind) => Debug.LogError($"type is not a supported {kind} value");

        void Write(SerializedNode node, object value)
        {
            bool force = m_SerializedObject.isEditingMultipleObjects && hasMultipleDifferentValues;
            if (SerializedTree.SetValue(node, value, force)) Changed(structural: node.Kind == SerializedNodeKind.ArraySize);
        }

        static bool IsIntegral(SerializedNode n) =>
            n.PropertyType == SerializedPropertyType.Integer || n.PropertyType == SerializedPropertyType.Enum
            || n.PropertyType == SerializedPropertyType.ArraySize || n.PropertyType == SerializedPropertyType.Character
            || n.PropertyType == SerializedPropertyType.LayerMask || n.PropertyType == SerializedPropertyType.RenderingLayerMask;

        static long ToLong(object value)
        {
            switch (value)
            {
                case null: return 0;
                case bool b: return b ? 1 : 0;
                case char c: return c;
                case ulong u: return unchecked((long)u);
                case LayerMask mask: return mask.value;
                case RenderingLayerMask renderingLayers: return renderingLayers.value;
                case Enum e:
                    return Enum.GetUnderlyingType(e.GetType()) == typeof(ulong)
                        ? unchecked((long)Convert.ToUInt64(e, CultureInfo.InvariantCulture))
                        : Convert.ToInt64(e, CultureInfo.InvariantCulture);
                default: return Convert.ToInt64(value, CultureInfo.InvariantCulture);
            }
        }

        static object FromLong(long value, Type type)
        {
            unchecked
            {
                if (type.IsEnum) return Enum.ToObject(type, value);
                if (type == typeof(LayerMask)) return (LayerMask)(int)value;
                if (type == typeof(RenderingLayerMask)) return (RenderingLayerMask)(uint)value;
                switch (Type.GetTypeCode(type))
                {
                    case TypeCode.SByte: return (sbyte)value;
                    case TypeCode.Byte: return (byte)value;
                    case TypeCode.Int16: return (short)value;
                    case TypeCode.UInt16: return (ushort)value;
                    case TypeCode.Int32: return (int)value;
                    case TypeCode.UInt32: return (uint)value;
                    case TypeCode.Int64: return value;
                    case TypeCode.UInt64: return (ulong)value;
                    case TypeCode.Char: return (char)value;
                    case TypeCode.Boolean: return value != 0;
                    case TypeCode.Single: return (float)value;
                    case TypeCode.Double: return (double)value;
                    default: return value;
                }
            }
        }

        public int intValue
        {
            get
            {
                var n = N;
                if (IsIntegral(n)) return unchecked((int)ToLong(SerializedTree.GetValue(n)));
                Unsupported("int");
                return 0;
            }
            set
            {
                var n = N;
                if (IsIntegral(n)) Write(n, FromLong(value, n.ClrType));
                else Unsupported("int");
            }
        }

        public long longValue
        {
            get
            {
                var n = N;
                if (IsIntegral(n)) return ToLong(SerializedTree.GetValue(n));
                Unsupported("long");
                return 0;
            }
            set
            {
                var n = N;
                if (IsIntegral(n)) Write(n, FromLong(value, n.ClrType));
                else Unsupported("long");
            }
        }

        public uint uintValue
        {
            get
            {
                var n = N;
                if (IsIntegral(n)) return unchecked((uint)ToLong(SerializedTree.GetValue(n)));
                Unsupported("uint");
                return 0;
            }
            set
            {
                var n = N;
                if (IsIntegral(n)) Write(n, FromLong(value, n.ClrType));
                else Unsupported("uint");
            }
        }

        public ulong ulongValue
        {
            get
            {
                var n = N;
                if (IsIntegral(n))
                {
                    var v = SerializedTree.GetValue(n);
                    return v is ulong u ? u : unchecked((ulong)ToLong(v));
                }
                Unsupported("ulong");
                return 0;
            }
            set
            {
                var n = N;
                if (IsIntegral(n)) Write(n, n.ClrType == typeof(ulong) ? value : FromLong(unchecked((long)value), n.ClrType));
                else Unsupported("ulong");
            }
        }

        public bool boolValue
        {
            get
            {
                var n = N;
                if (n.PropertyType == SerializedPropertyType.Boolean) return (bool)SerializedTree.GetValue(n);
                Unsupported("bool");
                return false;
            }
            set
            {
                var n = N;
                if (n.PropertyType == SerializedPropertyType.Boolean) Write(n, value);
                else Unsupported("bool");
            }
        }

        public float floatValue
        {
            get
            {
                var n = N;
                if (n.PropertyType == SerializedPropertyType.Float) return Convert.ToSingle(SerializedTree.GetValue(n), CultureInfo.InvariantCulture);
                Unsupported("float");
                return 0f;
            }
            set
            {
                var n = N;
                if (n.PropertyType == SerializedPropertyType.Float) Write(n, n.ClrType == typeof(double) ? (object)(double)value : value);
                else Unsupported("float");
            }
        }

        public double doubleValue
        {
            get
            {
                var n = N;
                if (n.PropertyType == SerializedPropertyType.Float) return Convert.ToDouble(SerializedTree.GetValue(n), CultureInfo.InvariantCulture);
                Unsupported("double");
                return 0d;
            }
            set
            {
                var n = N;
                if (n.PropertyType == SerializedPropertyType.Float) Write(n, n.ClrType == typeof(float) ? (object)(float)value : value);
                else Unsupported("double");
            }
        }

        public string stringValue
        {
            get
            {
                var n = N;
                if (n.PropertyType == SerializedPropertyType.String) return (string)SerializedTree.GetValue(n) ?? "";
                if (n.PropertyType == SerializedPropertyType.Character) return ((char)SerializedTree.GetValue(n)).ToString();
                Unsupported("string");
                return null;
            }
            set
            {
                var n = N;
                if (n.PropertyType == SerializedPropertyType.String) Write(n, value ?? "");
                else if (n.PropertyType == SerializedPropertyType.Character) Write(n, string.IsNullOrEmpty(value) ? '\0' : value[0]);
                else Unsupported("string");
            }
        }

        T GetTyped<T>(SerializedPropertyType expected, string kind)
        {
            var n = N;
            if (n.PropertyType == expected) return (T)SerializedTree.GetValue(n);
            Unsupported(kind);
            return default;
        }

        void SetTyped<T>(SerializedPropertyType expected, string kind, T value)
        {
            var n = N;
            if (n.PropertyType == expected) Write(n, value);
            else Unsupported(kind);
        }

        public Color colorValue
        {
            get
            {
                var n = N;
                if (n.PropertyType == SerializedPropertyType.Color)
                {
                    var v = SerializedTree.GetValue(n);
                    return v is Color32 c32 ? (Color)c32 : (Color)v;
                }
                Unsupported("color");
                return default;
            }
            set
            {
                var n = N;
                if (n.PropertyType == SerializedPropertyType.Color) Write(n, n.ClrType == typeof(Color32) ? (object)(Color32)value : value);
                else Unsupported("color");
            }
        }

        public Vector2 vector2Value
        {
            get => GetTyped<Vector2>(SerializedPropertyType.Vector2, "vector2");
            set => SetTyped(SerializedPropertyType.Vector2, "vector2", value);
        }

        public Vector3 vector3Value
        {
            get => GetTyped<Vector3>(SerializedPropertyType.Vector3, "vector3");
            set => SetTyped(SerializedPropertyType.Vector3, "vector3", value);
        }

        public Vector4 vector4Value
        {
            get => GetTyped<Vector4>(SerializedPropertyType.Vector4, "vector4");
            set => SetTyped(SerializedPropertyType.Vector4, "vector4", value);
        }

        public Vector2Int vector2IntValue
        {
            get => GetTyped<Vector2Int>(SerializedPropertyType.Vector2Int, "vector2int");
            set => SetTyped(SerializedPropertyType.Vector2Int, "vector2int", value);
        }

        public Vector3Int vector3IntValue
        {
            get => GetTyped<Vector3Int>(SerializedPropertyType.Vector3Int, "vector3int");
            set => SetTyped(SerializedPropertyType.Vector3Int, "vector3int", value);
        }

        public Quaternion quaternionValue
        {
            get => GetTyped<Quaternion>(SerializedPropertyType.Quaternion, "quaternion");
            set => SetTyped(SerializedPropertyType.Quaternion, "quaternion", value);
        }

        public Rect rectValue
        {
            get => GetTyped<Rect>(SerializedPropertyType.Rect, "rect");
            set => SetTyped(SerializedPropertyType.Rect, "rect", value);
        }

        public RectInt rectIntValue
        {
            get => GetTyped<RectInt>(SerializedPropertyType.RectInt, "rectint");
            set => SetTyped(SerializedPropertyType.RectInt, "rectint", value);
        }

        public Bounds boundsValue
        {
            get => GetTyped<Bounds>(SerializedPropertyType.Bounds, "bounds");
            set => SetTyped(SerializedPropertyType.Bounds, "bounds", value);
        }

        public BoundsInt boundsIntValue
        {
            get => GetTyped<BoundsInt>(SerializedPropertyType.BoundsInt, "boundsint");
            set => SetTyped(SerializedPropertyType.BoundsInt, "boundsint", value);
        }

        public Hash128 hash128Value
        {
            get => GetTyped<Hash128>(SerializedPropertyType.Hash128, "hash128");
            set => SetTyped(SerializedPropertyType.Hash128, "hash128", value);
        }

        public AnimationCurve animationCurveValue
        {
            get => (AnimationCurve)SerializedTree.CopyLeaf(GetTyped<AnimationCurve>(SerializedPropertyType.AnimationCurve, "animation curve"), Shape.AnimationCurve);
            set => SetTyped(SerializedPropertyType.AnimationCurve, "animation curve", value ?? new AnimationCurve());
        }

        internal bool ValueEquals(AnimationCurve curve)
        {
            var current = GetTyped<AnimationCurve>(SerializedPropertyType.AnimationCurve, "animation curve");
            return SerializedTree.ValueEquals(current, curve);
        }

        public Gradient gradientValue
        {
            get => (Gradient)SerializedTree.CopyLeaf(GetTyped<Gradient>(SerializedPropertyType.Gradient, "gradient"), Shape.Gradient);
            set => SetTyped(SerializedPropertyType.Gradient, "gradient", value ?? new Gradient());
        }

        internal bool ValueEquals(Gradient gradient)
        {
            var current = GetTyped<Gradient>(SerializedPropertyType.Gradient, "gradient");
            return SerializedTree.ValueEquals(current, gradient);
        }

        public Object objectReferenceValue
        {
            get
            {
                var n = N;
                // A destroyed object or placeholder reads as null via objectReferenceValue while
                // keeping its non-zero instance ID in objectReferenceInstanceIDValue, matching the
                // Unity 6000.0.71f1 Editor (tests/projects/objectbinding).
                if (n.PropertyType == SerializedPropertyType.ObjectReference)
                    return SerializedTree.GetValue(n) is Object value && value != null ? value : null;
                Unsupported("pptr");
                return null;
            }
            set
            {
                var n = N;
                if (n.PropertyType != SerializedPropertyType.ObjectReference)
                {
                    Unsupported("pptr");
                    return;
                }
                // Normally a reference of an incompatible type is not assigned; in unsafeMode
                // (used by ObjectField binding) an incompatible value clears the reference to null,
                // matching the Unity 6000.0.71f1 Editor (tests/projects/objectbinding).
                if (!(value is null) && !n.ClrType.IsInstanceOfType(value))
                {
                    if (!unsafeMode) return;
                    value = null;
                }
                Write(n, value);
            }
        }

        public int objectReferenceInstanceIDValue
        {
            get
            {
                var n = N;
                if (n.PropertyType != SerializedPropertyType.ObjectReference)
                {
                    Unsupported("pptr");
                    return 0;
                }
                return SerializedTree.GetValue(n) is Object raw && !(raw.m_InstanceID == 0 && raw.m_Destroyed)
                    ? raw.GetInstanceID()
                    : 0;
            }
            set => objectReferenceValue = value == 0 ? null : ObjectRegistry.FindById(value);
        }

        internal string objectReferenceStringValue
        {
            get
            {
                var n = N;
                if (n.PropertyType != SerializedPropertyType.ObjectReference) return "";
                return EditorGUIUtility.ObjectContent(objectReferenceValue, ObjectReferenceFieldType, objectReferenceInstanceIDValue, showNullIcon: false).text;
            }
        }

        static readonly Dictionary<string, Type> s_NativePPtrTypeCache = new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase);

        internal Type ObjectReferenceFieldType
        {
            get
            {
                var n = N;
                if (n.PropertyType != SerializedPropertyType.ObjectReference) return null;
                if (n.ClrType != null && n.ClrType != typeof(Object) && typeof(Object).IsAssignableFrom(n.ClrType))
                    return n.ClrType;
                if (n.TypeName != null && n.TypeName.StartsWith("PPtr<", StringComparison.Ordinal) && n.TypeName.EndsWith(">", StringComparison.Ordinal))
                {
                    int start = 5;
                    if (start < n.TypeName.Length && n.TypeName[start] == '$') start++;
                    string rawName = n.TypeName.Substring(start, n.TypeName.Length - 1 - start);
                    if (string.Equals(rawName, "Object", StringComparison.Ordinal))
                        return typeof(Object);
                    if (s_NativePPtrTypeCache.TryGetValue(rawName, out var cached))
                        return cached;
                    Type matched = null;
                    foreach (var candidate in TypeCache.GetTypesDerivedFrom<Object>())
                    {
                        if (!candidate.Name.Equals(rawName, StringComparison.OrdinalIgnoreCase)
                            || typeof(MonoBehaviour).IsAssignableFrom(candidate)
                            || typeof(ScriptableObject).IsAssignableFrom(candidate))
                            continue;
                        matched = candidate;
                        break;
                    }
                    s_NativePPtrTypeCache[rawName] = matched;
                    return matched;
                }
                return n.ClrType != null && typeof(Object).IsAssignableFrom(n.ClrType) ? n.ClrType : typeof(Object);
            }
        }

        // ------------------------------------------------------------ enums

        internal bool unsafeMode { get; set; }

        internal Type ManagedEnumType
        {
            get
            {
                var n = N;
                return n.PropertyType == SerializedPropertyType.Enum && n.ClrType != null && n.ClrType.IsEnum
                    ? n.ClrType
                    : null;
            }
        }

        static FieldInfo[] EnumFieldsInSerializedOrder(Type type)
        {
            var fields = type.GetFields(BindingFlags.Public | BindingFlags.Static);
            if (fields.Length > 1)
            {
                Array.Sort(fields, (a, b) =>
                {
                    int va = unchecked((int)ToLong(a.GetValue(null)));
                    int vb = unchecked((int)ToLong(b.GetValue(null)));
                    int cmp = va.CompareTo(vb);
                    return cmp != 0 ? cmp : string.CompareOrdinal(a.Name, b.Name);
                });
            }
            return fields;
        }

        static string[] EnumNames(Type type)
        {
            var fields = EnumFieldsInSerializedOrder(type);
            var names = new string[fields.Length];
            for (int i = 0; i < fields.Length; i++) names[i] = fields[i].Name;
            return names;
        }

        static long[] EnumValues(Type type)
        {
            var fields = EnumFieldsInSerializedOrder(type);
            var values = new long[fields.Length];
            for (int i = 0; i < fields.Length; i++) values[i] = ToLong(fields[i].GetValue(null));
            return values;
        }

        public int enumValueIndex
        {
            get
            {
                var n = N;
                if (n.PropertyType != SerializedPropertyType.Enum)
                {
                    Unsupported("enum");
                    return 0;
                }
                long current = ToLong(SerializedTree.GetValue(n));
                var values = EnumValues(n.ClrType);
                return Array.IndexOf(values, current);
            }
            set
            {
                var n = N;
                if (n.PropertyType != SerializedPropertyType.Enum)
                {
                    Unsupported("enum");
                    return;
                }
                var values = EnumValues(n.ClrType);
                if (value < 0 || value >= values.Length) return;
                Write(n, FromLong(values[value], n.ClrType));
            }
        }

        public int enumValueFlag
        {
            get
            {
                var n = N;
                if (n.PropertyType == SerializedPropertyType.Enum) return unchecked((int)ToLong(SerializedTree.GetValue(n)));
                Unsupported("enum");
                return 0;
            }
            set
            {
                var n = N;
                if (n.PropertyType == SerializedPropertyType.Enum) Write(n, FromLong(value, n.ClrType));
                else Unsupported("enum");
            }
        }

        public string[] enumNames
        {
            get
            {
                var n = N;
                return n.PropertyType == SerializedPropertyType.Enum ? EnumNames(n.ClrType) : Array.Empty<string>();
            }
        }

        public string[] enumDisplayNames
        {
            get
            {
                var n = N;
                if (n.PropertyType != SerializedPropertyType.Enum) return Array.Empty<string>();
                var fields = EnumFieldsInSerializedOrder(n.ClrType);
                var names = new string[fields.Length];
                for (int i = 0; i < fields.Length; i++)
                    names[i] = fields[i].GetCustomAttribute<InspectorNameAttribute>(false)?.displayName ?? ObjectNames.NicifyVariableName(fields[i].Name);
                return names;
            }
        }

        internal string[] enumLocalizedDisplayNames => enumDisplayNames;

        // ------------------------------------------------------------ managed references and boxed values

        public object managedReferenceValue
        {
            get
            {
                var n = N;
                if (n.Kind == SerializedNodeKind.ManagedReference) return n.Value;
                Debug.LogError("managedReferenceValue is only available on fields with the [SerializeReference] attribute");
                return null;
            }
            set
            {
                var n = N;
                if (n.Kind != SerializedNodeKind.ManagedReference)
                {
                    Debug.LogError("managedReferenceValue is only available on fields with the [SerializeReference] attribute");
                    return;
                }
                if (value != null && (value is Object || !n.ClrType.IsInstanceOfType(value)))
                {
                    Debug.LogError($"Cannot assign an object of type '{value.GetType()}' to the managed reference field '{n.Path}' of type '{n.ClrType}'.");
                    return;
                }
                if (ReferenceEquals(n.Value, value)) return;
                SerializedTree.Assign(n, value, callbacks: true);
                Changed(structural: true);
            }
        }

        public long managedReferenceId
        {
            get
            {
                var n = N;
                return n.Kind == SerializedNodeKind.ManagedReference ? SerializedTree.ReferenceId(n.Value) : SerializedTree.NullReferenceId;
            }
            set
            {
                if (value == SerializedTree.NullReferenceId)
                {
                    managedReferenceValue = null;
                    return;
                }
                var target = SerializedTree.FindReference(m_SerializedObject.m_UnboundRoot, value);
                if (target == null)
                {
                    Debug.LogError($"No managed reference with id {value} in the serialized object.");
                    return;
                }
                managedReferenceValue = target;
            }
        }

        public string managedReferenceFullTypename
        {
            get
            {
                var n = N;
                return n.Kind == SerializedNodeKind.ManagedReference ? SerializedTree.ManagedTypeName(n.Value?.GetType()) : "";
            }
        }

        public string managedReferenceFieldTypename
        {
            get
            {
                var n = N;
                return n.Kind == SerializedNodeKind.ManagedReference ? SerializedTree.ManagedTypeName(n.ClrType) : "";
            }
        }

        internal object structValue
        {
            get
            {
                var n = N;
                if (n.Kind == SerializedNodeKind.Array || n.Kind == SerializedNodeKind.ArrayContainer)
                    throw new InvalidOperationException($"'{n.Path}' is an array so it cannot be read with boxedValue.");
                if (n.Kind != SerializedNodeKind.Composite)
                    throw new InvalidOperationException($"structValue is not supported on property '{n.Path}' of type '{n.TypeName}'.");
                return SerializedTree.Build(n, null, primary: false);
            }
            set
            {
                var n = N;
                if (n.Kind == SerializedNodeKind.Array || n.Kind == SerializedNodeKind.ArrayContainer)
                    throw new InvalidOperationException($"'{n.Path}' is an array so it cannot be set with boxedValue.");
                if (n.Kind != SerializedNodeKind.Composite)
                    throw new InvalidOperationException($"structValue is not supported on property '{n.Path}' of type '{n.TypeName}'.");
                if (value == null || !n.ClrType.IsInstanceOfType(value))
                    throw new InvalidOperationException($"The input to boxedValue has type '{value?.GetType().FullName?.Replace("+", "/")}', which does not match the expected type '{n.ClrType.FullName?.Replace("+", "/")}'.");
                SerializedTree.Assign(n, value, callbacks: false);
                Changed(structural: true);
            }
        }

        /// <summary>The property's value as an object: primitives, structs, references and copies of
        /// serializable classes and collections.</summary>
        public object boxedValue
        {
            get
            {
                var n = N;
                switch (n.Kind)
                {
                    case SerializedNodeKind.Root:
                    case SerializedNodeKind.ArrayContainer:
                        throw new InvalidOperationException($"boxedValue is not supported on property '{n.Path}' of type '{n.TypeName}'.");
                    case SerializedNodeKind.ManagedReference:
                        return n.Value;
                    case SerializedNodeKind.Composite:
                    case SerializedNodeKind.Array:
                        return SerializedTree.Build(n, null, primary: false);
                    default:
                        return SerializedTree.CopyLeaf(SerializedTree.GetValue(n), n.Shape);
                }
            }
            set
            {
                var n = N;
                switch (n.Kind)
                {
                    case SerializedNodeKind.ManagedReference:
                        managedReferenceValue = value;
                        return;
                    case SerializedNodeKind.Composite:
                    case SerializedNodeKind.Array:
                        if (value == null || !n.ClrType.IsInstanceOfType(value))
                            throw new ArgumentException($"Cannot assign a value of type '{value?.GetType()}' to property '{n.Path}' of type '{n.ClrType}'.");
                        SerializedTree.Assign(n, value, callbacks: false);
                        Changed(structural: true);
                        return;
                    case SerializedNodeKind.Leaf:
                    case SerializedNodeKind.Component:
                    case SerializedNodeKind.ArraySize:
                        Write(n, ConvertBoxed(value, n));
                        return;
                    default:
                        throw new InvalidOperationException($"boxedValue is not supported on property '{n.Path}' of type '{n.TypeName}'.");
                }
            }
        }

        static object ConvertBoxed(object value, SerializedNode n)
        {
            var type = n.ClrType;
            if (value == null)
            {
                if (type.IsValueType) throw new ArgumentException($"Cannot assign null to property '{n.Path}' of type '{type}'.");
                return n.Shape == Shape.String ? "" : null;
            }
            if (type.IsInstanceOfType(value)) return value;
            if (type == typeof(float) || type == typeof(double)) return Convert.ChangeType(value, type, CultureInfo.InvariantCulture);
            if (type.IsEnum || type.IsPrimitive) return FromLong(ToLong(value), type);
            if (value is Color c && type == typeof(Color32)) return (Color32)c;
            throw new ArgumentException($"Cannot assign a value of type '{value.GetType()}' to property '{n.Path}' of type '{type}'.");
        }
    }
}
