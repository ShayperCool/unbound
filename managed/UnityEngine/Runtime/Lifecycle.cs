using System;
using System.Collections.Generic;
using UnityEngine;

namespace Unbound.Runtime
{
    /// <summary>
    /// Awake, OnEnable, Start, OnDisable and OnDestroy, and the per-frame update lists.
    ///
    /// Unity's rules: Awake runs once, when the GameObject is first active in the hierarchy, even for a
    /// disabled script; OnEnable runs right after it for an enabled script and whenever the script
    /// becomes enabled and active again; Start runs once, before the first Update, for an enabled
    /// script; OnDisable mirrors OnEnable; OnDestroy runs for scripts that were awake.
    /// </summary>
    internal static class Lifecycle
    {
        /// <summary>An entry of an update list, ordered by execution order then by when it joined.</summary>
        sealed class UpdateList
        {
            readonly List<(int Order, long Seq, MonoBehaviour Behaviour, Action<MonoBehaviour> Call)> m_Items =
                new List<(int, long, MonoBehaviour, Action<MonoBehaviour>)>();
            readonly HashSet<MonoBehaviour> m_Members = new HashSet<MonoBehaviour>();
            int m_Removed;

            internal void Add(MonoBehaviour b, int order, long seq, Action<MonoBehaviour> call)
            {
                if (!m_Members.Add(b)) return;
                var item = (order, seq, b, call);
                int index = m_Items.Count;
                while (index > 0 && Compare(m_Items[index - 1], item) > 0) index--;
                m_Items.Insert(index, item);
            }

            static int Compare((int Order, long Seq, MonoBehaviour, Action<MonoBehaviour>) a, (int Order, long Seq, MonoBehaviour, Action<MonoBehaviour>) b)
            {
                int c = a.Order.CompareTo(b.Order);
                return c != 0 ? c : a.Seq.CompareTo(b.Seq);
            }

            internal void Remove(MonoBehaviour b)
            {
                if (m_Members.Remove(b)) m_Removed++;
            }

            internal void Run(string message)
            {
                // Iterate over the entries present when the pass starts; entries removed during the
                // pass are skipped, entries added join the next pass.
                int count = m_Items.Count;
                for (int i = 0; i < count && i < m_Items.Count; i++)
                {
                    var item = m_Items[i];
                    var b = item.Behaviour;
                    if (!m_Members.Contains(b) || !b.m_DidStart || b.m_Destroyed) continue;
                    long start = Trace.PerfOn ? System.Diagnostics.Stopwatch.GetTimestamp() : 0;
                    try
                    {
                        item.Call(b);
                    }
                    catch (Exception e)
                    {
                        Debug.LogException(e, b);
                    }
                    if (Trace.PerfOn) Trace.Perf(message + " " + b.GetType().FullName, start);
                }
                if (m_Removed > 0)
                {
                    m_Items.RemoveAll(entry => !m_Members.Contains(entry.Behaviour));
                    m_Removed = 0;
                }
            }

            internal void Clear()
            {
                m_Items.Clear();
                m_Members.Clear();
                m_Removed = 0;
            }
        }

        static readonly UpdateList s_Update = new UpdateList();
        static readonly UpdateList s_LateUpdate = new UpdateList();
        static readonly UpdateList s_FixedUpdate = new UpdateList();
        static readonly List<MonoBehaviour> s_PendingStart = new List<MonoBehaviour>();
        static readonly HashSet<MonoBehaviour> s_OnGUI = new HashSet<MonoBehaviour>();
        static long s_Sequence;

        internal static IEnumerable<MonoBehaviour> GuiBehaviours => s_OnGUI;

        // ------------------------------------------------------------------ entry points

        /// <summary>A component was just attached to a GameObject.</summary>
        internal static void OnComponentAdded(Component c)
        {
            var go = c.m_GameObject;
            if (go.m_IsPrefabAsset || !go.activeInHierarchy) return;
            Activate(new List<Component> { c });
        }

        internal static void OnHierarchyActiveChanged(GameObject go, bool active)
        {
            var components = new List<Component>();
            CollectActive(go.m_Transform, components, includeRoot: true);
            if (active) Activate(components);
            else Deactivate(components);
        }

        /// <summary>Components of the subtree whose GameObjects are active themselves (activeSelf).</summary>
        static void CollectActive(Transform t, List<Component> into, bool includeRoot)
        {
            var go = t.m_GameObject;
            // The root is the object whose state changed; below it, inactive objects stay asleep.
            if (go.m_Destroyed || (!go.m_IsActive && !includeRoot)) return;
            foreach (var c in go.m_Components)
                if (!c.m_Destroyed) into.Add(c);
            foreach (var child in t.m_Children)
                CollectActive(child, into, includeRoot: false);
        }

