using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Unbound.Runtime
{
    /// <summary>
    /// The prefab instances of the loaded scenes and their overrides, as the Editor keeps them
    /// (Unity 6000.0.71f1, tests/projects/scenesave prefab-api, prefab-writes): an instance's
    /// object overrides a property when its PrefabInstance stores a modification of it, or when its
    /// value differs from its source's (an engine component's change is an override at once, a
    /// script's once PrefabUtility.RecordPrefabInstancePropertyModifications or a SerializedObject
    /// recorded it). A component of its source an instance's GameObject lacks is removed, one it has
    /// without a source added. When its prefab changes, an instance takes the prefab's values
    /// except its overrides, and the prefab's new and removed objects (<see cref="Merge"/>).
    /// </summary>
    internal static class PrefabInstances
    {
        /// <summary>An instance's objects linked to it: each GameObject, then its components, then
        /// its children (the objects added to it are not linked and not listed).</summary>
        internal static List<Object> ObjectsOf(PrefabInstanceData data) => ObjectsOf(data, RootOf(data));

        /// <summary>The objects of an instance whose root is known (one inside a prefab asset).</summary>
        internal static List<Object> ObjectsOf(PrefabInstanceData data, GameObject root)
        {
            var list = new List<Object>();
            if (root != null) Collect(root.transform, data, list);
            return list;
        }

        static void Collect(Transform t, PrefabInstanceData data, List<Object> into)
        {
            var go = t.m_GameObject;
            if (Of(go) != data) return;
            into.Add(go);
            foreach (var c in go.m_Components)
                if (c != null && !c.m_Destroyed && Of(c) == data) into.Add(c);
            foreach (var child in t.m_Children) Collect(child, data, into);
        }

        static PrefabInstanceData Of(Object o) => PrefabLinks.Of(o)?.Instance;

        /// <summary>The topmost GameObject of the instance in the loaded scenes (null: none loaded).</summary>
        internal static GameObject RootOf(PrefabInstanceData data)
        {
            foreach (var scene in SceneRegistry.Loaded)
            {
                if (!scene.IsLoaded) continue;
                foreach (var root in scene.Roots)
                    if (root != null && Find(root.transform, data) is GameObject found) return found;
            }
            return null;
        }

        static GameObject Find(Transform t, PrefabInstanceData data)
        {
            if (Of(t.m_GameObject) == data) return t.m_GameObject;
            foreach (var child in t.m_Children)
                if (Find(child, data) is GameObject found) return found;
            return null;
        }

        /// <summary>The instances of a prefab in the loaded scenes (their roots, in hierarchy order).</summary>
        internal static List<(PrefabInstanceData Data, GameObject Root)> InstancesOf(string guid)
        {
            var found = new List<(PrefabInstanceData, GameObject)>();
            var seen = new HashSet<PrefabInstanceData>();
            void Walk(Transform t)
            {
                var data = Of(t.m_GameObject);
                if (data != null && string.Equals(data.SourceGuid, guid, StringComparison.OrdinalIgnoreCase) && seen.Add(data))
                    found.Add((data, t.m_GameObject));
                foreach (var child in t.m_Children) Walk(child);
            }
            foreach (var scene in SceneRegistry.Loaded)
            {
                if (!scene.IsLoaded) continue;
                foreach (var root in scene.Roots) if (root != null) Walk(root.transform);
            }
            return found;
        }

        /// <summary>Whether an object's values count as its instance's overrides: an engine
        /// component's always, a script's once recorded or, in Edit mode for an instance
        /// InstantiatePrefab made in the session, once an Editor update has passed since (matches the
        /// Unity 6000.0.71f1 Editor, tests/projects/scenesave modification-timing script.direct,
        /// script.frame; edit save.base: a script change in the same update is not saved). For an
        /// instance read from a file the Scripting API says unrecorded changes are lost: its scripts
        /// count once recorded only (an update's taking of them is unmeasured there).</summary>
        internal static bool Considered(Object o) =>
            o is not MonoBehaviour || PrefabRecords.IsRecorded(o)
            || !EditorApplication.isPlaying && PrefabLinks.Of(o)?.Instance is PrefabInstanceData data
               && data.InstantiatedStamp >= 0 && data.InstantiatedStamp < PrefabRecordTiming.Boundary;

        /// <summary>Whether an instance's modifications have taken a Transform's last change: in
        /// Edit mode only at the next Editor update or a record point (modification-timing
        /// transform.direct, transform.frame, save.*); until then they hold its pose at the last
        /// update (or, changed for the first time, the value recorded before). Other engine
        /// components' changes, a RectTransform's included, are taken at once (their timing is
        /// unmeasured).</summary>
        static bool Taken(Object o, PrefabInstanceData data) =>
            o is not Transform t || t is RectTransform || EditorApplication.isPlaying
            || t.m_ChangeStamp < Math.Max(PrefabRecordTiming.TransformsTaken, data.RecordStamp);

        /// <summary>How many times a record walked an instance's objects (a test bounds it: one walk
        /// per distinct instance of a flush or an apply).</summary>
        internal static int Walks;

        /// <summary>Instances queued to be recorded once each (an Undo flush's or a multi-target
        /// apply's objects).</summary>
        static readonly HashSet<PrefabInstanceData> s_Pending = new HashSet<PrefabInstanceData>(ReferenceEqualityComparer.Instance);

        /// <summary>Queues the instance of an object for <see cref="RecordPending"/>.</summary>
        internal static void QueueRecord(Object o)
        {
            if (PrefabLinks.Of(o)?.Instance is PrefabInstanceData data) s_Pending.Add(data);
        }

        /// <summary>Records each queued instance once.</summary>
        internal static void RecordPending()
        {
            if (s_Pending.Count == 0) return;
            foreach (var data in s_Pending) Record(data);
            s_Pending.Clear();
        }

        /// <summary>A record point: the instance takes its changes now, each of its Transforms'
        /// pose too (a later change in the same update answers the recorded pose).</summary>
        internal static void Record(PrefabInstanceData data)
        {
            if (data == null) return;
            Walks++;
            PrefabRecordTiming.Recorded(data);
            foreach (var o in ObjectsOf(data))
                if (o is Transform t)
                {
                    t.m_TakenPose = t.CurrentPose();
                    t.m_HasTakenPose = true;
                }
        }

        /// <summary>A Transform's pose at the last update put in place for a moment (without a change
        /// of its own), and its live pose put back after.</summary>
        readonly struct TakenPose : IDisposable
        {
            readonly Transform m_Transform;
            readonly Transform.LocalPose m_Live;
            internal bool Applied => m_Transform != null;

            TakenPose(Transform t)
            {
                // A transform job finishing during the swap would write under the taken pose.
                Transform.SyncJobs();
                m_Transform = t;
                m_Live = t.CurrentPose();
                t.SetPoseQuietly(t.m_TakenPose);
            }

            internal static TakenPose Of(Transform t) => t.m_HasTakenPose ? new TakenPose(t) : default;

            public void Dispose()
            {
                if (m_Transform != null) m_Transform.SetPoseQuietly(m_Live);
            }
        }

        /// <summary>References to the instance's objects written as the source objects they stand for
        /// (so an instance and its source compare alike).</summary>
        internal static Func<Object, Object> ToSource(PrefabInstanceData data) =>
            x => PrefabLinks.Of(x) is PrefabLink link && link.Instance == data ? PrefabLinks.Source(x) ?? x : x;

        static bool Targets((ObjectRef Target, string Path, string Value, ObjectRef Reference) stored, PrefabInstanceData data, PrefabLink link) =>
            !stored.Target.IsNull && stored.Target.FileId == link.SourceFileId
            && string.Equals(stored.Target.Guid ?? data.SourceGuid, link.SourceGuid, StringComparison.OrdinalIgnoreCase);

        /// <summary>The overrides of an instance at one moment: each object's overridden values (as
        /// it has them), the stored modifications Unbound has no path of (set through a
        /// SerializedObject), and the source components its GameObjects lack.</summary>
        internal sealed class Snapshot
        {
            /// <summary>The names of the objects its values reference.</summary>
            internal ValueReferences Refs;
            internal readonly Dictionary<Object, List<(string Path, YText Value)>> Values = new Dictionary<Object, List<(string, YText)>>(ReferenceEqualityComparer.Instance);
            internal readonly Dictionary<Object, List<(string Path, string Value, Object Reference)>> Unmatched = new Dictionary<Object, List<(string, string, Object)>>(ReferenceEqualityComparer.Instance);
            internal readonly HashSet<Object> Removed = new HashSet<Object>(ReferenceEqualityComparer.Instance);

            /// <summary>Drops the overrides of a property (and its parts) of an object.</summary>
            internal void Forget(Object o, string property)
            {
                if (Values.TryGetValue(o, out var values)) values.RemoveAll(v => PrefabValues.Within(v.Path, property));
                if (Unmatched.TryGetValue(o, out var unmatched)) unmatched.RemoveAll(v => PrefabValues.Within(v.Path, property));
            }

            /// <summary>Drops the overrides of an object the predicate selects by path.</summary>
            internal void Forget(Object o, Func<string, bool> selected)
            {
                if (Values.TryGetValue(o, out var values)) values.RemoveAll(v => selected(v.Path));
                if (Unmatched.TryGetValue(o, out var unmatched)) unmatched.RemoveAll(v => selected(v.Path));
            }

            /// <summary>A copy (sharing the reference names).</summary>
            internal Snapshot Copy()
            {
                var copy = new Snapshot { Refs = Refs };
                foreach (var pair in Values) copy.Values[pair.Key] = new List<(string, YText)>(pair.Value);
                foreach (var pair in Unmatched) copy.Unmatched[pair.Key] = new List<(string, string, Object)>(pair.Value);
                foreach (var o in Removed) copy.Removed.Add(o);
                return copy;
            }
        }

        /// <summary>The instance's overrides now. <paramref name="baseline"/> gives a source object's
        /// values the instance was last merged from, when the source changed since (else its values now).</summary>
        /// <param name="liveDifferences">Whether values that differ from the source's are overrides
        /// (false: only the stored modifications are, for an instance being made).</param>
        internal static Snapshot Capture(PrefabInstanceData data, ValueReferences refs, Func<Object, Dictionary<string, YText>> baseline = null,
            bool liveDifferences = true)
        {
            var snapshot = new Snapshot { Refs = refs };
            var toSource = ToSource(data);
            foreach (var o in ObjectsOf(data))
            {
                var link = PrefabLinks.Of(o);
                var source = PrefabLinks.Source(o);
                if (source == null) continue;
                var live = PrefabValues.Flatten(PrefabValues.Fields(o, null, refs));
                var livePaths = new HashSet<string>(StringComparer.Ordinal);
                foreach (var (path, _) in live) livePaths.Add(path);
                var paths = new HashSet<string>(StringComparer.Ordinal);
                foreach (var stored in data.Stored)
                {
                    if (!Targets(stored, data, link)) continue;
                    if (livePaths.Contains(stored.Path)) paths.Add(stored.Path);
                    else
                    {
                        if (!snapshot.Unmatched.TryGetValue(o, out var unmatched)) snapshot.Unmatched[o] = unmatched = new List<(string, string, Object)>();
                        unmatched.Add((stored.Path, stored.Value, StoredReference(data, stored, o)));
                    }
                }
                if (liveDifferences && Considered(o))
                {
                    var before = baseline?.Invoke(source) ?? PrefabValues.ByPath(PrefabValues.Flatten(PrefabValues.Fields(source, null, refs)));
                    foreach (var (path, value) in PrefabValues.Flatten(PrefabValues.Fields(o, toSource, refs)))
                        if (!before.TryGetValue(path, out var was) || !PrefabValues.Same(was, value)) paths.Add(path);
                }
                var values = new List<(string, YText)>();
                foreach (var (path, value) in live) if (paths.Contains(path)) values.Add((path, value));
                if (values.Count > 0) snapshot.Values[o] = values;
                if (o is GameObject go && source is GameObject sourceGo)
                    foreach (var sc in sourceGo.m_Components)
                        if (sc != null && !sc.m_Destroyed && Counterpart(go, sc, data) == null) snapshot.Removed.Add(sc);
            }
            return snapshot;
        }

        /// <summary>A stored modification's object reference: the object it was set to, else the
        /// object of the instance's scene or the asset it names.</summary>
        static Object StoredReference(PrefabInstanceData data, (ObjectRef Target, string Path, string Value, ObjectRef Reference) stored, Object o)
        {
            if (data.ReferenceObjects.TryGetValue((stored.Target.FileId, stored.Path), out var set)) return set;
            var reference = stored.Reference;
            if (reference.IsNull) return null;
            if (reference.IsLocal)
            {
                var scene = (o as GameObject ?? (o as Component)?.m_GameObject)?.scene.Data;
                return scene == null ? null : SceneRegistry.FindByFileId(scene.Path, reference.FileId);
            }
            try { return AssetDatabaseRuntime.Load(reference.Guid, reference.FileId, typeof(Object)); }
            catch (Exception) { return null; }
        }

        /// <summary>The instance's component that stands for a source component.</summary>
        internal static Component Counterpart(GameObject go, Component source, PrefabInstanceData data)
        {
            foreach (var c in go.m_Components)
                if (c != null && !c.m_Destroyed && PrefabLinks.Of(c) is PrefabLink link && link.Instance == data && ReferenceEquals(PrefabLinks.Source(c), source))
                    return c;
            return null;
        }

        /// <summary>
        /// The instance takes its prefab's values and objects again, keeping its overrides
        /// (<paramref name="snapshot"/>, taken before the prefab changed): a GameObject or component
        /// the prefab has and the instance lacks is made (unless it is a removed component), one whose
        /// source is gone is destroyed; then each object takes its source's values (references to
        /// the prefab's objects as the instance's), its overrides over them.
        /// </summary>
        internal static void Merge(PrefabInstanceData data, Snapshot snapshot)
        {
            var refs = snapshot.Refs;
            foreach (var o in ObjectsOf(data))
            {
                if (o is not GameObject go || go == null) continue;
                if (PrefabLinks.Source(go) is not GameObject sourceGo)
                {
                    Object.DestroyImmediate(go);
                    continue;
                }
                for (int i = 0; i < sourceGo.m_Components.Count; i++)
                {
                    var sc = sourceGo.m_Components[i];
                    if (sc == null || sc.m_Destroyed || sc is Transform || snapshot.Removed.Contains(sc) || Counterpart(go, sc, data) != null) continue;
                    if (!AssetDatabaseRuntime.TryGetLocation(sc, out var guid, out var fileId)) continue;
                    var made = go.AddComponent(sc.GetType());
                    if (made == null) continue;
                    PrefabLinks.Set(made, new PrefabLink { SourceGuid = guid, SourceFileId = fileId, Instance = data });
                    IdentityLists.Remove(go.m_Components, made);
                    go.m_Components.Insert(Math.Min(i, go.m_Components.Count), made);
                }
                foreach (var c in go.m_Components.ToArray())
                    if (c != null && !c.m_Destroyed && !(c is Transform) && PrefabLinks.Of(c)?.Instance == data && PrefabLinks.Source(c) == null)
                        Object.DestroyImmediate(c);
                var sourceChildren = sourceGo.transform.m_Children;
                for (int i = 0; i < sourceChildren.Count; i++)
                {
                    var sourceChild = sourceChildren[i].m_GameObject;
                    bool present = false;
                    foreach (var child in go.transform.m_Children)
                        if (PrefabLinks.Of(child.m_GameObject)?.Instance == data && ReferenceEquals(PrefabLinks.Source(child.m_GameObject), sourceChild)) present = true;
                    if (present) continue;
                    var clone = Instantiation.CloneLinked(sourceChild, go.transform, null, map => LinkClones(map, data));
                    clone.transform.SetSiblingIndex(i);
                }
            }

            var locations = new AssetDatabaseRuntime.Locations();
            var counterparts = new Dictionary<(string, long), Object>();
            var objects = ObjectsOf(data);
            foreach (var o in objects)
                if (PrefabLinks.Of(o) is PrefabLink link) counterparts[(link.SourceGuid.ToLowerInvariant(), link.SourceFileId)] = o;
            Object ToInstance(Object x)
            {
                if (x is null || !locations.TryGet(x, out var guid, out var fileId, out _)) return x;
                return guid != null && counterparts.TryGetValue((guid.ToLowerInvariant(), fileId), out var instance) ? instance : x;
            }
            foreach (var o in objects)
            {
                var source = PrefabLinks.Source(o);
                if (source == null || o == null) continue;
                var values = PrefabValues.Flatten(PrefabValues.Fields(source, ToInstance, refs));
                if (snapshot.Values.TryGetValue(o, out var overrides))
                {
                    var at = new Dictionary<string, int>(StringComparer.Ordinal);
                    for (int i = 0; i < values.Count; i++) at[values[i].Path] = i;
                    foreach (var (path, value) in overrides)
                    {
                        if (at.TryGetValue(path, out var index)) values[index] = (path, value);
                        else values.Add((path, value));
                    }
                }
                PrefabValues.Write(o, values, refs);
                if (snapshot.Unmatched.TryGetValue(o, out var unmatched))
                    foreach (var (path, value, reference) in unmatched) SerializedValues.TrySet(o, path, value, reference);
            }
        }

        /// <summary>Links the copies of a prefab's objects (made for an instance) to their sources.</summary>
        internal static void LinkClones(Dictionary<Object, Object> clones, PrefabInstanceData data)
        {
            foreach (var pair in clones)
                if (AssetDatabaseRuntime.TryGetLocation(pair.Key, out var guid, out var fileId))
                    PrefabLinks.Set(pair.Value, new PrefabLink { SourceGuid = guid, SourceFileId = fileId, Instance = data });
        }

        /// <summary>The modifications an instance has now (PrefabUtility.GetPropertyModifications):
        /// its stored ones, in their order, with the values its objects have now, and each other
        /// property that differs from its source, after the last one of the same or a smaller target
        /// file id (the Editor keeps them ordered by target).</summary>
        internal static List<(ObjectRef Target, string Path, string Value, Object Reference, bool Resolved)> Modifications(PrefabInstanceData data)
        {
            var list = new List<(ObjectRef Target, string Path, string Value, Object Reference, bool Resolved)>(data.Stored.Count);
            foreach (var m in data.Stored)
            {
                bool set = data.ReferenceObjects.TryGetValue((m.Target.FileId, m.Path), out var reference);
                list.Add((m.Target, m.Path, m.Value, reference, set));
            }
            var refs = new ValueReferences();
            var toSource = ToSource(data);
            foreach (var o in ObjectsOf(data))
            {
                if (!Considered(o)) continue;
                var link = PrefabLinks.Of(o);
                if (PrefabLinks.Source(o) == null) continue;
                // A script whose changes are taken only when recorded answers its values at its last
                // record (matches the Unity 6000.0.71f1 Editor, tests/projects/animatorapi
                // modify.overridden.*: a change since is listed once recorded again).
                var entries = TakesChangesWhenRecorded(o) && PrefabRecords.Values(o) is { } recorded ? Entries(o, recorded) : null;
                if (entries == null)
                {
                    // A Transform changed since its instance last took its changes answers its pose
                    // at the last update, else (changed for the first time) the stored values.
                    using var taken = Taken(o, data) ? default : TakenPose.Of((Transform)o);
                    if (!Taken(o, data) && !taken.Applied) continue;
                    entries = Entries(o, toSource, refs);
                }
                var target = new ObjectRef(link.SourceFileId, link.SourceGuid, 3);
                foreach (var entry in entries)
                {
                    int existing = -1;
                    for (int i = 0; i < list.Count && existing < 0; i++)
                        if (list[i].Path == entry.Path && !list[i].Target.IsNull && list[i].Target.FileId == link.SourceFileId
                            && string.Equals(list[i].Target.Guid ?? data.SourceGuid, link.SourceGuid, StringComparison.OrdinalIgnoreCase))
                            existing = i;
                    if (existing >= 0)
                    {
                        list[existing] = (list[existing].Target, entry.Path, entry.Value, entry.Reference, true);
                        continue;
                    }
                    if (!entry.Differs) continue;
                    int insertAt = list.Count;
                    for (int i = 0; i < list.Count; i++)
                        if (list[i].Target.FileId > link.SourceFileId) { insertAt = i; break; }
                    list.Insert(insertAt, (target, entry.Path, entry.Value, entry.Reference, true));
                }
            }
            return list;
        }

        /// <summary>An object's properties as an instance's modifications take them: each path with
        /// its value text (or object reference), and whether it differs from the source's.</summary>
        internal static List<(string Path, string Value, Object Reference, bool Differs)> Entries(Object o, Func<Object, Object> toSource, ValueReferences refs)
        {
            var list = new List<(string, string, Object, bool)>();
            var source = PrefabLinks.Source(o);
            if (source == null) return list;
            var before = PrefabValues.ByPath(PrefabValues.Flatten(PrefabValues.Fields(source, null, refs)));
            foreach (var (path, value) in PrefabValues.Flatten(PrefabValues.Fields(o, toSource, refs)))
            {
                bool isReference = value.Kind == YKind.Map;
                bool differs = !(before.TryGetValue(path, out var was) && PrefabValues.Same(was, value));
                list.Add((path, isReference ? "" : value.Text, isReference ? refs.ObjectOf(value) : null, differs));
            }
            return list;
        }

        /// <summary>A script's values as a record took them, with the references they name.</summary>
        internal sealed class RecordedValues
        {
            internal ValueReferences Refs;
            internal List<(string Path, YText Value)> Values;
        }

        /// <summary>The overrides a record keeps for a script of an instance (null for other objects):
        /// its values now of the properties that differ from the source's or that the instance
        /// stores, what its modifications take until it is recorded again (a revert or an apply
        /// takes paths away, <see cref="PrefabRecords.Forget"/>).</summary>
        internal static RecordedValues Record(Object o)
        {
            if (o is not MonoBehaviour || PrefabLinks.Of(o)?.Instance is not PrefabInstanceData data || !TakesChangesWhenRecorded(o)) return null;
            var link = PrefabLinks.Of(o);
            var source = PrefabLinks.Source(o);
            if (source == null) return null;
            var refs = new ValueReferences();
            var before = PrefabValues.ByPath(PrefabValues.Flatten(PrefabValues.Fields(source, null, refs)));
            var stored = new HashSet<string>(StringComparer.Ordinal);
            foreach (var m in data.Stored)
                if (Targets(m, data, link)) stored.Add(m.Path);
            var values = PrefabValues.Flatten(PrefabValues.Fields(o, ToSource(data), refs))
                .FindAll(v => stored.Contains(v.Path) || !(before.TryGetValue(v.Path, out var was) && PrefabValues.Same(was, v.Value)));
            return new RecordedValues { Refs = refs, Values = values };
        }

        /// <summary>A script's recorded overrides as entries, each differing when its recorded value
        /// differs from the source's now (an apply since leaves it equal).</summary>
        static List<(string Path, string Value, Object Reference, bool Differs)> Entries(Object o, RecordedValues recorded)
        {
            var list = new List<(string, string, Object, bool)>(recorded.Values.Count);
            var source = PrefabLinks.Source(o);
            if (source == null) return list;
            var before = PrefabValues.ByPath(PrefabValues.Flatten(PrefabValues.Fields(source, null, recorded.Refs)));
            foreach (var (path, value) in recorded.Values)
            {
                bool isReference = value.Kind == YKind.Map;
                bool differs = !(before.TryGetValue(path, out var was) && PrefabValues.Same(was, value));
                list.Add((path, isReference ? "" : value.Text, isReference ? recorded.Refs.ObjectOf(value) : null, differs));
            }
            return list;
        }

        /// <summary>A script whose changes reach its instance's modifications (GetPropertyModifications)
        /// only when recorded, as measured: in Play mode. Edit mode keeps its scripts' live values;
        /// applies, reverts and merges take live values in both (unmeasured for Play mode).</summary>
        static bool TakesChangesWhenRecorded(Object o) =>
            o is MonoBehaviour && EditorApplication.isPlaying;
    }

    /// <summary>A property set by its path through a SerializedObject, from a modification's text
    /// (PropertyModification: a value as text, or an object reference).</summary>
    internal static class SerializedValues
    {
        internal static bool TrySet(Object target, string path, string value, Object reference)
        {
            using var quiet = PrefabRecordTiming.QuietScope.Begin();
            if (target == null) return false;
            var so = new SerializedObject(target);
            var property = so.FindProperty(path);
            if (property == null) return false;
            var invariant = System.Globalization.CultureInfo.InvariantCulture;
            switch (property.propertyType)
            {
                case SerializedPropertyType.ObjectReference:
                    property.objectReferenceValue = reference;
                    break;
                case SerializedPropertyType.Integer:
                case SerializedPropertyType.LayerMask:
                case SerializedPropertyType.Character:
                case SerializedPropertyType.ArraySize:
                    if (!long.TryParse(value, System.Globalization.NumberStyles.Integer, invariant, out var l)) return false;
                    property.longValue = l;
                    break;
                case SerializedPropertyType.Enum:
                    if (!int.TryParse(value, System.Globalization.NumberStyles.Integer, invariant, out var e)) return false;
                    property.intValue = e;
                    break;
                case SerializedPropertyType.Boolean:
                    property.boolValue = value != "0" && !string.Equals(value, "false", StringComparison.OrdinalIgnoreCase);
                    break;
                case SerializedPropertyType.Float:
                    if (!double.TryParse(value, System.Globalization.NumberStyles.Float, invariant, out var d)) return false;
                    property.doubleValue = d;
                    break;
                case SerializedPropertyType.String:
                    property.stringValue = value ?? "";
                    break;
                default:
                    return false;
            }
            so.ApplyQuietly();
            return true;
        }
    }
}
