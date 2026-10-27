using System;
using System.Collections.Generic;
using System.Linq;
using Unbound.Runtime;
using UnityEngine;
using Object = UnityEngine.Object;

namespace UnityEditor
{
    /// <summary>
    /// The Editor's selection. Nothing is selected when Play mode starts; editor code may select
    /// objects, and <see cref="selectionChanged"/> fires on the Editor's next update, as the native
    /// selection notifies.
    /// </summary>
    public sealed partial class Selection
    {
        static int[] s_Ids = Array.Empty<int>();
        static int s_Active;
        static int s_Context;
        static bool s_Changed;

        public static Action selectionChanged;

        static Selection() => EditorApplication.InternalUpdate += NotifyIfChanged;

        static void NotifyIfChanged()
        {
            if (!s_Changed) return;
            s_Changed = false;
            selectionChanged?.Invoke();
        }

        static void SetSelection(int[] ids, int active, int context)
        {
            var clean = ids.Where(id => id != 0).Distinct().ToArray();
            if (active != 0 && Array.IndexOf(clean, active) < 0) active = clean.Length > 0 ? clean[0] : 0;
            if (active == 0 && clean.Length > 0) active = clean[0];
            if (s_Active == active && s_Context == context && s_Ids.SequenceEqual(clean)) return;
            if (!Undo.isProcessing) Undo.RecordSelection(s_Ids, s_Active, clean, active);
            s_Ids = clean;
            s_Active = active;
            s_Context = context;
            s_Changed = true;
        }

        /// <summary>Undo and redo put a recorded selection back.</summary>
        internal static void Restore(int[] ids, int active) => SetSelection(ids, active, 0);

        static Object Find(int id) => id == 0 ? null : ObjectRegistry.FindById(id);

        static IEnumerable<Object> Selected() => s_Ids.Select(Find).Where(o => o != null);

        public static Object activeObject
        {
            get => Find(s_Active);
            set => SetActiveObjectWithContext(value, null);
        }

        public static GameObject activeGameObject
        {
            get => activeObject switch
            {
                GameObject go => go,
                Component c => c.gameObject,
                _ => null,
            };
            set => activeObject = value;
        }

        public static Transform activeTransform
        {
            get
            {
                var go = activeObject as GameObject;
                return go != null && !go.IsPersistentAsset ? go.transform : null;
            }
            set => activeObject = value != null ? value.gameObject : null;
        }

        public static Object activeContext => Find(s_Context);

        public static int activeInstanceID
        {
            get => Find(s_Active) != null ? s_Active : 0;
            set => SetSelection(value == 0 ? Array.Empty<int>() : new[] { value }, value, 0);
        }

        public static Object[] objects
        {
            get => Selected().ToArray();
            set
            {
                var ids = (value ?? Array.Empty<Object>()).Where(o => o != null).Select(o => o.GetInstanceID()).ToArray();
                SetSelection(ids, ids.Length > 0 ? ids[0] : 0, s_Context);
            }
        }

        public static int[] instanceIDs
        {
            get => Selected().Select(o => o.GetInstanceID()).ToArray();
            set
            {
                var ids = value ?? Array.Empty<int>();
                SetSelection(ids, ids.Length > 0 ? ids[0] : 0, s_Context);
            }
        }

        public static GameObject[] gameObjects => Selected().OfType<GameObject>().ToArray();

        public static Transform[] transforms => GetTransforms(SelectionMode.Unfiltered);

        public static string[] assetGUIDs =>
            Selected().Select(o => AssetDatabaseRuntime.TryGetLocation(o, out var guid, out _) ? guid : null).Where(g => g != null).Distinct().ToArray();

        public static int count => Selected().Count();

        public static bool Contains(int instanceID) => Array.IndexOf(s_Ids, instanceID) >= 0 && Find(instanceID) != null;

        public static bool Contains(Object obj) => Contains(obj.GetInstanceID());

        public static void SetActiveObjectWithContext(Object obj, Object context)
        {
            int id = obj != null ? obj.GetInstanceID() : 0;
            SetSelection(id == 0 ? Array.Empty<int>() : new[] { id }, id, context != null ? context.GetInstanceID() : 0);
        }

        /// <summary>The selected scene objects' transforms (assets are not in the scene).</summary>
        public static Transform[] GetTransforms(SelectionMode mode)
        {
            var result = new List<Transform>();
            foreach (var go in Selected().OfType<GameObject>())
            {
                if (go.IsPersistentAsset) continue;
                if ((mode & SelectionMode.Deep) != 0) result.AddRange(go.GetComponentsInChildren<Transform>(true));
                else result.Add(go.transform);
            }
            if ((mode & SelectionMode.TopLevel) != 0)
                result = result.Where(t => !result.Any(o => o != t && t.IsChildOf(o))).ToList();
            return result.Distinct().ToArray();
        }