        internal static void OnBehaviourEnabledChanged(Behaviour b, bool enabled)
        {
            var go = b.m_GameObject;
            if (go == null || go.m_IsPrefabAsset || !go.activeInHierarchy) return;
            if (b is MonoBehaviour mb)
            {
                if (enabled)
                {
                    if (!mb.m_DidAwake) CallAwake(mb);
                    if (mb.m_Enabled && !mb.m_Destroyed) Enable(mb);
                }
                else
                {
                    Disable(mb);
                }
            }
            else if (enabled)
            {
                b.OnEngineEnable();
            }
            else
            {
                b.OnEngineDisable();
            }
        }

        // ------------------------------------------------------------------ activation

        /// <summary>Awake and OnEnable for components that just became active in the hierarchy.
        /// Scripts go in script execution order (stable), each one's Awake right before its OnEnable.</summary>
        internal static void Activate(List<Component> components)
        {
            var scripts = new List<MonoBehaviour>();
            foreach (var c in components)
            {
                if (c is MonoBehaviour mb) scripts.Add(mb);
                else if (!(c is Behaviour b) || b.m_Enabled) SafeEngineEnable(c);
            }
            StableSortByOrder(scripts);
            foreach (var mb in scripts)
            {
                if (mb.m_Destroyed || !mb.m_GameObject.activeInHierarchy) continue;
                if (!mb.m_DidAwake) CallAwake(mb);
                if (mb.m_Enabled && !mb.m_Destroyed && mb.m_GameObject.activeInHierarchy) Enable(mb);
            }
        }

        static void SafeEngineEnable(Component c)
        {
            try { c.OnEngineEnable(); }
            catch (Exception e) { Debug.LogException(e, c); }
        }

        static void StableSortByOrder(List<MonoBehaviour> scripts)
        {
            var keyed = new List<(int Order, int Index, MonoBehaviour B)>(scripts.Count);
            for (int i = 0; i < scripts.Count; i++)
                keyed.Add((ScriptMessages.For(scripts[i].GetType()).ExecutionOrder, i, scripts[i]));
            keyed.Sort((a, b) => a.Order != b.Order ? a.Order.CompareTo(b.Order) : a.Index.CompareTo(b.Index));
            for (int i = 0; i < keyed.Count; i++) scripts[i] = keyed[i].B;
        }

        internal static void Deactivate(List<Component> components)
        {
            foreach (var c in components)
            {
                if (c.m_Destroyed) continue;
                if (c is MonoBehaviour mb)
                {
                    if (mb.m_Enabled) Disable(mb);
                    Coroutines.StopAll(mb);
                }
                else if (!(c is Behaviour b) || b.m_Enabled)
                {
                    try { c.OnEngineDisable(); }
                    catch (Exception e) { Debug.LogException(e, c); }
                }
            }
        }

        static void CallAwake(MonoBehaviour mb)
        {
            mb.m_DidAwake = true;
            var call = ScriptMessages.For(mb.GetType()).Awake;
            if (call == null) return;
            Trace.Lifecycle("Awake", mb);
            try { call(mb); }
            catch (Exception e) { Debug.LogException(e, mb); }
        }

        /// <summary>The behaviour is enabled and active: OnEnable, and it joins the update lists.</summary>
        static void Enable(MonoBehaviour mb)
        {
            var m = ScriptMessages.For(mb.GetType());
            var seq = ++s_Sequence;
            if (!mb.m_DidStart && !s_PendingStart.Contains(mb)) s_PendingStart.Add(mb);
            if (m.Update != null) s_Update.Add(mb, m.ExecutionOrder, seq, m.Update);
            if (m.LateUpdate != null) s_LateUpdate.Add(mb, m.ExecutionOrder, seq, m.LateUpdate);
            if (m.FixedUpdate != null) s_FixedUpdate.Add(mb, m.ExecutionOrder, seq, m.FixedUpdate);
            if (m.OnGUI != null) s_OnGUI.Add(mb);
            mb.m_EnabledState = true;
            if (m.OnEnable != null)
            {
                try { m.OnEnable(mb); }
                catch (Exception e) { Debug.LogException(e, mb); }
            }
        }

        static void Disable(MonoBehaviour mb)
        {
            RemoveFromLists(mb);
            // Only a behaviour whose OnEnable ran gets OnDisable (one deactivated during its
            // GameObject's activation, before its turn, gets neither).
            if (!mb.m_EnabledState) return;
            mb.m_EnabledState = false;
            var m = ScriptMessages.For(mb.GetType());
            if (m.OnDisable != null)
            {
                try { m.OnDisable(mb); }
                catch (Exception e) { Debug.LogException(e, mb); }
            }
        }

        static void RemoveFromLists(MonoBehaviour mb)
        {
            s_Update.Remove(mb);
            s_LateUpdate.Remove(mb);
            s_FixedUpdate.Remove(mb);
            s_OnGUI.Remove(mb);
        }

