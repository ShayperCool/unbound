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
        static readonly HashSet<int> s_SelectedIds = new HashSet<int>();
        static int s_Active;
        static int s_Context;
        static bool s_Changed;

        public static Action selectionChanged;

        static Selection()
        {
            EditorApplication.InternalUpdate += NotifyIfChanged;
            Object.EngineObjectUnloadedOrDestroyed += OnObjectUnavailable;
        }

        static void NotifyIfChanged()
        {
            if (!s_Changed) return;
            s_Changed = false;
            selectionChanged?.Invoke();
        }

        // Keep removals constant-time during destruction. Rebuild the ordered view once when
        // a reader or the next selection edit needs it.
        static int[] CurrentIds()
        {
            if (s_Ids.Length != s_SelectedIds.Count)
                s_Ids = s_Ids.Where(s_SelectedIds.Contains).ToArray();
            return s_Ids;
        }

        static void SetSelection(int[] ids, int active, int context)
        {
            var measured = Compat.SelectionMutationsMeasured;
            var clean = (measured ? ids : ids.Where(id => id != 0)).Distinct().ToArray();
            if (!measured && (active == 0 || Array.IndexOf(clean, active) < 0))
                active = clean.Length > 0 ? clean[0] : 0;
            // The active asset is resolved on assignment. Other IDs can stay unloaded.
            if (measured) _ = Find(active);
            var previous = CurrentIds();
            if (s_Active == active && s_Context == context && previous.SequenceEqual(clean)) return;
            if (!Undo.isProcessing) Undo.RecordSelection(previous, s_Active, clean, active);
            s_Ids = clean;
            s_SelectedIds.Clear();
            s_SelectedIds.UnionWith(clean);
            s_Active = active;
            s_Context = context;
            s_Changed = true;
        }

        static void OnObjectUnavailable(int instanceID, bool destroyed)
        {
            var wasActive = s_Active == instanceID;
            var lostContext = destroyed && s_Context == instanceID;
            if (!wasActive && !lostContext && !s_SelectedIds.Contains(instanceID)) return;
            if (!Compat.SelectionMutationsMeasured) return;
            var wasSelected = s_SelectedIds.Remove(instanceID);
            if (wasActive) s_Active = 0;
            if (lostContext) s_Context = 0;
            if (wasActive || wasSelected) s_Changed = true;
        }

        static void SetActiveSelection(int id, int context)
        {
            var selectedId = id;
            if (Compat.SelectionMutationsMeasured && Find(id) is Component component && component.gameObject != null)
                selectedId = component.gameObject.GetInstanceID();
            SetSelection(id == 0 ? Array.Empty<int>() : new[] { selectedId }, id, context);
        }

        /// <summary>Undo and redo put a recorded selection back.</summary>
        internal static void Restore(int[] ids, int active) => SetSelection(ids, active, 0);

        static Object Find(int id)
        {
            if (id == 0) return null;
            return Compat.SelectionMutationsMeasured ? AssetDatabaseRuntime.FindOrReloadById(id) : ObjectRegistry.FindById(id);
        }

        static IEnumerable<Object> Selected() => CurrentIds().Select(Find).Where(o => o != null);

        public static Object activeObject
        {
            get => Find(s_Active);
            set
            {
                if (Compat.SelectionMutationsMeasured) activeInstanceID = value != null ? value.GetInstanceID() : 0;
                else SetActiveObjectWithContext(value, null);
            }
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
                var go = Compat.SelectionMutationsMeasured ? activeGameObject : activeObject as GameObject;
                return go != null && !go.IsPersistentAsset ? go.transform : null;
            }
            set => activeObject = Compat.SelectionMutationsMeasured ? value : value != null ? value.gameObject : null;
        }

        public static Object activeContext => Find(s_Context);

        public static int activeInstanceID
        {
            get => Compat.SelectionMutationsMeasured || Find(s_Active) != null ? s_Active : 0;
            set
            {
                if (Compat.SelectionMutationsMeasured && value == s_Active)
                {
                    if (s_Context != 0) SetSelection(CurrentIds(), value, 0);
                    return;
                }
                SetActiveSelection(value, 0);
            }
        }

        public static Object[] objects
        {
            get => Selected().ToArray();
            set
            {
                var ids = (value ?? Array.Empty<Object>()).Where(o => o != null).Select(o => o.GetInstanceID()).ToArray();
                SetSelection(ids, ids.Length > 0 ? ids[0] : 0, Compat.SelectionMutationsMeasured ? 0 : s_Context);
            }
        }

        public static int[] instanceIDs
        {
            get => Compat.SelectionMutationsMeasured ? (int[])CurrentIds().Clone()
                : Selected().Select(o => o.GetInstanceID()).ToArray();
            set
            {
                var measured = Compat.SelectionMutationsMeasured;
                if (measured && value == null) throw new ArgumentNullException(nameof(instanceIDs));
                var ids = value ?? Array.Empty<int>();
                SetSelection(ids, ids.Length > 0 ? ids[0] : 0, measured ? 0 : s_Context);
            }
        }

        public static GameObject[] gameObjects => Selected().OfType<GameObject>().ToArray();

        public static Transform[] transforms => GetTransforms(SelectionMode.Unfiltered);

        public static string[] assetGUIDs => CurrentIds().Select(AssignedAssetGuid).Where(guid => guid != null).Distinct().ToArray();

        static string AssignedAssetGuid(int id)
        {
            if (Compat.SelectionMutationsMeasured && AssetDatabaseRuntime.TryGetPersistentLocation(id, out var guid, out _))
                return guid;
            var obj = ObjectRegistry.FindById(id);
            return obj != null && AssetDatabaseRuntime.TryGetLocation(obj, out guid, out _) ? guid : null;
        }

        public static int count => Selected().Count();

        [global::Unbound.ImplementedIn(Compat.SelectionMutationsFamily)]
        internal static void Add(int instanceID)
        {
            if (CanMutate(nameof(Add))) AddInstance(instanceID);
        }

        [global::Unbound.ImplementedIn(Compat.SelectionMutationsFamily)]
        internal static void Add(Object obj)
        {
            if (CanMutate(nameof(Add)) && obj != null) AddInstance(obj.GetInstanceID());
        }

        static void AddInstance(int instanceID)
        {
            if (!s_SelectedIds.Contains(instanceID)) instanceIDs = CurrentIds().Append(instanceID).ToArray();
        }

        [global::Unbound.ImplementedIn(Compat.SelectionMutationsFamily)]
        internal static void Remove(int instanceID)
        {
            if (CanMutate(nameof(Remove))) RemoveInstance(instanceID);
        }

        [global::Unbound.ImplementedIn(Compat.SelectionMutationsFamily)]
        internal static void Remove(Object obj)
        {
            if (CanMutate(nameof(Remove)) && obj != null) RemoveInstance(obj.GetInstanceID());
        }

        static void RemoveInstance(int instanceID) => instanceIDs = CurrentIds().Where(id => id != instanceID).ToArray();

        static bool CanMutate(string member)
        {
            if (Compat.SelectionMutationsMeasured) return true;
            Stubs.Ignored("UnityEditor.Selection." + member, "selection mutations are unmeasured for this release family");
            return false;
        }

        public static bool Contains(int instanceID) => s_SelectedIds.Contains(instanceID)
            && (Compat.SelectionMutationsMeasured || Find(instanceID) != null);

        public static bool Contains(Object obj) => Contains(obj.GetInstanceID());

        public static void SetActiveObjectWithContext(Object obj, Object context)
        {
            int id = obj != null ? obj.GetInstanceID() : 0;
            SetActiveSelection(id, context != null ? context.GetInstanceID() : 0);
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

    /// <summary>Script reloads: every editor domain load ends with afterAssemblyReload, and a session
    /// a script edit restarts raises beforeAssemblyReload before the restart.</summary>
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

        [Unbound.EditorFaithful("no window shows the progress details (as the batch-mode Editor)")]
        public static void ShowDetails(bool shouldReposition = true) { }
    }

    public partial class EditorUtility
    {
        static readonly Dictionary<int, int> s_DirtyCounts = new Dictionary<int, int>();

        /// <summary>Marks an object changed: a dirty asset is written by AssetDatabase.SaveAssets
        /// and the Editor's exit (<see cref="AssetFiles"/>).</summary>
        public static void SetDirty(Object target)
        {
            if (target is null) throw new ArgumentNullException(nameof(target));
            if (target == null) return;
            int id = target.GetInstanceID();
            s_DirtyCounts[id] = (s_DirtyCounts.TryGetValue(id, out var n) ? n : 0) + 1;
            s_Dirty.Add(id);
            // An object of a scene dirties its scene in Edit mode (Scripting API, SetDirty;
            // measured: tests/projects/scenesave second.setDirty).
            if (!EditorApplication.isPlaying && !target.IsPersistentAsset)
            {
                var go = target as GameObject ?? (target as Component)?.gameObject;
                if (go != null && SceneRegistry.Get(go.m_Scene) is SceneData scene) SceneManagement.EditorSceneManager.Dirty(scene);
            }
        }

        static readonly HashSet<int> s_Dirty = new HashSet<int>();

        /// <summary>The instance IDs of the dirty objects.</summary>
        internal static IEnumerable<int> DirtyIds => s_Dirty;

        public static bool IsDirty(Object target) => target != null && IsDirty(target.GetInstanceID());

        public static bool IsDirty(int instanceID) => s_Dirty.Contains(instanceID);

        public static void ClearDirty(Object target)
        {
            if (target is null) throw new ArgumentNullException(nameof(target));
            if (target == null) return;
            s_Dirty.Remove(target.GetInstanceID());
            s_DirtyCounts.Remove(target.GetInstanceID());
        }

        /// <summary>Makes an object clean again with the dirty count it had (changes the Editor
        /// makes without dirtying the object).</summary>
        internal static void RestoreClean(Object target, int dirtyCount)
        {
            if (target is null) return;
            int id = target.GetInstanceID();
            s_Dirty.Remove(id);
            if (dirtyCount > 0) s_DirtyCounts[id] = dirtyCount;
            else s_DirtyCounts.Remove(id);
        }

        /// <summary>Runs an asset file's load callbacks (Awake, OnEnable, OnValidate of its objects):
        /// what they mark dirty among <paramref name="objects"/>, the file's own, is not a change, and
        /// those objects stay as clean as they were, whatever the callbacks set (matches the Unity
        /// 6000.0.71f1 Editor, tests/projects/urprendererwrites: a URP renderer whose OnEnable reloads
        /// a missing resource, script assets that call SetDirty from Awake, OnEnable or OnValidate,
        /// with a field changed or not, and a prefab's behaviour from OnValidate, are clean after their
        /// load, at a first open and in a later session, and not written). Unmeasured: objects of
        /// other files the callbacks mark (they keep their dirtiness), loads in Edit mode, the
        /// dirty count (left as it was), and the other users of the same load (asset bundles,
        /// serialized files read and forgotten, a VFX graph's models), which are treated alike.</summary>
        internal static void LoadCallbacks(ICollection<Object> objects, Action callbacks)
        {
            var before = new Dictionary<Object, (bool Dirty, int Count)>(objects.Count, ReferenceEqualityComparer.Instance);
            foreach (var o in objects)
                if (o is not null) before[o] = (IsDirty(o.GetInstanceID()), GetDirtyCount(o.GetInstanceID()));
            try { callbacks(); }
            finally
            {
                foreach (var (o, state) in before)
                    if (o != null && !state.Dirty && GetDirtyCount(o.GetInstanceID()) > state.Count) RestoreClean(o, state.Count);
            }
        }

        public static int GetDirtyCount(int instanceID) => s_DirtyCounts.TryGetValue(instanceID, out var n) ? n : 0;

        public static int GetDirtyCount(Object target) => target != null ? GetDirtyCount(target.GetInstanceID()) : 0;

        /// <summary>Script compilation succeeded: the game would not run otherwise.</summary>
        [Unbound.EditorFaithful("measured: tests/projects/editor env.scriptCompilationFailed")]
        public static bool scriptCompilationFailed => false;

        public static bool audioMasterMute { get; set; }

        /// <summary>A dialog nobody can answer: its cancel button (1), as the batch-mode Editor
        /// answers (measured).</summary>
        [Unbound.EditorFaithful("measured: tests/projects/editorui EditorUtility.DisplayDialogComplex")]
        public static int DisplayDialogComplex(string title, string message, string ok, string cancel, string alt) => 1;

        /// <summary>An object's enabled state as its inspector toggle shows it: 1 or 0, or -1 when it
        /// has no toggle. A script's behaviour has one only when it (or a script base class) has
        /// Start (a coroutine too), Update, FixedUpdate, LateUpdate, OnGUI, OnEnable, OnDisable,
        /// OnRenderImage or OnAudioFilterRead; a GameObject answers whether it is active in the
        /// hierarchy. Matches the Unity 6000.0.71f1 Editor (tests/projects/editortitlebar
        /// objectEnabled, objectEnabled/functions with 33 event functions, objectEnabled/gameObjects).</summary>
        public static int GetObjectEnabled(Object target) => target switch
        {
            // Other families keep the unmeasured answer: every behaviour's enabled state, none for a GameObject.
            _ when !Compat.EditorTitlebarMeasured => target switch
            {
                Behaviour b => b.enabled ? 1 : 0,
                Renderer r => r.enabled ? 1 : 0,
                Collider c => c.enabled ? 1 : 0,
                _ => -1,
            },
            MonoBehaviour m => HasEnabledToggle(m.GetType()) ? (m.enabled ? 1 : 0) : -1,
            Behaviour b => b.enabled ? 1 : 0,
            Renderer r => r.enabled ? 1 : 0,
            Collider c => c.enabled ? 1 : 0,
            GameObject g => g.activeInHierarchy ? 1 : 0,
            _ => -1,
        };

        static readonly Dictionary<Type, bool> s_HasEnabledToggle = new Dictionary<Type, bool>();
        static readonly Type[] s_AudioFilterParameters = { typeof(float[]), typeof(int) };

        /// <summary>Whether a script's behaviours have the enabled toggle (measured: none for Awake,
        /// OnDestroy, OnValidate, Reset, the gizmo, camera, visibility, application, animator,
        /// mouse and hierarchy messages).</summary>
        static bool HasEnabledToggle(Type type)
        {
            if (s_HasEnabledToggle.TryGetValue(type, out bool has)) return has;
            var m = Unbound.Runtime.ScriptMessages.For(type);
            has = m.Start != null || m.StartCoroutine != null || m.Update != null || m.FixedUpdate != null
                || m.LateUpdate != null || m.OnGUI != null || m.OnEnable != null || m.OnDisable != null
                || m.OnRenderImage != null
                || Unbound.Runtime.ScriptMessages.Find(type, "OnAudioFilterRead", s_AudioFilterParameters) != null;
            return s_HasEnabledToggle[type] = has;
        }

        public static void SetObjectEnabled(Object target, bool enabled)
        {
            switch (target)
            {
                case Behaviour b: b.enabled = enabled; break;
                // Its own active state (tests/projects/editortitlebar script/gameObject.up).
                case GameObject g when Compat.EditorTitlebarMeasured: g.SetActive(enabled); break;
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

        public static void RequestScriptReload() =>
            Unbound.Runtime.Stubs.Ignored("EditorUtility.RequestScriptReload", "does not reload the scripts (they reload when their sources change)");

        [Unbound.EditorFaithful("Unbound's Editor runs natively, not under Rosetta")]
        public static bool IsRunningUnderCPUEmulation() => false;

        [Unbound.EditorFaithful("the default parent only applies to objects created from the Hierarchy and GameObject menus, which Unbound's Editor has none of")]
        public static void SetDefaultParentObject(GameObject defaultParentObject) { }

        [Unbound.EditorFaithful("the default parent only applies to objects created from the Hierarchy and GameObject menus, which Unbound's Editor has none of")]
        public static void ClearDefaultParentObject() { }
    }
}