        static IEnumerable<Object> Filtered(Type type, SelectionMode mode)
        {
            if (typeof(Component).IsAssignableFrom(type) || type.IsInterface)
            {
                foreach (var t in GetTransforms(mode))
                    if (t.TryGetComponent(type, out var component)) yield return component;
                yield break;
            }
            if (typeof(GameObject).IsAssignableFrom(type))
            {
                foreach (var t in GetTransforms(mode)) yield return t.gameObject;
                yield break;
            }
            foreach (var o in Selected())
                if (type.IsInstanceOfType(o)) yield return o;
        }

        public static T[] GetFiltered<T>(SelectionMode mode) => Filtered(typeof(T), mode).Cast<T>().ToArray();

        public static Object[] GetFiltered(Type type, SelectionMode mode) => Filtered(type, mode).ToArray();
    }

    /// <summary>Domain reloads: the hot reload's fallback raises these around the reload.</summary>
    public static partial class AssemblyReloadEvents
    {
        public delegate void AssemblyReloadCallback();

        public static event AssemblyReloadCallback beforeAssemblyReload;
        public static event AssemblyReloadCallback afterAssemblyReload;

        internal static void RaiseBefore() => Raise(beforeAssemblyReload);

        internal static void RaiseAfter() => Raise(afterAssemblyReload);

        static void Raise(AssemblyReloadCallback callbacks)
        {
            if (callbacks == null) return;
            foreach (AssemblyReloadCallback callback in callbacks.GetInvocationList())
            {
                try { callback(); }
                catch (Exception e) { Debug.LogException(e); }
            }
        }
    }

    /// <summary>
    /// Background tasks shown in the Editor's progress window: kept as the native table keeps
    /// them (ids from 1, status, progress, steps, description), without the window.
    /// </summary>
    public static partial class Progress
    {
        sealed class Task
        {
            public int Id;
            public string Name;
            public string Description;
            public Options Options;
            public int ParentId = -1;
            public Status Status = Status.Running;
            public float Progress;
            public int CurrentStep = -1;
            public int TotalSteps = -1;
            public string StepLabel;
            public long Start;
            public long End;
            public long Updated;
            public long RemainingTime = -1;
            public int Priority = (int)UnityEditor.Progress.Priority.Normal;
            public TimeDisplayMode TimeDisplayMode = TimeDisplayMode.ShowRunningTime;
            public Func<bool> CancelCallback;
            public Func<bool, bool> PauseCallback;
        }

        static readonly List<Task> s_Tasks = new List<Task>();
        static readonly List<Action> s_Pending = new List<Action>();
        static int s_NextId = 1;

        static Progress() => EditorApplication.InternalUpdate += ApplyPending;

        /// <summary>Changes reach the native table on the Editor's next update (measured: a report
        /// reads back unchanged in the same frame); finished tasks that are not sticky leave it then.</summary>
        static void Later(int id, Action<Task> change)
        {
            lock (s_Tasks) s_Pending.Add(() => { var task = s_Tasks.Find(t => t.Id == id); if (task != null) change(task); });
        }

        static void ApplyPending()
        {
            Action[] pending;
            lock (s_Tasks)
            {
                if (s_Pending.Count == 0) return;
                pending = s_Pending.ToArray();
                s_Pending.Clear();
            }
            foreach (var change in pending) change();
            lock (s_Tasks) s_Tasks.RemoveAll(t => t.Status != Status.Running && t.Status != Status.Paused && (t.Options & Options.Sticky) == 0);
        }

        static long Now => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        static Task Get(int id)
        {
            lock (s_Tasks) return s_Tasks.Find(t => t.Id == id);
        }

        public static int Start(string name, string description = null, Options options = Options.None, int parentId = -1)
        {
            if ((options & Options.Unmanaged) == 0) options |= Options.Managed;
            lock (s_Tasks)
            {
                var task = new Task { Id = s_NextId++, Name = name ?? "", Description = description, Options = options, ParentId = parentId, Start = Now };
                task.Updated = task.Start;
                if ((options & Options.Indefinite) != 0) task.Progress = -1f;
                s_Tasks.Add(task);
                return task.Id;
            }
        }

