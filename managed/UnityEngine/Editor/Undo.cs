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
    /// group is named by <see cref="SetCurrentGroupName"/> else by its first operation, undo and
    /// redo apply a whole group and each advances the current group by one, and new operations
    /// drop what could be redone. Object state is the serialized state Unbound models (script fields,
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
            public string DisplayName => Name ?? (Operations.Count > 0 ? Operations[0].Name : "");
        }

        abstract class Operation
        {
            public string Name;
            public abstract void Undo();
            public abstract void Redo();
            /// <summary>Notes the changes the operation made (done or redone), or its undo made.</summary>
            public virtual void NoteChanges(bool undone) { }
            public virtual bool Concerns(Object o) => false;
        }

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
            if (operation is StateOperation state) s_Unflushed.Add(state);
        }

        /// <summary>Recordings whose object may have changed since: a flush marks the changed ones
        /// dirty (EditorUtility.SetDirty), which dirties their scene (measured:
        /// tests/projects/scenesave second.undo).</summary>
        static readonly List<StateOperation> s_Unflushed = new List<StateOperation>();

        /// <summary>Marks the objects recorded since the last flush dirty when they changed.</summary>
        internal static void FlushDirtiness()
        {
            foreach (var state in s_Unflushed) state.DirtyIfChanged();
            s_Unflushed.Clear();
        }

        public static int GetCurrentGroup() => s_Current;

        public static string GetCurrentGroupName() => Current(false)?.DisplayName ?? "";

        public static void SetCurrentGroupName(string name) => Current(true).Name = name;

        public static void IncrementCurrentGroup() => s_Current++;

        // ------------------------------------------------------------------ recording

        public static void RecordObject(Object objectToUndo, string name)
        {
            if (objectToUndo == null) return;
            Add(new StateOperation(objectToUndo, name));
        }

        public static void RecordObjects(Object[] objectsToUndo, string name)
        {
            if (objectsToUndo == null) return;
            foreach (var o in objectsToUndo) RecordObject(o, name);
        }

        public static void RegisterCompleteObjectUndo(Object objectToUndo, string name) => RecordObject(objectToUndo, name);

        public static void RegisterCompleteObjectUndo(Object[] objectsToUndo, string name) => RecordObjects(objectsToUndo, name);

        public static void RegisterFullObjectHierarchyUndo(Object objectToUndo, string name)
        {
            var go = objectToUndo as GameObject ?? (objectToUndo as Component)?.gameObject;
            if (go == null)
            {
                RecordObject(objectToUndo, name);
                return;
            }
            foreach (var t in go.GetComponentsInChildren<Transform>(true))
            {
                RecordObject(t.gameObject, name);
                foreach (var c in t.GetComponents<Component>()) RecordObject(c, name);
            }
        }

        [global::System.@ObsoleteAttribute("Use Undo.RegisterFullObjectHierarchyUndo(Object, string) instead")] public static void RegisterFullObjectHierarchyUndo(Object objectToUndo) => RegisterFullObjectHierarchyUndo(objectToUndo, "Full Object Hierarchy");

        public static void RegisterCreatedObjectUndo(Object objectToUndo, string name)
        {
            if (objectToUndo == null) return;
            Add(CreatedOperation.Of(objectToUndo, name));
        }

        public static Component AddComponent(GameObject gameObject, Type type)
        {
            if (gameObject == null) throw new ArgumentNullException(nameof(gameObject));
            var component = gameObject.AddComponent(type);
            if (component != null) Add(CreatedOperation.Of(component, "Add " + type.Name));
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

        /// <summary>Called with the property changes a recorded edit (SerializedObject's
        /// ApplyModifiedProperties) makes; what it returns is what Undo records.</summary>
        public static PostprocessModifications postprocessModifications;

        internal static UndoPropertyModification[] InvokePostprocessModifications(UndoPropertyModification[] modifications)
        {
            var callback = postprocessModifications;
            if (callback == null) return modifications;
            foreach (PostprocessModifications handler in callback.GetInvocationList())
            {
                try { modifications = handler(modifications) ?? Array.Empty<UndoPropertyModification>(); }
                catch (Exception e) { Debug.LogException(e); }
            }
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
        }

        public static void ClearUndo(Object identifier)
        {
            foreach (var group in s_Undo.Concat(s_Redo)) group.Operations.RemoveAll(o => o.Concerns(identifier));
        }

        // ------------------------------------------------------------------ operations

        /// <summary>An object's state before an operation; undo restores it (after keeping the
        /// state it replaces for redo).</summary>
        sealed class StateOperation : Operation
        {
            readonly Object m_Target;
            readonly ObjectState m_Before;
            ObjectState m_After;

            internal StateOperation(Object target, string name)
            {
                m_Target = target;
                Name = name;
                m_Before = ObjectState.Capture(target);
            }

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
            public int Id, ParentId;
            public Scene Scene;

            public override void NoteChanges(bool undone)
            {
                if (!ObjectChangeEvents.Listening || undone) return;
                if (Target is GameObject)
                    ObjectChangeEvents.Note(ObjectChangeKind.DestroyGameObjectHierarchy, new DestroyGameObjectHierarchyEventArgs(Id, ParentId, Scene));
                else
                    Stubs.NotImplemented("ObjectChangeEvents for a destroyed object that is not a GameObject");
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
