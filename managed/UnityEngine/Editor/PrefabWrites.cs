using System;
using System.Collections.Generic;
using System.IO;
using Unbound.Runtime;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace UnityEditor
{
    /// <summary>
    /// The prefab writes: overrides of an instance applied to its prefab or reverted, a prefab's
    /// contents loaded and saved, a prefab asset saved, a plain GameObject made an instance, as the
    /// Unity 6000.0.71f1 Editor does them (tests/projects/scenesave prefab-writes, prefab-api, edit).
    /// An apply writes the prefab (<see cref="PrefabAssets"/>), and every loaded instance of it takes
    /// the change keeping its own overrides (prefabInstanceUpdated is called for each); a revert
    /// changes the instance only (no callback). A user action dirties the instance's scene and can be
    /// undone (an undo of an apply writes the prefab again); an automated one does neither. Writes
    /// that make a new prefab asset (another path, CreatePrefab, SaveAsPrefabAssetAndConnect) fail
    /// loudly until Unbound registers new assets.
    /// </summary>
    public sealed partial class PrefabUtility
    {
        const string NotInstanceMessage = "Calling apply or revert methods on an object which is not part of a Prefab instance is not supported.";

        /// <summary>The instance an object belongs to: its own, its GameObject's, or (an added
        /// GameObject) its nearest linked parent's.</summary>
        static PrefabInstanceData InstanceOf(Object o)
        {
            if (o == null) return null;
            if (PrefabLinks.Of(o)?.Instance is PrefabInstanceData own) return own;
            for (var t = GameObjectOf(o)?.transform; t != null; t = t.parent)
                if (PrefabLinks.Of(t.m_GameObject)?.Instance is PrefabInstanceData data) return data;
            return null;
        }

        static PrefabInstanceData RequireInstance(Object o)
        {
            var data = o == null ? null : InstanceOf(o);
            if (data == null || IsPartOfPrefabAsset(o)) throw new ArgumentException(NotInstanceMessage, "prefabInstanceObject");
            return data;
        }

        static LoadedFile PrefabOf(PrefabInstanceData data, string assetPath)
        {
            var file = PrefabAssets.FileOf(data.SourceGuid)
                ?? throw new InvalidOperationException("The Prefab asset of the instance could not be loaded.");
            if (assetPath != null && !string.Equals(SceneFiles.ProjectRelative(assetPath), file.Path, StringComparison.Ordinal))
                throw new NotSupportedException($"Unbound applies an instance's overrides to its own prefab ({file.Path}) only, not to {assetPath} yet");
            return file;
        }

        /// <summary>A reference that cannot go into an asset: an object of a scene.</summary>
        static bool SceneObject(Object o) => o is not null && o != null && !o.IsPersistentAsset;

        /// <summary>References of an instance's object as its prefab's: the instance's objects as their
        /// sources, a scene's objects as none.</summary>
        static Func<Object, Object> ForAsset(Func<Object, Object> toSource) => x =>
        {
            var mapped = toSource(x);
            return SceneObject(mapped) && (mapped is GameObject || mapped is Component) ? null : mapped;
        };

        static void Dirty(Object o, InteractionMode mode)
        {
            if (mode == InteractionMode.UserAction && GameObjectOf(o)?.scene.Data is SceneData scene) EditorSceneManager.Dirty(scene);
        }

        static void RemoveStored(PrefabInstanceData data, Object o, Func<string, bool> selected)
        {
            var link = PrefabLinks.Of(o);
            if (link == null) return;
            int removed = data.Stored.RemoveAll(m => !m.Target.IsNull && m.Target.FileId == link.SourceFileId
                && string.Equals(m.Target.Guid ?? data.SourceGuid, link.SourceGuid, StringComparison.OrdinalIgnoreCase) && selected(m.Path));
            if (removed > 0) data.Edited = true;
            foreach (var key in new List<(long, string)>(data.ReferenceObjects.Keys))
                if (key.Item1 == link.SourceFileId && selected(key.Item2)) data.ReferenceObjects.Remove(key);
        }

        // ------------------------------------------------------------------ apply

        /// <summary>What one apply takes to the prefab.</summary>
        sealed class ApplyPlan
        {
            /// <summary>Objects and the properties to apply (null: every override but the default ones).</summary>
            internal readonly List<(Object Instance, string Property)> Properties = new List<(Object, string)>();
            internal readonly List<Component> AddedComponents = new List<Component>();
            internal readonly List<(GameObject Instance, Component Asset)> RemovedComponents = new List<(GameObject, Component)>();
            internal readonly List<GameObject> AddedGameObjects = new List<GameObject>();
            internal bool Structural => AddedComponents.Count + RemovedComponents.Count + AddedGameObjects.Count > 0;
        }

        static void Apply(PrefabInstanceData data, LoadedFile file, ApplyPlan plan, InteractionMode mode, string name)
        {
            var change = new PrefabAssets.Change(file);
            var refs = change.Refs;
            var snapshot = change.SnapshotOf(data) ?? new PrefabInstances.Snapshot { Refs = refs };
            var before = snapshot.Copy();
            var storedBefore = new List<(ObjectRef, string, string, ObjectRef)>(data.Stored);
            bool editedBefore = data.Edited;
            var toSource = PrefabInstances.ToSource(data);
            var forAsset = ForAsset(toSource);
            var assetStates = new List<(Object Asset, ObjectState Before)>();
            var applied = new List<Object>();

            foreach (var (o, property) in plan.Properties)
            {
                var source = PrefabLinks.Source(o);
                if (source == null || !snapshot.Values.TryGetValue(o, out var overrides)) continue;
                var chosen = new HashSet<string>(StringComparer.Ordinal);
                foreach (var (path, _) in overrides)
                    if (property == null ? !IsDefaultOverrideProperty(o, path) : PrefabValues.Within(path, property)) chosen.Add(path);
                var values = new List<(string, YText)>();
                foreach (var (path, value) in PrefabValues.Flatten(PrefabValues.Fields(o, toSource, refs)))
                {
                    if (!chosen.Contains(path)) continue;
                    // A reference to a scene object stays the instance's override (measured:
                    // tests/projects/scenesave prefab-api apply.mods.count).
                    if (value.Kind == YKind.Map && SceneObject(refs.ObjectOf(value))) continue;
                    values.Add((path, value));
                }
                if (values.Count == 0) continue;
                assetStates.Add((source, ObjectState.Capture(source)));
                PrefabValues.Write(source, values, refs);
                var done = new HashSet<string>(StringComparer.Ordinal);
                foreach (var (path, _) in values) done.Add(path);
                snapshot.Forget(o, path => done.Contains(path));
                RemoveStored(data, o, path => done.Contains(path));
                applied.Add(o);
            }
            foreach (var o in applied)
            {
                // Measured: an applied object's change is published twice, then the instances' update.
                ObjectChangeEvents.NoteProperties(o);
                ObjectChangeEvents.NoteProperties(o);
            }

            var madeComponents = new List<(Component Instance, Component Asset)>();
            foreach (var c in plan.AddedComponents)
            {
                if (PrefabLinks.Source(c.m_GameObject) is not GameObject sourceGo) continue;
                madeComponents.Add((c, AssetComponent(sourceGo, c, forAsset, refs)));
                var scene = c.gameObject.scene;
                ObjectChangeEvents.Note(ObjectChangeKind.ChangeGameObjectStructureHierarchy, new ChangeGameObjectStructureHierarchyEventArgs(c.gameObject.GetInstanceID(), scene));
                ObjectChangeEvents.Note(ObjectChangeKind.ChangeGameObjectStructure, new ChangeGameObjectStructureEventArgs(c.gameObject.GetInstanceID(), scene));
            }
            foreach (var (go, asset) in plan.RemovedComponents)
            {
                snapshot.Removed.Remove(asset);
                Object.DestroyImmediate(asset, true);
                ObjectChangeEvents.NoteInstanceProperties(data.Handle, go.scene);
            }
            var madeObjects = new List<Dictionary<Object, Object>>();
            foreach (var go in plan.AddedGameObjects)
            {
                if (go.transform.parent == null || PrefabLinks.Source(go.transform.parent.m_GameObject) is not GameObject sourceParent) continue;
                madeObjects.Add(AssetHierarchy(go, sourceParent.transform, toSource, refs));
                var root = PrefabInstances.RootOf(data);
                if (root != null)
                    ObjectChangeEvents.Note(ObjectChangeKind.ChangeGameObjectStructureHierarchy, new ChangeGameObjectStructureHierarchyEventArgs(root.GetInstanceID(), root.scene));
            }

            bool written = change.Commit(() =>
            {
                foreach (var (instance, asset) in madeComponents) Link(instance, file, asset, data);
                foreach (var map in madeObjects)
                    foreach (var pair in map) Link(pair.Key, file, pair.Value, data);
            });
            if (!written) return;
            foreach (var (instance, asset) in madeComponents)
            {
                ObjectChangeEvents.Note(ObjectChangeKind.CreateAssetObject, new CreateAssetObjectEventArgs(new GUID(file.Guid), asset.GetInstanceID(), default));
                ObjectChangeEvents.Note(ObjectChangeKind.ChangeGameObjectStructure, new ChangeGameObjectStructureEventArgs(instance.gameObject.GetInstanceID(), instance.gameObject.scene));
            }
            foreach (var go in plan.AddedGameObjects)
                if (go != null && PrefabLinks.Of(go) != null)
                    ObjectChangeEvents.Note(ObjectChangeKind.CreateGameObjectHierarchy, new CreateGameObjectHierarchyEventArgs(go.GetInstanceID(), go.scene));
            foreach (var (o, _) in plan.Properties) Dirty(o, mode);
            if (mode != InteractionMode.UserAction) return;
            if (plan.Structural)
            {
                Undo.RegisterOperation(name,
                    () => Debug.LogError($"Unbound: undoing {name} is not supported (the prefab's new or removed objects are not restored)."),
                    () => { });
                return;
            }
            var assetAfter = new List<(Object Asset, ObjectState After)>();
            foreach (var (asset, _) in assetStates) assetAfter.Add((asset, ObjectState.Capture(asset)));
            var storedAfter = new List<(ObjectRef, string, string, ObjectRef)>(data.Stored);
            var after = snapshot.Copy();
            void Swap(List<(Object, ObjectState)> states, List<(ObjectRef, string, string, ObjectRef)> stored, bool edited, PrefabInstances.Snapshot overrides)
            {
                var undoChange = new PrefabAssets.Change(file);
                undoChange.Replace(data, overrides.Copy());
                foreach (var (asset, state) in states) if (asset != null) state.Restore(asset);
                data.Stored.Clear();
                data.Stored.AddRange(stored);
                data.Edited = edited;
                foreach (var o in applied)
                {
                    ObjectChangeEvents.NoteProperties(o);
                    ObjectChangeEvents.NoteProperties(o);
                }
                undoChange.Commit();
            }
            Undo.RegisterOperation(name,
                () => Swap(assetStates, storedBefore, editedBefore, before),
                () => Swap(assetAfter, storedAfter, true, after));
        }

        static void Link(Object instance, LoadedFile file, Object asset, PrefabInstanceData data)
        {
            long id = PrefabAssets.IdOf(file, asset);
            if (id != 0 && instance != null) PrefabLinks.Set(instance, new PrefabLink { SourceGuid = file.Guid, SourceFileId = id, Instance = data });
        }

        /// <summary>A new component of a prefab asset's GameObject with another's values.</summary>
        static Component AssetComponent(GameObject assetGo, Component from, Func<Object, Object> remap, ValueReferences refs)
        {
            Component made;
            using (ObjectRegistry.AsAssets()) made = ComponentFactory.Create(from.GetType(), assetGo, deserializing: true);
            made.IsPersistentAsset = true;
            assetGo.m_Components.Add(made);
            PrefabValues.Write(made, PrefabValues.Flatten(PrefabValues.Fields(from, remap, refs)), refs);
            return made;
        }

        /// <summary>A copy of a GameObject hierarchy made a prefab asset's, under one of its
        /// Transforms (references into the copied hierarchy as the copies, others through
        /// <paramref name="remap"/>, a scene's objects as none). Originals to copies.</summary>
        static Dictionary<Object, Object> AssetHierarchy(GameObject from, Transform assetParent, Func<Object, Object> remap, ValueReferences refs)
        {
            var map = new Dictionary<Object, Object>(ReferenceEqualityComparer.Instance);
            void Make(Transform t, Transform parent)
            {
                var source = t.m_GameObject;
                GameObject go;
                using (ObjectRegistry.AsAssets())
                {
                    go = new GameObject(source.m_Name, bare: true) { m_IsPrefabAsset = true, IsPersistentAsset = true };
                    map[source] = go;
                    foreach (var c in source.m_Components)
                    {
                        if (c == null || c.m_Destroyed) continue;
                        var made = ComponentFactory.Create(c.GetType(), go, deserializing: true);
                        made.IsPersistentAsset = true;
                        if (made is Transform made_t)
                        {
                            go.m_Transform = made_t;
                            go.m_Components.Insert(0, made_t);
                        }
                        else go.m_Components.Add(made);
                        map[c] = made;
                    }
                }
                var transform = go.m_Transform;
                transform.m_Parent = parent;
                parent.m_Children.Add(transform);
                foreach (var child in t.m_Children) Make(child, transform);
            }
            Make(from.transform, assetParent);
            var forAsset = ForAsset(x => x != null && map.TryGetValue(x, out var copy) ? copy : remap(x));
            foreach (var pair in map)
                PrefabValues.Write(pair.Value, PrefabValues.Flatten(PrefabValues.Fields(pair.Key, forAsset, refs)), refs);
            return map;
        }

        public static void ApplyPropertyOverride(SerializedProperty instanceProperty, string assetPath, InteractionMode action)
        {
            if (instanceProperty == null) throw new ArgumentNullException(nameof(instanceProperty));
            var target = instanceProperty.serializedObject.targetObject;
            var data = RequireInstance(target);
            var plan = new ApplyPlan();
            plan.Properties.Add((target, instanceProperty.propertyPath));
            Apply(data, PrefabOf(data, assetPath), plan, action, "Apply Property");
        }

        public static void ApplyObjectOverride(Object instanceComponentOrGameObject, string assetPath, InteractionMode action)
        {
            var data = RequireInstance(instanceComponentOrGameObject);
            var plan = new ApplyPlan();
            plan.Properties.Add((instanceComponentOrGameObject, null));
            Apply(data, PrefabOf(data, assetPath), plan, action, "Apply Object Override");
        }

        /// <summary>Every override of the instance: its objects' properties (not the root's name
        /// and pose), its added and removed components and its added GameObjects.</summary>
        public static void ApplyPrefabInstance(GameObject instanceRoot, InteractionMode action)
        {
            var data = RequireInstance(instanceRoot);
            var plan = new ApplyPlan();
            foreach (var o in PrefabInstances.ObjectsOf(data))
            {
                plan.Properties.Add((o, null));
                if (o is not GameObject go) continue;
                foreach (var c in go.m_Components)
                    if (c != null && !c.m_Destroyed && PrefabLinks.Of(c) == null) plan.AddedComponents.Add(c);
                if (PrefabLinks.Source(go) is GameObject sourceGo)
                    foreach (var sc in sourceGo.m_Components)
                        if (sc != null && !sc.m_Destroyed && PrefabInstances.Counterpart(go, sc, data) == null) plan.RemovedComponents.Add((go, sc));
                foreach (var child in go.transform.m_Children)
                    if (PrefabLinks.Of(child.m_GameObject) == null) plan.AddedGameObjects.Add(child.m_GameObject);
            }
            Apply(data, PrefabOf(data, null), plan, action, "Apply Prefab Instance");
        }

        public static void ApplyPrefabInstances(GameObject[] instanceRoots, InteractionMode action)
        {
            if (instanceRoots == null) throw new ArgumentNullException(nameof(instanceRoots));
            foreach (var root in instanceRoots) ApplyPrefabInstance(root, action);
        }

        public static void ApplyAddedComponent(Component component, string assetPath, InteractionMode action)
        {
            if (component == null) throw new ArgumentNullException(nameof(component));
            var data = RequireInstance(component);
            if (!IsAddedComponentOverride(component)) throw new ArgumentException("Cannot apply component: it is not an added component override.", nameof(component));
            var plan = new ApplyPlan();
            plan.AddedComponents.Add(component);
            Apply(data, PrefabOf(data, assetPath), plan, action, "Apply Added Component");
        }

        public static void ApplyRemovedComponent(GameObject instanceGameObject, Component assetComponent, InteractionMode action)
        {
            if (instanceGameObject == null) throw new ArgumentNullException(nameof(instanceGameObject));
            if (assetComponent == null) throw new ArgumentNullException(nameof(assetComponent));
            var data = RequireInstance(instanceGameObject);
            var plan = new ApplyPlan();
            plan.RemovedComponents.Add((instanceGameObject, assetComponent));
            Apply(data, PrefabOf(data, null), plan, action, "Apply Removed Component");
        }

        public static void ApplyAddedGameObject(GameObject gameObject, string assetPath, InteractionMode action)
        {
            if (gameObject == null) throw new ArgumentNullException(nameof(gameObject));
            var data = RequireInstance(gameObject);
            if (!IsAddedGameObjectOverride(gameObject)) throw new ArgumentException("Cannot apply GameObject: it is not an added GameObject override.", nameof(gameObject));
            var plan = new ApplyPlan();
            plan.AddedGameObjects.Add(gameObject);
            Apply(data, PrefabOf(data, assetPath), plan, action, "Apply Added GameObject");
        }

        public static void ApplyAddedGameObjects(GameObject[] gameObjects, string assetPath, InteractionMode action)
        {
            if (gameObjects == null) throw new ArgumentNullException(nameof(gameObjects));
            foreach (var go in gameObjects) ApplyAddedGameObject(go, assetPath, action);
        }

        // ------------------------------------------------------------------ revert

        /// <summary>The instance takes its prefab's values again but for the overrides
        /// <paramref name="edit"/> keeps (its objects as they are otherwise).</summary>
        static void Revert(PrefabInstanceData data, Action<PrefabInstances.Snapshot> edit, Object changed, InteractionMode mode)
        {
            var snapshot = PrefabInstances.Capture(data, new ValueReferences());
            edit(snapshot);
            PrefabInstances.Merge(data, snapshot);
            Dirty(changed, mode);
        }

        public static void RevertPropertyOverride(SerializedProperty instanceProperty, InteractionMode action)
        {
            if (instanceProperty == null) throw new ArgumentNullException(nameof(instanceProperty));
            var target = instanceProperty.serializedObject.targetObject;
            var data = RequireInstance(target);
            var property = instanceProperty.propertyPath;
            RemoveStored(data, target, path => PrefabValues.Within(path, property));
            Revert(data, s => s.Forget(target, path => PrefabValues.Within(path, property)), target, action);
            ObjectChangeEvents.NoteProperties(target);
        }

        public static void RevertObjectOverride(Object instanceComponentOrGameObject, InteractionMode action)
        {
            var data = RequireInstance(instanceComponentOrGameObject);
            var o = instanceComponentOrGameObject;
            RemoveStored(data, o, path => !IsDefaultOverrideProperty(o, path));
            Revert(data, s => s.Forget(o, path => !IsDefaultOverrideProperty(o, path)), o, action);
            ObjectChangeEvents.NoteProperties(o);
        }

        public static void RevertAddedComponent(Component component, InteractionMode action)
        {
            if (component == null) throw new ArgumentNullException(nameof(component));
            RequireInstance(component);
            if (!IsAddedComponentOverride(component)) throw new ArgumentException("Cannot revert component: it is not an added component override.", nameof(component));
            var go = component.gameObject;
            Dirty(go, action);
            if (action == InteractionMode.UserAction) Undo.DestroyObjectImmediate(component);
            else
            {
                Object.DestroyImmediate(component);
                ObjectChangeEvents.Note(ObjectChangeKind.ChangeGameObjectStructure, new ChangeGameObjectStructureEventArgs(go.GetInstanceID(), go.scene));
            }
        }

        public static void RevertAddedGameObject(GameObject gameObject, InteractionMode action)
        {
            if (gameObject == null) throw new ArgumentNullException(nameof(gameObject));
            RequireInstance(gameObject);
            if (!IsAddedGameObjectOverride(gameObject)) throw new ArgumentException("Cannot revert GameObject: it is not an added GameObject override.", nameof(gameObject));
            Dirty(gameObject, action);
            if (action == InteractionMode.UserAction) Undo.DestroyObjectImmediate(gameObject);
            else
            {
                var parent = gameObject.transform.parent;
                var (id, scene) = (gameObject.GetInstanceID(), gameObject.scene);
                Object.DestroyImmediate(gameObject);
                ObjectChangeEvents.Note(ObjectChangeKind.DestroyGameObjectHierarchy, new DestroyGameObjectHierarchyEventArgs(id, ObjectChangeEvents.ParentId(parent), scene));
            }
        }

        public static void RevertRemovedComponent(GameObject instanceGameObject, Component assetComponent, InteractionMode action)
        {
            if (instanceGameObject == null) throw new ArgumentNullException(nameof(instanceGameObject));
            if (assetComponent == null) throw new ArgumentNullException(nameof(assetComponent));
            var data = RequireInstance(instanceGameObject);
            ObjectChangeEvents.NoteProperties(instanceGameObject);
            Revert(data, s => s.Removed.Remove(assetComponent), instanceGameObject, action);
            // Measured: the component made again is published as a created object of no asset.
            if (PrefabInstances.Counterpart(instanceGameObject, assetComponent, data) is Component made)
                ObjectChangeEvents.Note(ObjectChangeKind.CreateAssetObject, new CreateAssetObjectEventArgs(default, made.GetInstanceID(), default));
            ObjectChangeEvents.Note(ObjectChangeKind.ChangeGameObjectStructure, new ChangeGameObjectStructureEventArgs(instanceGameObject.GetInstanceID(), instanceGameObject.scene));
        }

        /// <summary>Every override but the root's name and pose: added components and GameObjects
        /// go, removed components come back, every object takes its prefab's values.</summary>
        public static void RevertPrefabInstance(GameObject instanceRoot, InteractionMode action)
        {
            var data = RequireInstance(instanceRoot);
            var objects = PrefabInstances.ObjectsOf(data);
            foreach (var o in objects)
            {
                if (o is not GameObject go || go == null) continue;
                foreach (var c in go.m_Components.ToArray())
                    if (c != null && !c.m_Destroyed && !(c is Transform) && PrefabLinks.Of(c) == null) Object.DestroyImmediate(c);
                foreach (var child in go.transform.m_Children.ToArray())
                    if (PrefabLinks.Of(child.m_GameObject) == null) Object.DestroyImmediate(child.m_GameObject);
            }
            foreach (var o in objects) RemoveStored(data, o, path => !IsDefaultOverrideProperty(o, path));
            Revert(data, s =>
            {
                foreach (var o in objects) s.Forget(o, path => !IsDefaultOverrideProperty(o, path));
                s.Removed.Clear();
            }, instanceRoot, action);
            var root = PrefabInstances.RootOf(data);
            if (root != null)
                ObjectChangeEvents.Note(ObjectChangeKind.ChangeGameObjectStructureHierarchy, new ChangeGameObjectStructureHierarchyEventArgs(root.GetInstanceID(), root.scene));
        }

        // ------------------------------------------------------------------ modifications

        /// <summary>The instance's modifications become these (ordered by target, as the Editor keeps
        /// them) and its objects take its prefab's values with them (measured: tests/projects/scenesave
        /// prefab-api set.*: no callback, the scene not dirtied).</summary>
        public static void SetPropertyModifications(Object targetPrefab, PropertyModification[] modifications)
        {
            if (modifications == null) throw new ArgumentNullException(nameof(modifications));
            var data = RequireInstance(targetPrefab);
            var refs = new ValueReferences();
            data.Stored.Clear();
            data.ReferenceObjects.Clear();
            foreach (var m in modifications)
            {
                if (m == null) continue;
                var target = m.target != null && AssetDatabaseRuntime.TryGetLocation(m.target, out var guid, out var fileId) ? new ObjectRef(fileId, guid, 3) : default;
                var reference = default(ObjectRef);
                if (m.objectReference != null)
                {
                    if (AssetDatabaseRuntime.TryGetLocation(m.objectReference, out var refGuid, out var refId)) reference = new ObjectRef(refId, refGuid, 2);
                    data.ReferenceObjects[(target.FileId, m.propertyPath ?? "")] = m.objectReference;
                }
                data.Stored.Add((target, m.propertyPath ?? "", m.value ?? "", reference));
            }
            data.OrderByTarget();
            data.Edited = true;
            var snapshot = new PrefabInstances.Snapshot { Refs = refs };
            foreach (var o in PrefabInstances.ObjectsOf(data))
            {
                var link = PrefabLinks.Of(o);
                var paths = new HashSet<string>(StringComparer.Ordinal);
                foreach (var (path, _) in PrefabValues.Flatten(PrefabValues.Fields(o, null, refs))) paths.Add(path);
                foreach (var m in data.Stored)
                {
                    if (m.Target.IsNull || m.Target.FileId != link.SourceFileId || !string.Equals(m.Target.Guid ?? data.SourceGuid, link.SourceGuid, StringComparison.OrdinalIgnoreCase)) continue;
                    data.ReferenceObjects.TryGetValue((m.Target.FileId, m.Path), out var referenced);
                    if (paths.Contains(m.Path))
                    {
                        var value = referenced != null || !m.Reference.IsNull
                            ? SceneFiles.FlowValue(refs.Text(referenced))
                            : YText.Scalar(UnityYamlWriter.Scalar(m.Value));
                        if (!snapshot.Values.TryGetValue(o, out var list)) snapshot.Values[o] = list = new List<(string, YText)>();
                        list.Add((m.Path, value));
                    }
                    else
                    {
                        if (!snapshot.Unmatched.TryGetValue(o, out var unmatched)) snapshot.Unmatched[o] = unmatched = new List<(string, string, Object)>();
                        unmatched.Add((m.Path, m.Value, referenced));
                    }
                }
            }
            PrefabInstances.Merge(data, snapshot);
        }

        public static List<AddedComponent> GetAddedComponents(GameObject prefabInstance)
        {
            var data = RequireOverridesInstance(prefabInstance);
            var list = new List<AddedComponent>();
            foreach (var o in PrefabInstances.ObjectsOf(data))
                if (o is GameObject go)
                    foreach (var c in go.m_Components)
                        if (c != null && !c.m_Destroyed && PrefabLinks.Of(c) == null) list.Add(new AddedComponent { instanceComponent = c });
            return list;
        }

        public static List<RemovedComponent> GetRemovedComponents(GameObject prefabInstance)
        {
            var data = RequireOverridesInstance(prefabInstance);
            var list = new List<RemovedComponent>();
            foreach (var o in PrefabInstances.ObjectsOf(data))
                if (o is GameObject go && PrefabLinks.Source(go) is GameObject sourceGo)
                    foreach (var sc in sourceGo.m_Components)
                        if (sc != null && !sc.m_Destroyed && PrefabInstances.Counterpart(go, sc, data) == null)
                            list.Add(new RemovedComponent { containingInstanceGameObject = go, assetComponent = sc });
            return list;
        }

        public static List<AddedGameObject> GetAddedGameObjects(GameObject prefabInstance)
        {
            var data = RequireOverridesInstance(prefabInstance);
            var list = new List<AddedGameObject>();
            foreach (var o in PrefabInstances.ObjectsOf(data))
                if (o is GameObject go)
                    for (int i = 0; i < go.transform.m_Children.Count; i++)
                        if (PrefabLinks.Of(go.transform.m_Children[i].m_GameObject) == null)
                            list.Add(new AddedGameObject { instanceGameObject = go.transform.m_Children[i].m_GameObject, siblingIndex = i });
            return list;
        }

        /// <summary>As GetObjectOverrides checks its argument (measured: tests/projects/scenesave
        /// overrides.null, overrides.plain).</summary>
        static PrefabInstanceData RequireOverridesInstance(GameObject prefabInstance)
        {
            if (prefabInstance == null) throw new ArgumentNullException(nameof(prefabInstance));
            if (!IsPartOfPrefabInstance(prefabInstance)) throw new ArgumentException("Provided GameObject is not a Prefab instance");
            return PrefabLinks.Of(GetOutermostPrefabInstanceRoot(prefabInstance))?.Instance;
        }

        // ------------------------------------------------------------------ prefab assets and contents

        /// <summary>Writes a prefab asset Unbound loaded, after its objects were changed by script;
        /// its loaded instances take the change (measured: tests/projects/scenesave prefab-writes
        /// assetSave.*).</summary>
        public static GameObject SavePrefabAsset(GameObject asset, out bool savedSuccessfully)
        {
            savedSuccessfully = false;
            if (asset == null) throw new ArgumentNullException(nameof(asset));
            if (!IsPartOfPrefabAsset(asset) || asset.transform.parent != null)
                throw new ArgumentException("Can't save part of a Prefab: the GameObject must be the root of a Prefab asset.", nameof(asset));
            var file = PrefabAssets.FileOf(GuidOf(asset)) ?? throw new ArgumentException("The Prefab asset could not be found.", nameof(asset));
            var change = new PrefabAssets.Change(file, PrefabAssets.FileValues(file));
            savedSuccessfully = change.Commit();
            return file.MainObject as GameObject;
        }

        public static GameObject SavePrefabAsset(GameObject asset) => SavePrefabAsset(asset, out _);

        /// <summary>A prefab's contents, loaded for editing: a copy of its hierarchy in a preview
        /// scene of its own (not an instance, nor the asset), each object remembering its asset's.</summary>
        public static GameObject LoadPrefabContents(string assetPath)
        {
            if (assetPath == null) throw new ArgumentNullException(nameof(assetPath));
            var file = PrefabAssets.FileAt(SceneFiles.ProjectRelative(assetPath));
            if (file == null) throw new ArgumentException($"Path: {assetPath}, does not exist");
            return PrefabContents.Load(file);
        }

        public static void UnloadPrefabContents(GameObject contentsRoot)
        {
            if (!PrefabContents.Unload(contentsRoot)) throw new ArgumentException("Specified object is not part of Prefab contents");
        }

        public static GameObject SaveAsPrefabAsset(GameObject instanceRoot, string assetPath) => SaveAsPrefabAsset(instanceRoot, assetPath, out _);

        /// <summary>A prefab's contents saved to its own file, or a plain hierarchy over a prefab
        /// that exists (its root and the root's Transform keep their ids, measured for ReplacePrefab:
        /// tests/projects/scenesave replace.prefab). A new prefab file is not supported yet.</summary>
        public static GameObject SaveAsPrefabAsset(GameObject instanceRoot, string assetPath, out bool success)
        {
            success = false;
            // The argument checks as measured (tests/projects/scenesave prefab-writes errors.*).
            if (instanceRoot == null) throw new ArgumentNullException("Parameter root is null");
            if (assetPath == null || !assetPath.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException($"Given path is not valid: '{assetPath}'");
            var folder = Path.GetDirectoryName(SceneFiles.ProjectRelative(assetPath));
            if (string.IsNullOrEmpty(folder) || !Directory.Exists(Path.Combine(ProjectInfo.ProjectPath, folder)))
                throw new ArgumentException($"Given path does not exist: '{assetPath}'");
            var path = SceneFiles.ProjectRelative(assetPath).Replace('\\', '/');
            var file = PrefabAssets.FileAt(path);
            if (file == null)
                throw new NotSupportedException($"Unbound cannot make a new prefab asset ({path}) yet: only a prefab that exists can be saved");
            var contents = PrefabContents.Of(instanceRoot);
            if (contents != null && contents.File == file) success = PrefabContents.Save(contents);
            else if (contents == null && !IsPartOfAnyPrefab(instanceRoot)) success = PrefabContents.Replace(instanceRoot, file);
            else throw new NotSupportedException($"Unbound saves a prefab's contents to its own file, or a plain GameObject over a prefab, only (not {instanceRoot.name} to {path})");
            return success ? file.MainObject as GameObject : null;
        }

#pragma warning disable CS0618 // ReplacePrefabOptions is obsolete, as the methods taking it.
        [Obsolete("Use SaveAsPrefabAsset or SaveAsPrefabAssetAndConnect with a path instead.")]
        public static GameObject ReplacePrefab(GameObject go, Object targetPrefab, ReplacePrefabOptions replaceOptions)
        {
            if (go == null) throw new ArgumentNullException(nameof(go));
            var file = (targetPrefab == null ? null : PrefabAssets.FileOf(GuidOf(targetPrefab)))
                ?? throw new ArgumentException("The target is not a Prefab asset.", nameof(targetPrefab));
            if (replaceOptions != ReplacePrefabOptions.Default)
                throw new NotSupportedException($"Unbound replaces prefabs with ReplacePrefabOptions.Default only, not {replaceOptions}");
            return PrefabContents.Replace(go, file) ? file.MainObject as GameObject : null;
        }

        [Obsolete("Use SaveAsPrefabAsset with a path instead.")]
        public static GameObject ReplacePrefab(GameObject go, Object targetPrefab) => ReplacePrefab(go, targetPrefab, ReplacePrefabOptions.Default);
#pragma warning restore CS0618

        // ------------------------------------------------------------------ conversion

        /// <summary>
        /// A plain GameObject made an instance of a prefab: its objects matched to the prefab's
        /// (by hierarchy: the root to the root, children by name in order, components by class in
        /// order), those not matched removed unless the settings keep them as added overrides, the
        /// prefab's objects it lacks made, every matched object taking the prefab's values unless
        /// the settings record them as overrides, the root renamed to the prefab's name made unique
        /// among its siblings (measured: tests/projects/scenesave prefab-api convert.*, the settings'
        /// defaults: prefab-writes settings.defaults; other match modes fail loudly).
        /// </summary>
        public static void ConvertToPrefabInstance(GameObject plainGameObject, GameObject prefabAssetRoot, ConvertToPrefabInstanceSettings settings, InteractionMode mode)
        {
            if (plainGameObject == null) throw new ArgumentNullException(nameof(plainGameObject));
            if (prefabAssetRoot == null) throw new ArgumentNullException(nameof(prefabAssetRoot));
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            if (IsPartOfAnyPrefab(plainGameObject)) throw new ArgumentException("The GameObject to convert must be a plain GameObject, not part of a Prefab.", nameof(plainGameObject));
            if (!IsPartOfPrefabAsset(prefabAssetRoot) || prefabAssetRoot.transform.parent != null)
                throw new ArgumentException("The Prefab to convert to must be the root of a Prefab asset.", nameof(prefabAssetRoot));
            if (settings.objectMatchMode != ObjectMatchMode.ByHierarchy)
                throw new NotSupportedException($"Unbound converts to prefab instances with ObjectMatchMode.ByHierarchy only, not {settings.objectMatchMode}");
            var guid = GuidOf(prefabAssetRoot);
            var data = new PrefabInstanceData { SourceGuid = guid };
            void Match(GameObject plain, GameObject asset)
            {
                LinkTo(plain, asset, data);
                var used = new HashSet<Component>(ReferenceEqualityComparer.Instance);
                foreach (var c in plain.m_Components.ToArray())
                {
                    if (c == null || c.m_Destroyed) continue;
                    Component found = null;
                    foreach (var ac in asset.m_Components)
                        if (ac != null && !ac.m_Destroyed && ac.GetType() == c.GetType() && used.Add(ac)) { found = ac; break; }
                    if (found != null) LinkTo(c, found, data);
                    else if (!settings.componentsNotMatchedBecomesOverride && !(c is Transform)) Object.DestroyImmediate(c);
                }
                var usedChildren = new HashSet<Transform>(ReferenceEqualityComparer.Instance);
                foreach (var child in plain.transform.m_Children.ToArray())
                {
                    Transform found = null;
                    foreach (var ac in asset.transform.m_Children)
                        if (ac.m_GameObject.m_Name == child.m_GameObject.m_Name && usedChildren.Add(ac)) { found = ac; break; }
                    if (found != null) Match(child.m_GameObject, found.m_GameObject);
                    else if (!settings.gameObjectsNotMatchedBecomesOverride) Object.DestroyImmediate(child.m_GameObject);
                }
            }
            Match(plainGameObject, prefabAssetRoot);
            if (settings.changeRootNameToAssetName)
                plainGameObject.m_Name = UniqueSiblingName(plainGameObject, prefabAssetRoot.m_Name);
            foreach (var (target, path) in DefaultOverrides(prefabAssetRoot))
                if (AssetDatabaseRuntime.TryGetLocation(target, out var targetGuid, out var targetId))
                {
                    Object live = target is GameObject ? plainGameObject : plainGameObject.transform;
                    data.Stored.Add((new ObjectRef(targetId, targetGuid, 3), path, DefaultOverrideValue(live, path), default));
                }
            data.OrderByTarget();
            var refs = new ValueReferences();
            var snapshot = PrefabInstances.Capture(data, refs, liveDifferences: settings.recordPropertyOverridesOfMatches);
            // The prefab's objects the plain one lacks are made (none is a removed override).
            snapshot.Removed.Clear();
            PrefabInstances.Merge(data, snapshot);
            Dirty(plainGameObject, mode);
        }

        static void LinkTo(Object instance, Object asset, PrefabInstanceData data)
        {
            if (AssetDatabaseRuntime.TryGetLocation(asset, out var guid, out var fileId))
                PrefabLinks.Set(instance, new PrefabLink { SourceGuid = guid, SourceFileId = fileId, Instance = data });
        }

        /// <summary>A name no other sibling has: the name, else "name (n)" with the smallest n from 1.</summary>
        static string UniqueSiblingName(GameObject go, string name)
        {
            var taken = new HashSet<string>(StringComparer.Ordinal);
            var parent = go.transform.parent;
            if (parent != null)
            {
                foreach (var t in parent.m_Children) if (t.m_GameObject != go) taken.Add(t.m_GameObject.m_Name);
            }
            else if (go.scene.Data is SceneData scene)
            {
                foreach (var root in scene.Roots) if (root != null && root != go) taken.Add(root.m_Name);
            }
            if (!taken.Contains(name)) return name;
            for (int n = 1; ; n++)
            {
                var candidate = $"{name} ({n})";
                if (!taken.Contains(candidate)) return candidate;
            }
        }
    }

    [Flags]
    [Obsolete("This has turned into the more explicit APIs, SavePrefabAsset, SaveAsPrefabAsset, SaveAsPrefabAssetAndConnect")]
    public enum ReplacePrefabOptions
    {
        Default = 0,
        ConnectToPrefab = 1,
        ReplaceNameBased = 2,
    }

    public enum ObjectMatchMode
    {
        NoMatchingPerformed = 0,
        ByName = 1,
        ByHierarchy = 2,
    }

    /// <summary>The settings of PrefabUtility.ConvertToPrefabInstance; a new one has the
    /// release's defaults (measured: tests/projects/scenesave prefab-writes settings.defaults).</summary>
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    public partial class ConvertToPrefabInstanceSettings
    {
        public ConvertToPrefabInstanceSettings() { }

        public ObjectMatchMode objectMatchMode { get; set; } = ObjectMatchMode.ByHierarchy;
        public bool componentsNotMatchedBecomesOverride { get; set; }
        public bool gameObjectsNotMatchedBecomesOverride { get; set; }
        public bool recordPropertyOverridesOfMatches { get; set; }
        public bool changeRootNameToAssetName { get; set; } = true;
        public bool logInfo { get; set; }
    }
}

namespace UnityEditor.SceneManagement
{
    public partial class ObjectOverride
    {
        public override void Apply(string prefabAssetPath, InteractionMode mode) => PrefabUtility.ApplyObjectOverride(instanceObject, prefabAssetPath, mode);

        public override void Revert(InteractionMode mode) => PrefabUtility.RevertObjectOverride(instanceObject, mode);
    }

    public partial class AddedComponent : PrefabOverride
    {
        public AddedComponent() { }

        public Component instanceComponent { get; set; }

        public override void Apply(string prefabAssetPath, InteractionMode mode) => PrefabUtility.ApplyAddedComponent(instanceComponent, prefabAssetPath, mode);

        public override void Revert(InteractionMode mode) => PrefabUtility.RevertAddedComponent(instanceComponent, mode);

        [Unbound.EditorFaithful("an added component has no object in the prefab (unmeasured)")]
        public override Object GetAssetObject() => null;

        internal override Object GetObject() => instanceComponent;
    }

    public partial class RemovedComponent : PrefabOverride
    {
        public RemovedComponent() { }

        public GameObject containingInstanceGameObject { get; set; }

        public Component assetComponent { get; set; }

        public override void Apply(string prefabAssetPath, InteractionMode mode) => PrefabUtility.ApplyRemovedComponent(containingInstanceGameObject, assetComponent, mode);

        public override void Revert(InteractionMode mode) => PrefabUtility.RevertRemovedComponent(containingInstanceGameObject, assetComponent, mode);

        public override Object GetAssetObject() => assetComponent;

        internal override Object GetObject() => assetComponent;
    }

    public partial class AddedGameObject : PrefabOverride
    {
        public AddedGameObject() { }

        public GameObject instanceGameObject { get; set; }

        public int siblingIndex { get; set; }

        public override void Apply(string prefabAssetPath, InteractionMode mode) => PrefabUtility.ApplyAddedGameObject(instanceGameObject, prefabAssetPath, mode);

        public override void Revert(InteractionMode mode) => PrefabUtility.RevertAddedGameObject(instanceGameObject, mode);

        [Unbound.EditorFaithful("an added GameObject has no object in the prefab (unmeasured)")]
        public override Object GetAssetObject() => null;

        internal override Object GetObject() => instanceGameObject;
    }
}