        public static void Finish(int id, Status status = Status.Succeeded) => Later(id, task =>
        {
            task.Status = status;
            task.End = task.Updated = Now;
            if (status == Status.Succeeded) task.Progress = 1f;
        });

        public static int Remove(int id) => Remove(id, false);

        public static int Remove(int id, bool forceSynchronous)
        {
            lock (s_Tasks)
            {
                int removed = s_Tasks.RemoveAll(t => t.Id == id || t.ParentId == id);
                return removed > 0 ? id : -1;
            }
        }

        public static void Report(int id, float progress) => Report(id, progress, null);

        public static void Report(int id, int currentStep, int totalSteps) => Report(id, currentStep, totalSteps, null);

        public static void Report(int id, float progress, string description) => Later(id, task =>
        {
            task.Progress = progress;
            if (description != null) task.Description = description;
            task.Updated = Now;
        });

        public static void Report(int id, int currentStep, int totalSteps, string description) => Later(id, task =>
        {
            task.CurrentStep = currentStep;
            task.TotalSteps = totalSteps;
            task.Progress = totalSteps > 0 ? (float)currentStep / totalSteps : 0f;
            if (description != null) task.Description = description;
            task.Updated = Now;
        });

        public static void SetDescription(int id, string description) => Later(id, task => task.Description = description);

        public static void SetStepLabel(int id, string label)
        {
            var task = Get(id);
            if (task != null) task.StepLabel = label;
        }

        public static bool Cancel(int id)
        {
            var task = Get(id);
            if (task == null) return false;
            if (task.CancelCallback != null && !task.CancelCallback()) return false;
            task.Status = Status.Canceled;
            task.End = Now;
            return true;
        }

        public static void RegisterCancelCallback(int id, Func<bool> callback)
        {
            var task = Get(id);
            if (task != null) task.CancelCallback = callback;
        }

        public static void UnregisterCancelCallback(int id)
        {
            var task = Get(id);
            if (task != null) task.CancelCallback = null;
        }

        public static bool Pause(int id)
        {
            var task = Get(id);
            if (task == null || task.Status != Status.Running) return false;
            if (task.PauseCallback != null && !task.PauseCallback(true)) return false;
            task.Status = Status.Paused;
            return true;
        }

        public static bool Resume(int id)
        {
            var task = Get(id);
            if (task == null || task.Status != Status.Paused) return false;
            if (task.PauseCallback != null && !task.PauseCallback(false)) return false;
            task.Status = Status.Running;
            return true;
        }

        public static void RegisterPauseCallback(int id, Func<bool, bool> callback)
        {
            var task = Get(id);
            if (task != null) task.PauseCallback = callback;
        }

        public static void UnregisterPauseCallback(int id)
        {
            var task = Get(id);
            if (task != null) task.PauseCallback = null;
        }

        public static int GetCount()
        {
            lock (s_Tasks) return s_Tasks.Count;
        }

        public static int GetId(int index)
        {
            lock (s_Tasks) return index >= 0 && index < s_Tasks.Count ? s_Tasks[index].Id : -1;
        }

        public static bool Exists(int id) => Get(id) != null;

        public static float GetProgress(int id) => Get(id)?.Progress ?? 0f;
        public static int GetCurrentStep(int id) => Get(id)?.CurrentStep ?? -1;
        public static int GetTotalSteps(int id) => Get(id)?.TotalSteps ?? -1;
        public static string GetName(int id) => Get(id)?.Name;
        public static string GetDescription(int id) => Get(id)?.Description;
        public static string GetStepLabel(int id) => Get(id)?.StepLabel;
        public static long GetStartDateTime(int id) => Get(id)?.Start ?? 0;
        public static long GetEndDateTime(int id) => Get(id)?.End ?? 0;
        public static long GetUpdateDateTime(int id) => Get(id)?.Updated ?? 0;
        public static int GetParentId(int id) => Get(id)?.ParentId ?? -1;
        public static bool IsCancellable(int id) => Get(id)?.CancelCallback != null;
        public static bool IsPausable(int id) => Get(id)?.PauseCallback != null;
        public static Status GetStatus(int id) => Get(id)?.Status ?? Status.Running;
        public static Options GetOptions(int id) => Get(id)?.Options ?? Options.None;
        public static long GetRemainingTime(int id) => Get(id)?.RemainingTime ?? -1;
        public static int GetPriority(int id) => Get(id)?.Priority ?? 0;
        public static TimeDisplayMode GetTimeDisplayMode(int id) => Get(id)?.TimeDisplayMode ?? TimeDisplayMode.NoTimeShown;

