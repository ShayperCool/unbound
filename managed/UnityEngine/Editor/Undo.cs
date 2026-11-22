using System;
using System.Collections.Generic;
using System.Linq;
using Unbound.Runtime;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace UnityEditor
{
    /// <summary>
    /// The Editor's undo stack, in Play mode (tests/projects/editor measured it): operations are
    /// recorded into the current group (0 when Play mode starts; editor code increments it), a
    /// group is named by <see cref="SetCurrentGroupName"/> else by its operations: a created
    /// object's record over an added component's over the others (object and property records),
    /// the latest of equal rank (tests/projects/serializedundo priority.*, records.*). Object and
    /// property records name it only once Undo's records are flushed (the end of the frame,
    /// FlushUndoRecordObjects, a new group, an undo or a redo; until then the current group name is
    /// the last named group's), undo and redo apply a whole group and each advances the current
    /// group by one, and new operations drop what could be redone. Object state is the serialized state Unbound models (script fields,
    /// GameObject and Transform properties, enabled flags). Recreating destroyed objects on undo is
    /// not supported and says so.
    /// </summary>
    public partial class Undo
    {
        public delegate void UndoRedoCallback();
        public delegate void UndoRedoEventCallback(in UndoRedoInfo undo);
        public delegate void WillFlushUndoRecord();

        public static UndoRedoCallback undoRedoPerformed;
        public static UndoRedoEventCallback undoRedoEvent;
        public static WillFlushUndoRecord willFlushUndoRecord;

        sealed class Group
        {
            public int Index;
            public string Name;
            public readonly List<Operation> Operations = new List<Operation>();
            public string DisplayName => Name ?? Naming(publishedOnly: false) ?? "";

            /// <summary>The name the group shows before an undo: its own, else its naming flushed
            /// operation's; null while it has neither.</summary>
            public string PublishedName => Name ?? Naming(publishedOnly: true);

            // The operation of highest rank names the group, the latest of them.
            string Naming(bool publishedOnly)
            {
                Operation naming = null;
                foreach (var operation in Operations)
                    if ((!publishedOnly || operation.Published) && (naming == null || operation.NameRank >= naming.NameRank)) naming = operation;
                return naming?.Name;
            }

            public void Publish()
            {
                int batch = ++s_Batch;
                foreach (var operation in Operations) operation.Publish(batch);
            }
        }

        abstract class Operation
        {
            public string Name;
            /// <summary>Flushed: the operation names its group.</summary>
            public bool Published;
            /// <summary>How the operation ranks for naming its group: created objects 2, added
            /// components 1, the others 0.</summary>
            public int NameRank;
            public abstract void Undo();
            public abstract void Redo();
            /// <summary>Notes the changes the operation made (done or redone), or its undo made.</summary>
            public virtual void NoteChanges(bool undone) { }
            public virtual bool Concerns(Object o) => false;

            /// <summary>Which entry of Undo.GetRecords the operation is part of: recorded objects
            /// and properties get the number of the flush that found them changed, and those of one
            /// flush (or of one RegisterCompleteObjectUndo or RegisterFullObjectHierarchyUndo call)
            /// that follow each other are one entry; <see cref="Unlisted"/> until then. Other
            /// operations are entries of their own (<see cref="OwnEntries"/>).</summary>
            public int Batch = OwnEntries;
            public const int OwnEntries = -1, Unlisted = 0;

            /// <summary>A flush reached the operation: it names its group, and a recorded object
            /// that changed is listed from now on.</summary>
            public void Publish(int batch)
            {
                if (Published) return;
                Published = true;
                if (Batch == Unlisted && ChangedSinceRecorded) Batch = batch;
            }

            protected virtual bool ChangedSinceRecorded => true;

            /// <summary>The operation's own entries of Undo.GetRecords.</summary>
            public virtual void List(List<string> entries) => entries.Add(Name);
        }

        /// <summary>The last flush or complete registration numbered (<see cref="Operation.Batch"/>).</summary>
        static int s_Batch;

        static readonly List<Group> s_Undo = new List<Group>();
        static readonly List<Group> s_Redo = new List<Group>();
        static int s_Current;
        static bool s_Processing;

        public static bool isProcessing => s_Processing;

        static Group Current(bool create)
        {
            if (s_Undo.Count > 0 && s_Undo[^1].Index == s_Current) return s_Undo[^1];
            if (!create) return null;
            var group = new Group { Index = s_Current };
            s_Undo.Add(group);
            return group;
        }

        static void Add(Operation operation)
        {
            if (s_Processing) return;
            operation.NoteChanges(undone: false);
            s_Redo.Clear();
            Current(true).Operations.Add(operation);
            // Object and property records name their group once flushed; the others at once
            // (a selection change names it in the same frame: tests/projects/editor undo.f0).
            if (operation is StateOperation || operation is PropertiesOperation) s_Unpublished.Add(operation);
            else operation.Published = true;
            if (operation is StateOperation state) s_Unflushed.Add(state);
        }

        /// <summary>Recordings whose object may have changed since: a flush marks the changed ones
        /// dirty (EditorUtility.SetDirty), which dirties their scene (measured:
        /// tests/projects/scenesave second.undo).</summary>
        static readonly List<StateOperation> s_Unflushed = new List<StateOperation>();

        /// <summary>Operations recorded since the last flush: they name their group from it on.</summary>
        static readonly List<Operation> s_Unpublished = new List<Operation>();

        /// <summary>Marks the objects recorded since the last flush dirty when they changed, and
        /// passes the property changes recorded since to Undo.postprocessModifications (a handler's
        /// exception is logged, the flush goes on; what the handlers return changes no value:
        /// measured, tests/projects/scenesave filter.*, throw.*).</summary>
        internal static void FlushDirtiness()
        {
            PublishRecords();
            foreach (var state in s_Unflushed)
            {
                state.DirtyIfChanged();
                // A recorded object's prefab instance takes its changes (scenesave modification-timing undo.flushed).
                Unbound.Runtime.PrefabInstances.QueueRecord(state.Target);
            }
            Unbound.Runtime.PrefabInstances.RecordPending();
            s_Unflushed.Clear();
            if (s_Recorded.Count == 0) return;
            var recorded = s_Recorded.ToArray();
            s_Recorded.Clear();
            if (postprocessModifications == null) return;
            try { InvokePostprocessModifications(recorded); }
            catch (Exception e) { Debug.LogException(e); }
        }

        static readonly List<UndoPropertyModification> s_Recorded = new List<UndoPropertyModification>();

        /// <summary>Property changes a recorded edit made, for the next flush.</summary>
        internal static void QueueModifications(UndoPropertyModification[] modifications) => s_Recorded.AddRange(modifications);

        public static int GetCurrentGroup() => s_Current;

        public static string GetCurrentGroupName()
        {
            for (int i = s_Undo.Count - 1; i >= 0; i--)
                if (s_Undo[i].PublishedName is { } name) return name;
            return "";
        }

        public static void SetCurrentGroupName(string name) => Current(true).Name = name;

        /// <summary>Closes the current group: its records name it from now on (tests/projects/serializedundo
        /// split.applied).</summary>
        public static void IncrementCurrentGroup()
        {
            PublishRecords();
            s_Current++;
        }

        static void PublishRecords()
        {
            if (s_Unpublished.Count == 0) return;
            int batch = ++s_Batch;
            foreach (var operation in s_Unpublished) operation.Publish(batch);
            s_Unpublished.Clear();
        }

        // ------------------------------------------------------------------ the record list

        /// <summary>The name a recorded object's entry has when its record was given none.</summary>
        const string UnnamedRecord = "Record Modification";

        /// <summary>
        /// The entries of the undo and redo lists, oldest first (the next to redo first). An entry
        /// is not a group: a group's name set by SetCurrentGroupName is one; the objects and
        /// properties recorded in a group that one flush found changed are one, named by the last
        /// of them (an unchanged or unflushed record is none); a complete object or hierarchy
        /// registration is one at once; a registered created object is two, an object destroyed
        /// through Undo a selection clear and its own; other operations are one each. Matches the
        /// Unity 6000.0.71f1 Editor, tests/projects/scenesave scene-api-rest undo.*. Not measured:
        /// selection changes, a group named after its records were listed, created objects that
        /// are no GameObject, records of several objects registered complete in one call, a
        /// destroyed component, and a group flushed more than once with nothing between (two
        /// entries here: the measured groups had one flush each, so one entry per flush is not
        /// told apart from one per run of records).
        /// </summary>
        internal static void GetRecords(List<string> undoRecords, List<string> redoRecords)
        {
            if (undoRecords == null || redoRecords == null) throw new NullReferenceException("List must be non-null.");
            undoRecords.Clear();
            redoRecords.Clear();
            foreach (var group in s_Undo) AddEntries(group, undoRecords);
            for (int i = s_Redo.Count - 1; i >= 0; i--) AddEntries(s_Redo[i], redoRecords);
        }

        /// <summary>Both lists in one, and how many of its entries can be undone.</summary>
        internal static void GetRecords(List<string> undoRecords, out int undoCursor)
        {
            if (undoRecords == null) throw new NullReferenceException("List must be non-null.");
            undoRecords.Clear();
            foreach (var group in s_Undo) AddEntries(group, undoRecords);
            undoCursor = undoRecords.Count;
            for (int i = s_Redo.Count - 1; i >= 0; i--) AddEntries(s_Redo[i], undoRecords);
        }

        static void AddEntries(Group group, List<string> entries)
        {
            if (group.Name != null) entries.Add(group.Name);
            int batch = Operation.OwnEntries;
            foreach (var operation in group.Operations)
            {
                // A record that is not listed (unchanged, or not flushed yet) does not part the
                // records of one flush around it.
                if (operation.Batch == Operation.Unlisted) continue;
                if (operation.Batch == Operation.OwnEntries) operation.List(entries);
                else
                {
                    string name = string.IsNullOrEmpty(operation.Name) ? UnnamedRecord : operation.Name;
                    if (operation.Batch == batch) entries[^1] = name;
                    else entries.Add(name);
                }
                batch = operation.Batch;
            }
        }

        // ------------------------------------------------------------------ recording

        public static void RecordObject(Object objectToUndo, string name)
        {
            if (objectToUndo == null) return;
            Add(new StateOperation(objectToUndo, name));
        }

        /// <summary>A record listed at once, whether or not its object changes afterwards.</summary>
        static void RecordComplete(Object objectToUndo, string name, int batch)
        {
            if (objectToUndo == null) return;
            Add(new StateOperation(objectToUndo, name) { Batch = batch });
        }

        /// <summary>The record SerializedObject.ApplyModifiedProperties makes of its targets before
        /// it writes them: the properties it changes, whose values an undo restores and a redo sets
        /// again, leaving the others as they are then (tests/projects/serializedundo without.*).</summary>
        internal static void RecordApply(Object[] targets, SerializedNode[] befores, List<string> paths, string name)
        {
            var operation = new PropertiesOperation { Name = name, Targets = (Object[])targets.Clone(), Befores = befores, Paths = paths, Batch = Operation.Unlisted };
            Add(operation);
        }

        public static void RecordObjects(Object[] objectsToUndo, string name)
        {
            if (objectsToUndo == null) return;
            foreach (var o in objectsToUndo) RecordObject(o, name);
        }

        public static void RegisterCompleteObjectUndo(Object objectToUndo, string name) => RecordComplete(objectToUndo, name, ++s_Batch);

        public static void RegisterCompleteObjectUndo(Object[] objectsToUndo, string name)
        {
            if (objectsToUndo == null) return;
            int batch = ++s_Batch;
            foreach (var o in objectsToUndo) RecordComplete(o, name, batch);
        }

        public static void RegisterFullObjectHierarchyUndo(Object objectToUndo, string name)
        {
            var go = objectToUndo as GameObject ?? (objectToUndo as Component)?.gameObject;
            int batch = ++s_Batch;
            if (go == null)
            {
                RecordComplete(objectToUndo, name, batch);
                return;
            }
            foreach (var t in go.GetComponentsInChildren<Transform>(true))
            {
                RecordComplete(t.gameObject, name, batch);
                foreach (var c in t.GetComponents<Component>()) RecordComplete(c, name, batch);
            }
        }

        [global::System.@ObsoleteAttribute("Use Undo.RegisterFullObjectHierarchyUndo(Object, string) instead")] public static void RegisterFullObjectHierarchyUndo(Object objectToUndo) => RegisterFullObjectHierarchyUndo(objectToUndo, "Full Object Hierarchy");

        public static void RegisterCreatedObjectUndo(Object objectToUndo, string name)
        {
            if (objectToUndo == null) return;
            // Records made before it are listed before it (scenesave scene-api-rest
            // undo.kind.recordThenCreated, whose record had changed by then). Only their naming
            // and listing is brought forward: whether the Editor also flushes them here
            // (willFlushUndoRecord, dirtiness), and what it lists of a record whose object
            // changes only afterwards (nothing here), is not measured.
            PublishRecords();
            var operation = CreatedOperation.Of(objectToUndo, name);
            operation.NameRank = 2;
            operation.Registered = true;
            Add(operation);
        }

        public static Component AddComponent(GameObject gameObject, Type type)
        {
            if (gameObject == null) throw new ArgumentNullException(nameof(gameObject));
            var component = gameObject.AddComponent(type);
            if (component != null)
            {
                var operation = CreatedOperation.Of(component, "Add " + type.Name);
                operation.NameRank = 1;
                Add(operation);
            }
            return component;
        }

        public static T AddComponent<T>(GameObject gameObject) where T : Component => (T)AddComponent(gameObject, typeof(T));

        public static void DestroyObjectImmediate(Object objectToUndo)
        {
            if (objectToUndo == null) return;
            var go = objectToUndo as GameObject;
            Add(new DestroyedOperation
            {
                Description = $"{objectToUndo.GetType().Name} '{objectToUndo.name}'", Name = "Destroy Object", Target = objectToUndo,
                Id = ObjectChangeEvents.Id(objectToUndo), ParentId = go != null ? ObjectChangeEvents.ParentId(go.transform.parent) : 0,
                OwnerId = objectToUndo is Component component ? ObjectChangeEvents.Id(component.gameObject) : 0,
                Scene = ObjectChangeEvents.SceneOf(objectToUndo),
            });
            Object.DestroyImmediate(objectToUndo);
        }

        public static void SetTransformParent(Transform transform, Transform newParent, string name) =>
            SetTransformParent(transform, newParent, true, name);

        public static void SetTransformParent(Transform transform, Transform newParent, bool worldPositionStays, string name)
        {
            if (transform == null) throw new ArgumentNullException(nameof(transform));
            var operation = new ParentOperation { Target = transform, Before = transform.parent, After = newParent, Name = name, BeforeScene = transform.gameObject.scene };
            operation.State = new StateOperation(transform, name);
            transform.SetParent(newParent, worldPositionStays);
            Add(operation);
        }

        public static void MoveGameObjectToScene(GameObject go, Scene scene, string name)
        {
            if (go == null) throw new ArgumentNullException(nameof(go));
            SceneManager.MoveGameObjectToScene(go, scene);
        }

        /// <summary>A selection change (the Editor records them as undoable).</summary>
        internal static void RecordSelection(int[] before, int beforeActive, int[] after, int afterActive)
        {
            // Selection changes in one group make one operation, named by the latest.
            var group = Current(false);
            if (group != null && group.Operations.Count > 0 && group.Operations[^1] is SelectionOperation last)
            {
                last.After = after;
                last.AfterActive = afterActive;
                last.Name = after.Length > 1 ? "Select Multiple from Project" : "Selection Change";
                return;
            }
            Add(new SelectionOperation
            {
                Before = before, BeforeActive = beforeActive, After = after, AfterActive = afterActive,
                Name = after.Length > 1 ? "Select Multiple from Project" : "Selection Change",
            });
        }

        public delegate UndoPropertyModification[] PostprocessModifications(UndoPropertyModification[] modifications);

        /// <summary>Called, when Undo's records are flushed, with the property changes recorded
        /// edits (SerializedObject's ApplyModifiedProperties) made; what it returns changes neither
        /// the values nor the undo records.</summary>
        public static PostprocessModifications postprocessModifications;

        internal static UndoPropertyModification[] InvokePostprocessModifications(UndoPropertyModification[] modifications)
        {
            var callback = postprocessModifications;
            if (callback == null) return modifications;
            // Each handler takes what the one before returned.
            foreach (PostprocessModifications handler in callback.GetInvocationList()) modifications = handler(modifications);
            return modifications;
        }

        public static void FlushUndoRecordObjects()
        {
            willFlushUndoRecord?.Invoke();
            FlushDirtiness();
        }

        // ------------------------------------------------------------------ undo, redo

        public static void PerformUndo()
        {
            if (s_Undo.Count == 0) return;
            var group = s_Undo[^1];
            s_Undo.RemoveAt(s_Undo.Count - 1);
            group.Publish();
            Apply(group, redo: false);
            s_Redo.Add(group);
            s_Current++;
            Notify(group, false);
        }

        public static void PerformRedo()
        {
            if (s_Redo.Count == 0) return;
            var group = s_Redo[^1];
            s_Redo.RemoveAt(s_Redo.Count - 1);
            Apply(group, redo: true);
            s_Undo.Add(group);
            s_Current++;
            Notify(group, true);
        }

        static void Apply(Group group, bool redo)
        {
            s_Processing = true;
            try
            {
                if (redo)
                    foreach (var o in group.Operations)
                    {
                        Run(o.Redo);
                        o.NoteChanges(undone: false);
                    }
                else
                    for (int i = group.Operations.Count - 1; i >= 0; i--)
                    {
                        Run(group.Operations[i].Undo);
                        group.Operations[i].NoteChanges(undone: true);
                    }
            }
            finally
            {
                s_Processing = false;
            }
        }

        static void Run(Action action)
        {
            try { action(); }
            catch (Exception e) { Debug.LogException(e); }
        }

        static void Notify(Group group, bool redo)
        {
            var info = new UndoRedoInfo { undoName = group.DisplayName, undoGroup = group.Index, isRedo = redo };
            if (undoRedoPerformed != null)
                foreach (UndoRedoCallback callback in undoRedoPerformed.GetInvocationList()) Run(() => callback());
            if (undoRedoEvent != null)
                foreach (UndoRedoEventCallback callback in undoRedoEvent.GetInvocationList()) Run(() => callback(in info));
        }

        public static void CollapseUndoOperations(int groupIndex)
        {
            Group into = null;
            for (int i = 0; i < s_Undo.Count; i++)
            {
                if (s_Undo[i].Index < groupIndex) continue;
                if (into == null) { into = s_Undo[i]; continue; }
                // A group that takes later groups keeps its name (tests/projects/editor undo.f2).
                into.Name ??= into.PublishedName;
                into.Operations.AddRange(s_Undo[i].Operations);
                into.Name ??= s_Undo[i].Name;
                s_Undo.RemoveAt(i--);
            }
        }

        public static void RevertAllInCurrentGroup()
        {
            var group = Current(false);
            if (group == null) return;
            s_Undo.Remove(group);
            Apply(group, redo: false);
        }

        public static void RevertAllDownToGroup(int group)
        {
            while (s_Undo.Count > 0 && s_Undo[^1].Index >= group)
            {
                var top = s_Undo[^1];
                s_Undo.RemoveAt(s_Undo.Count - 1);
                Apply(top, redo: false);
            }
        }

        public static void ClearAll()
        {
            s_Undo.Clear();
            s_Redo.Clear();
            s_Unpublished.Clear();
        }

        public static void ClearUndo(Object identifier)
        {
            foreach (var group in s_Undo.Concat(s_Redo)) group.Operations.RemoveAll(o => o.Concerns(identifier));
        }

        /// <summary>An operation Unbound's own editing code undoes and redoes (a prefab apply).</summary>
        internal static void RegisterOperation(string name, Action undo, Action redo) =>
            Add(new CustomOperation { Name = name, UndoAction = undo, RedoAction = redo });

        // ------------------------------------------------------------------ operations

        /// <summary>Properties of objects, set back to their values before (undo) or after (redo).</summary>
        sealed class PropertiesOperation : Operation
        {
            public Object[] Targets;
            public SerializedNode[] Befores;
            public SerializedNode[] Afters;
            public List<string> Paths;

            public override void Undo()
            {
                Afters = new SerializedNode[Targets.Length];
                for (int i = 0; i < Targets.Length; i++)
                    if (Targets[i] != null && Befores[i] != null)
                    {
                        Afters[i] = SerializedObject.ReadRoot(Targets[i]);
                        Write(Targets[i], Befores[i]);
                    }
            }

            public override void Redo()
            {
                if (Afters == null) return;
                for (int i = 0; i < Targets.Length; i++)
                    if (Targets[i] != null && Afters[i] != null) Write(Targets[i], Afters[i]);
            }

            // The paths' values from a state, over the object's current data.
            void Write(Object target, SerializedNode state)
            {
                var current = SerializedObject.ReadRoot(target);
                foreach (var path in Paths)
                {
                    var from = SerializedTree.Find(state, path);
                    var to = SerializedTree.Find(current, path);
                    if (from != null && to != null) SerializedTree.CopyData(to, from);
                }
                SerializedObject.WriteRoot(target, current);
            }

            public override bool Concerns(Object o) => Array.IndexOf(Targets, o) >= 0;

            public override void NoteChanges(bool undone)
            {
                // The apply publishes its own changes; an undo or redo publishes the targets'.
                if (!s_Processing) return;
                foreach (var target in Targets)
                    if (target != null) ObjectChangeEvents.NoteProperties(target);
            }
        }

        sealed class CustomOperation : Operation
        {
            public Action UndoAction, RedoAction;
            public override void Undo() => UndoAction();
            public override void Redo() => RedoAction();
        }

        /// <summary>An object's state before an operation; undo restores it (after keeping the
        /// state it replaces for redo).</summary>
        sealed class StateOperation : Operation
        {
            readonly Object m_Target;
            internal Object Target => m_Target;
            readonly ObjectState m_Before;
            ObjectState m_After;

            internal StateOperation(Object target, string name)
            {
                m_Target = target;
                Name = name;
                m_Before = ObjectState.Capture(target);
                Batch = Unlisted;
            }

            protected override bool ChangedSinceRecorded => m_Target != null && !m_Before.SameAs(ObjectState.Capture(m_Target));

            public override void Undo()
            {
                if (m_Target == null) return;
                m_After = ObjectState.Capture(m_Target);
                m_Before.Restore(m_Target);
            }

            public override void Redo()
            {
                if (m_Target != null) m_After?.Restore(m_Target);
            }

            public override bool Concerns(Object o) => m_Target == o;

            internal void DirtyIfChanged()
            {
                if (m_Target != null && !m_Before.SameAs(ObjectState.Capture(m_Target))) EditorUtility.SetDirty(m_Target);
            }

            /// <summary>A recording publishes a change only when the object changed by the time the
            /// changes are published, an undo or redo only when the states it swaps differ (not
            /// measured: every measured recording changed its object).</summary>
            public override void NoteChanges(bool undone)
            {
                if (!s_Processing)
                    ObjectChangeEvents.NoteProperties(m_Target, () => m_Target != null && !m_Before.SameAs(ObjectState.Capture(m_Target)));
                else if (m_After == null || !m_Before.SameAs(m_After))
                    ObjectChangeEvents.NoteProperties(m_Target);
            }
        }

        sealed class CreatedOperation : Operation
        {
            public Object Target;
            /// <summary>The GameObject made, or the one the component was added to, its ID and scene
            /// (an undo destroys the object).</summary>
            GameObject m_Owner;
            int m_OwnerId;
            Scene m_Scene;
            /// <summary>Made by RegisterCreatedObjectUndo (not an added component).</summary>
            public bool Registered;

            /// <summary>A registered object is two entries of its name; unnamed, these two.</summary>
            public override void List(List<string> entries)
            {
                if (!Registered) base.List(entries);
                else if (string.IsNullOrEmpty(Name)) { entries.Add("Create Object"); entries.Add("Update Object"); }
                else { entries.Add(Name); entries.Add(Name); }
            }

            internal static CreatedOperation Of(Object target, string name)
            {
                var owner = target as GameObject ?? (target as Component)?.gameObject;
                return new CreatedOperation { Target = target, Name = name, m_Owner = owner, m_OwnerId = ObjectChangeEvents.Id(owner), m_Scene = ObjectChangeEvents.SceneOf(owner) };
            }

            public override void Undo()
            {
                if (Target != null) Object.DestroyImmediate(Target);
            }

            public override void Redo() =>
                Debug.LogError($"Unbound: redoing the creation of an object undo destroyed is not supported ({Name}).");

            public override bool Concerns(Object o) => Target == o;

            public override void NoteChanges(bool undone)
            {
                if (!ObjectChangeEvents.Listening) return;
                if (m_Owner is null)
                    Stubs.NotImplemented("ObjectChangeEvents for a created object that is neither a GameObject nor a component");
                else if (Target is Component)
                    ObjectChangeEvents.Note(ObjectChangeKind.ChangeGameObjectStructure, new ChangeGameObjectStructureEventArgs(m_OwnerId, m_Scene));
                else if (undone)
                    ObjectChangeEvents.Note(ObjectChangeKind.DestroyGameObjectHierarchy, new DestroyGameObjectHierarchyEventArgs(m_OwnerId, 0, m_Scene));
                else
                    ObjectChangeEvents.Note(ObjectChangeKind.CreateGameObjectHierarchy, new CreateGameObjectHierarchyEventArgs(m_OwnerId, m_Scene));
            }
        }

        sealed class DestroyedOperation : Operation
        {
            public string Description;
            public Object Target;
            public int Id, ParentId, OwnerId;
            public Scene Scene;

            /// <summary>Listed after a selection clear (measured for a GameObject that was not selected).</summary>
            public override void List(List<string> entries)
            {
                entries.Add("Clear Selection");
                base.List(entries);
            }

            /// <summary>A destroyed component changes its GameObject's structure (measured:
            /// tests/projects/scenesave prefab-writes removeRevert.event).</summary>
            public override void NoteChanges(bool undone)
            {
                if (!ObjectChangeEvents.Listening || undone) return;
                if (Target is GameObject)
                    ObjectChangeEvents.Note(ObjectChangeKind.DestroyGameObjectHierarchy, new DestroyGameObjectHierarchyEventArgs(Id, ParentId, Scene));
                else if (OwnerId != 0)
                    ObjectChangeEvents.Note(ObjectChangeKind.ChangeGameObjectStructure, new ChangeGameObjectStructureEventArgs(OwnerId, Scene));
                else
                    Stubs.NotImplemented("ObjectChangeEvents for a destroyed object that is neither a GameObject nor a component");
            }

            public override void Undo() =>
                Debug.LogError($"Unbound: undoing the destruction of {Description} is not supported (the object is not recreated).");

            public override void Redo() { }
        }

        sealed class ParentOperation : Operation
        {
            public Transform Target, Before, After;
            public StateOperation State;
            public Scene BeforeScene;

            public override void NoteChanges(bool undone)
            {
                if (!ObjectChangeEvents.Listening || Target == null) return;
                var (from, to) = undone ? (After, Before) : (Before, After);
                var scene = Target.gameObject.scene;
                ObjectChangeEvents.Note(ObjectChangeKind.ChangeGameObjectParent, new ChangeGameObjectParentEventArgs(
                    ObjectChangeEvents.Id(Target.gameObject), undone ? scene : BeforeScene, ObjectChangeEvents.ParentId(from), scene, ObjectChangeEvents.ParentId(to)));
            }

            public override void Undo()
            {
                if (Target == null) return;
                Target.SetParent(Before, false);
                State.Undo();
            }

            public override void Redo()
            {
                if (Target == null) return;
                Target.SetParent(After, false);
                State.Redo();
            }

            public override bool Concerns(Object o) => Target == o;
        }

        sealed class SelectionOperation : Operation
        {
            public int[] Before, After;
            public int BeforeActive, AfterActive;

            public override void Undo() => Selection.Restore(Before, BeforeActive);
            public override void Redo() => Selection.Restore(After, AfterActive);
        }
    }

    public partial struct UndoRedoInfo
    {
        public string undoName;
        public int undoGroup;
        int m_IsRedo;

        public bool isRedo
        {
            get => m_IsRedo != 0;
            set => m_IsRedo = value ? 1 : 0;
        }
    }
}