        // ------------------------------------------------------------------ destruction

        /// <summary>OnDisable (when active and enabled) then OnDestroy (when awake) for one component.</summary>
        internal static void OnComponentDestroyed(Component c)
        {
            if (c is MonoBehaviour mb)
            {
                if (mb.isActiveAndEnabled) Disable(mb);
                else RemoveFromLists(mb);
                s_PendingStart.Remove(mb);
                Coroutines.StopAll(mb);
                Invokes.Cancel(mb, null);
                mb.CancelDestroyToken();
                if (mb.m_DidAwake)
                {
                    var m = ScriptMessages.For(mb.GetType());
                    if (m.OnDestroy != null)
                    {
                        try { m.OnDestroy(mb); }
                        catch (Exception e) { Debug.LogException(e, mb); }
                    }
                }
            }
            else if (c.m_GameObject != null && c.m_GameObject.activeInHierarchy && (!(c is Behaviour b) || b.m_Enabled))
            {
                try { c.OnEngineDisable(); }
                catch (Exception e) { Debug.LogException(e, c); }
            }
        }

        // ------------------------------------------------------------------ the frame

        /// <summary>Start for every behaviour enabled since the last pass, in execution order.</summary>
        internal static void RunPendingStarts()
        {
            while (s_PendingStart.Count > 0)
            {
                var batch = new List<MonoBehaviour>(s_PendingStart);
                s_PendingStart.Clear();
                StableSortByOrder(batch);
                foreach (var mb in batch)
                {
                    if (mb.m_DidStart || mb.m_Destroyed) continue;
                    if (!mb.isActiveAndEnabled)
                        continue; // Start waits until it is enabled again
                    mb.m_DidStart = true;
                    var m = ScriptMessages.For(mb.GetType());
                    if (m.Start != null || m.StartCoroutine != null) Trace.Lifecycle("Start", mb);
                    try
                    {
                        if (m.Start != null) m.Start(mb);
                        else if (m.StartCoroutine != null)
                        {
                            var routine = m.StartCoroutine(mb);
                            if (routine != null) Coroutines.Start(mb, routine, "Start");
                        }
                    }
                    catch (Exception e)
                    {
                        Debug.LogException(e, mb);
                    }
                }
            }
        }

        internal static void RunFixedUpdate() => s_FixedUpdate.Run("FixedUpdate");

        internal static void RunUpdate() => s_Update.Run("Update");

        internal static void RunLateUpdate() => s_LateUpdate.Run("LateUpdate");

        /// <summary>Rebuilds the update lists from the active and enabled behaviours with their current
        /// methods, without calling OnEnable (after code changed in place).</summary>
        internal static void RefreshRegistrations()
        {
            s_Update.Clear();
            s_LateUpdate.Clear();
            s_FixedUpdate.Clear();
            s_OnGUI.Clear();
            foreach (var go in SceneRegistry.AllGameObjects(includeInactive: false))
            {
                foreach (var c in go.m_Components)
                {
                    if (!(c is MonoBehaviour mb) || !mb.isActiveAndEnabled) continue;
                    var m = ScriptMessages.For(mb.GetType());
                    var seq = ++s_Sequence;
                    if (m.Update != null) s_Update.Add(mb, m.ExecutionOrder, seq, m.Update);
                    if (m.LateUpdate != null) s_LateUpdate.Add(mb, m.ExecutionOrder, seq, m.LateUpdate);
                    if (m.FixedUpdate != null) s_FixedUpdate.Add(mb, m.ExecutionOrder, seq, m.FixedUpdate);
                    if (m.OnGUI != null) s_OnGUI.Add(mb);
                }
            }
        }

        /// <summary>Forgets every behaviour: used when the game domain is torn down.</summary>
        internal static void Reset()
        {
            s_Update.Clear();
            s_LateUpdate.Clear();
            s_FixedUpdate.Clear();
            s_PendingStart.Clear();
            s_OnGUI.Clear();
        }

        /// <summary>A message every live script gets (OnApplicationPause, OnApplicationFocus, OnApplicationQuit).</summary>
        internal static void Broadcast(Func<ScriptMessages, Delegate> pick, object argument)
        {
            foreach (var go in SceneRegistry.AllGameObjects(includeInactive: false))
            {
                foreach (var c in go.m_Components.ToArray())
                {
                    if (!(c is MonoBehaviour mb) || !mb.isActiveAndEnabled) continue;
                    var call = pick(ScriptMessages.For(mb.GetType()));
                    if (call == null) continue;
                    try
                    {
                        if (call is Action<MonoBehaviour> a) a(mb);
                        else if (call is Action<MonoBehaviour, bool> ab) ab(mb, (bool)argument);
                    }
                    catch (Exception e)
                    {
                        Debug.LogException(e, mb);
                    }
                }
            }
        }
    }
}