        public static void SetTimeDisplayMode(int id, TimeDisplayMode displayMode)
        {
            var task = Get(id);
            if (task != null) task.TimeDisplayMode = displayMode;
        }

        public static void SetRemainingTime(int id, long seconds)
        {
            var task = Get(id);
            if (task != null) task.RemainingTime = seconds;
        }

        public static void ClearRemainingTime(int id) => SetRemainingTime(id, -1);

        public static void SetPriority(int id, int priority)
        {
            var task = Get(id);
            if (task != null) task.Priority = priority;
        }

        public static void SetPriority(int id, Priority priority) => SetPriority(id, (int)priority);

        public static int GetRunningProgressCount()
        {
            lock (s_Tasks) return s_Tasks.Count(t => t.Status == Status.Running);
        }

        public static bool running => GetRunningProgressCount() > 0;

        public static void ShowDetails(bool shouldReposition = true) { }
    }

    public partial class EditorUtility
    {
        static readonly Dictionary<int, int> s_DirtyCounts = new Dictionary<int, int>();

        /// <summary>Marks an object changed (the Editor saves dirty assets and scenes; Unbound
        /// writes nothing back, so dirtiness is only counted).</summary>
        public static void SetDirty(Object target)
        {
            if (target is null) throw new ArgumentNullException(nameof(target));
            if (target == null) return;
            int id = target.GetInstanceID();
            s_DirtyCounts[id] = (s_DirtyCounts.TryGetValue(id, out var n) ? n : 0) + 1;
            s_Dirty.Add(id);
        }

        static readonly HashSet<int> s_Dirty = new HashSet<int>();

        public static bool IsDirty(Object target) => target != null && IsDirty(target.GetInstanceID());

        public static bool IsDirty(int instanceID) => s_Dirty.Contains(instanceID);

        public static void ClearDirty(Object target)
        {
            if (target is null) throw new ArgumentNullException(nameof(target));
            if (target == null) return;
            s_Dirty.Remove(target.GetInstanceID());
            s_DirtyCounts.Remove(target.GetInstanceID());
        }

        public static int GetDirtyCount(int instanceID) => s_DirtyCounts.TryGetValue(instanceID, out var n) ? n : 0;

        public static int GetDirtyCount(Object target) => target != null ? GetDirtyCount(target.GetInstanceID()) : 0;

        /// <summary>Script compilation succeeded: the game would not run otherwise.</summary>
        public static bool scriptCompilationFailed => false;

        public static bool audioMasterMute { get; set; }

        /// <summary>A dialog nobody can answer: its cancel button (1), as the batch-mode Editor
        /// answers (measured).</summary>
        public static int DisplayDialogComplex(string title, string message, string ok, string cancel, string alt) => 1;

        public static int GetObjectEnabled(Object target) => target switch
        {
            Behaviour b => b.enabled ? 1 : 0,
            Renderer r => r.enabled ? 1 : 0,
            Collider c => c.enabled ? 1 : 0,
            _ => -1,
        };

        public static void SetObjectEnabled(Object target, bool enabled)
        {
            switch (target)
            {
                case Behaviour b: b.enabled = enabled; break;
                case Renderer r: r.enabled = enabled; break;
                case Collider c: c.enabled = enabled; break;
            }
        }

        /// <summary>Bytes as the Editor prints them (measured): "511 B", "0.5 KB", "1.5 MB", "0.50 GB",
        /// a unit from half of it on.</summary>
        public static string FormatBytes(long bytes)
        {
            if (bytes < 0) return "Unknown";
            if (bytes < 512) return bytes + " B";
            if (bytes < 524288) return (bytes / 1024.0).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " KB";
            if (bytes < 536870912) return (bytes / 1048576.0).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " MB";
            return (bytes / 1073741824.0).ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) + " GB";
        }

        public static string FormatBytes(int bytes) => FormatBytes((long)bytes);

        public static GameObject CreateGameObjectWithHideFlags(string name, HideFlags flags, params Type[] components)
        {
            var go = new GameObject(name, components);
            go.hideFlags = flags;
            return go;
        }

        public static void CopySerializedIfDifferent(Object source, Object dest) => CopySerialized(source, dest);

        public static void CopySerializedManagedFieldsOnly(object source, object dest)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (dest == null) throw new ArgumentNullException(nameof(dest));
            JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(source), dest);
        }

        public static void RequestScriptReload() { }

        public static bool IsRunningUnderCPUEmulation() => false;

        public static void SetDefaultParentObject(GameObject defaultParentObject) { }

        public static void ClearDefaultParentObject() { }
    }
}