namespace Unbound.Runtime
{
    /// <summary>An object's serialized state: its script fields (as JsonUtility writes them,
    /// references by instance) and the native properties Unbound models.</summary>
    internal sealed class ObjectState
    {
        string m_Json;
        readonly List<(SerializedTree.NativeField Field, object Value)> m_Native = new List<(SerializedTree.NativeField, object)>();

        internal static ObjectState Capture(Object target)
        {
            var state = new ObjectState();
            foreach (var field in SerializedTree.NativeFields(target))
            {
                if (field.ReadOnly || field.Set == null) continue;
                try { state.m_Native.Add((field, SerializedTree.CopyLeaf(field.Get(target), field.Shape))); }
                catch (Exception) { }
            }
            if (SerializedTree.IsScript(target)) state.m_Json = JsonUtility.ToJson(target);
            return state;
        }

        /// <summary>Whether two captures of an object hold the same state.</summary>
        internal bool SameAs(ObjectState other)
        {
            if (other == null || m_Json != other.m_Json || m_Native.Count != other.m_Native.Count) return false;
            for (int i = 0; i < m_Native.Count; i++)
            {
                object a = m_Native[i].Value, b = other.m_Native[i].Value;
                if (Equals(a, b)) continue;
                if (a is System.Collections.IEnumerable x && b is System.Collections.IEnumerable y && !(a is string)
                    && System.Linq.Enumerable.SequenceEqual(System.Linq.Enumerable.Cast<object>(x), System.Linq.Enumerable.Cast<object>(y))) continue;
                return false;
            }
            return true;
        }

        internal void Restore(Object target)
        {
            foreach (var (field, value) in m_Native)
            {
                try { field.Set(target, SerializedTree.CopyLeaf(value, field.Shape)); }
                catch (Exception e) { Debug.LogException(e); }
            }
            if (m_Json != null) JsonUtility.FromJsonOverwrite(m_Json, target);
        }
    }
}
