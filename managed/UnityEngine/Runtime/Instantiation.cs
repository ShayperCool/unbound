using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Unbound.Runtime
{
    /// <summary>Object.Instantiate: clones a GameObject hierarchy (or a single asset object) the way
    /// Unity does, through serialization: serialized state is copied, references inside the cloned
    /// hierarchy point to the clones, and the clones wake up once they are all built.</summary>
    internal static class Instantiation
    {
        /// <summary>Engine objects that are not GameObjects or components clone through this.</summary>
        internal interface ICloneable
        {
            Object CloneObject();
        }

        internal static Object Clone(Object original, Transform parent, bool instantiateInWorldSpace, Vector3? position, Quaternion? rotation)
        {
            var sourceGo = original as GameObject ?? (original as Component)?.m_GameObject;
            if (sourceGo != null)
                return CloneGameObject(original, sourceGo, parent, instantiateInWorldSpace, position, rotation);

            Object clone;
            if (original is ScriptableObject so)
            {
                var copy = ScriptableObject.CreateInstanceWithoutCallbacks(so.GetType());
                // As Unity's: references to the original inside its data point at the copy
                // (an InputActionAsset's maps name their asset).
                new SerializedCopier(o => ReferenceEquals(o, so) ? copy : o).CopyScriptObject(so, copy);
                clone = copy;
                ((ScriptableObject)clone).RunCreationCallbacks();
            }
            else if (original is ICloneable cloneable)
            {
                clone = cloneable.CloneObject();
            }
            else
            {
                throw new ArgumentException($"Instantiating an object of type {original.GetType().Name} is not supported.");
            }
            clone.m_Name = original is Shader ? original.m_Name : original.m_Name + "(Clone)";
            return clone;
        }

        /// <summary>Object.Instantiate with InstantiateParameters: the parent decides the scene, else
        /// the given scene; with a parent, explicit coordinates are local unless worldSpace.</summary>
        internal static Object Clone(Object original, InstantiateParameters parameters, Vector3? position, Quaternion? rotation)
        {
            var sourceGo = original as GameObject ?? (original as Component)?.m_GameObject;
            if (sourceGo == null) return Clone(original, null, false, null, null);
            var parent = parameters.parent;
            var scene = TargetScene(parent, parameters.scene);
            var clone = Prepare(original, sourceGo, scene, "(Clone)");
            Place(clone, parent, scene, parameters.worldSpace, !parameters.worldSpace, position, rotation);
            Activate(new[] { clone });
            return clone.Result;
        }

        static Object CloneGameObject(Object original, GameObject sourceGo, Transform parent, bool instantiateInWorldSpace, Vector3? position, Quaternion? rotation)
        {
            var scene = TargetScene(parent, default);
            var clone = Prepare(original, sourceGo, scene, "(Clone)");
            Place(clone, parent, scene, instantiateInWorldSpace, false, position, rotation);
            Activate(new[] { clone });
            return clone.Result;
        }

        /// <summary>PrefabUtility.InstantiatePrefab: a copy named as the original, in the parent or
        /// scene given, whose objects <paramref name="linked"/> sees (originals to copies) before
        /// they wake up.</summary>
        internal static GameObject CloneLinked(GameObject original, Transform parent, Scene? scene, Action<Dictionary<Object, Object>> linked)
        {
            SceneHandle target = parent != null ? parent.m_GameObject.m_Scene
                : scene?.Data is SceneData d ? d.Handle : TargetScene(null, default);
            var clone = Prepare(original, original, target, "");
            Place(clone, parent, target, false, false, null, null);
            linked?.Invoke(clone.Map);
            Activate(new[] { clone });
            return clone.RootGo;
        }

        /// <summary>A copied hierarchy that is in no scene and has not woken up yet: the first half of
        /// Instantiate, which InstantiateAsync does when it is called.</summary>
        internal sealed class Prepared
        {
            internal Object Original;
            internal GameObject RootGo;
            internal Transform Root;
            internal Dictionary<Object, Object> Map;
            internal List<(Component Source, Component Clone)> Pairs;
            internal Vector3 SourcePosition, SourceLossyScale, SourceLocalScale;
            internal Quaternion SourceRotation;
            internal Transform Parent;

            internal Object Result => Original is GameObject ? RootGo : Map[Original];
        }

        /// <summary>The scene a clone goes to: its parent's, else the requested one if it is loaded,
        /// else the default one (the active scene, or the incoming scene while a Single load closes
        /// the scenes it replaces: SceneRegistry.CreationTarget). A scene still loading (its
        /// objects waking up or being processed, when the public isLoaded is false) keeps its
        /// copies, as SetActiveScene and MoveGameObjectToScene accept it; Unity's answer for that
        /// window is unmeasured.</summary>
        internal static SceneHandle TargetScene(Transform parent, Scene scene)
        {
            if (parent != null) return parent.m_GameObject.m_Scene;
            if (scene.Data is SceneData data && data.IsLoaded) return data.Handle;
            return SceneRegistry.DefaultCreationScene().Handle;
        }

        /// <summary>Copies the hierarchy of <paramref name="sourceGo"/> and the serialized state of its
        /// components, references inside it remapped to the copies. Nothing is placed or woken.</summary>
        internal static Prepared Prepare(Object original, GameObject sourceGo, SceneHandle scene, string suffix)
        {
            var map = new Dictionary<Object, Object>();
            var pairs = new List<(Component Source, Component Clone)>();
            var source = sourceGo.m_Transform;
            var rootClone = CloneTree(source, null, scene, map, pairs);
            var rootGo = rootClone.m_GameObject;
            rootGo.m_Name = sourceGo.m_Name + suffix;

            // Remap references into the cloned set, then copy component state.
            Object Remap(Object o) => o != null && map.TryGetValue(o, out var clone) ? clone : o;
            var copier = new SerializedCopier(Remap);
            foreach (var (src, dst) in pairs)
            {
                try
                {
                    if (src is MonoBehaviour)
                    {
                        ((Behaviour)dst).m_Enabled = ((Behaviour)src).m_Enabled;
                        copier.CopyScriptObject(src, dst);
                    }
                    else
                    {
                        dst.CopyFrom(src, Remap);
                    }
                }
                catch (Exception e)
                {
                    Debug.LogException(e, src);
                }
            }
            return new Prepared
            {
                Original = original, RootGo = rootGo, Root = rootClone, Map = map, Pairs = pairs,
                SourcePosition = source.position, SourceRotation = source.rotation, SourceLossyScale = source.lossyScale,
                SourceLocalScale = source.m_LocalScale,
            };
        }

        /// <summary>Puts a prepared copy into its scene or under its parent. With a parent, explicit
        /// coordinates are world coordinates unless <paramref name="localPose"/> (the parent then
        /// applies to them); without explicit coordinates the copied local pose stays unless
        /// <paramref name="instantiateInWorldSpace"/>.</summary>
        internal static void Place(Prepared clone, Transform parent, SceneHandle scene, bool instantiateInWorldSpace, bool localPose,
            Vector3? position, Quaternion? rotation)
        {
            var rootClone = clone.Root;
            foreach (var (_, dst) in clone.Pairs)
                if (dst is Transform moved) moved.m_GameObject.m_Scene = scene;
            if (parent == null || localPose)
            {
                // With no parent, Native keeps the copied local pose unless explicit world
                // coordinates were supplied. A null parent behaves the same for either flag.
                if (parent == null) SceneRegistry.Get(scene)?.Roots.Add(clone.RootGo);
                else
                {
                    rootClone.m_Parent = parent;
                    parent.m_Children.Add(rootClone);
                }
                if (rotation.HasValue) rootClone.m_LocalRotation = rotation.Value;
                if (position.HasValue) rootClone.SetLocalPosition(position.Value);
            }
            else
            {
                rootClone.m_Parent = parent;
                parent.m_Children.Add(rootClone);
                if (position.HasValue || rotation.HasValue)
                {
                    rootClone.KeepWorldPose(position ?? clone.SourcePosition, rotation ?? clone.SourceRotation, clone.SourceLossyScale, parent);
                    rootClone.m_LocalScale = clone.SourceLocalScale;
                }
                else if (instantiateInWorldSpace)
                {
                    rootClone.KeepWorldPose(clone.SourcePosition, clone.SourceRotation, clone.SourceLossyScale, parent);
                }
            }
            clone.Parent = parent;
            rootClone.MarkChanged();
            foreach (var (_, dst) in clone.Pairs)
                if (dst is Transform t) t.OnHierarchyChanged();

            // Bodies are attached before placement. Their initial inactive physics pose must
            // reflect the completed hierarchy, before callers can edit its Transform again.
            foreach (var (_, dst) in clone.Pairs)
                if (dst is Rigidbody rb) rb.CaptureInactivePose();
        }

        /// <summary>Wakes placed copies together: Awake and OnEnable go through the copies object by
        /// object of the original, each object of every copy before the next object.</summary>
        internal static void Activate(IReadOnlyList<Prepared> clones)
        {
            var components = new List<Component>();
            int count = clones.Count == 0 ? 0 : clones[0].Pairs.Count;
            for (int j = 0; j < count; j++)
                foreach (var clone in clones)
                {
                    var dst = clone.Pairs[j].Clone;
                    if (clone.RootGo.activeInHierarchy && !dst.m_Destroyed && dst.m_GameObject.activeInHierarchy)
                        components.Add(dst);
                }
            if (components.Count > 0) Lifecycle.Activate(components);
            foreach (var clone in clones)
                if (clone.Parent != null)
                    Messaging.SendFast(clone.Parent.m_GameObject, "OnTransformChildrenChanged");
        }

        /// <summary>Drops a prepared copy that was never placed: it never woke, so it gets no messages.</summary>
        internal static void Discard(Prepared clone)
        {
            foreach (var (_, dst) in clone.Pairs) dst.MarkDestroyed();
            foreach (var (_, dst) in clone.Pairs)
                if (dst is Transform t) t.m_GameObject.MarkDestroyed();
        }

        static Transform CloneTree(Transform source, Transform parentClone, SceneHandle scene, Dictionary<Object, Object> map, List<(Component, Component)> pairs)
        {
            var sourceGo = source.m_GameObject;
            var go = new GameObject(sourceGo.m_Name, bare: true)
            {
                m_IsActive = sourceGo.m_IsActive,
                m_Layer = sourceGo.m_Layer,
                m_Tag = sourceGo.m_Tag,
                m_Scene = scene,
            };
            go.isStatic = sourceGo.isStatic;
            map[sourceGo] = go;
            Transform transformClone = null;
            foreach (var c in sourceGo.m_Components)
            {
                if (c.m_Destroyed) continue;
                var clone = ComponentFactory.Create(c.GetType(), go);
                if (clone is Transform t)
                {
                    transformClone = t;
                    go.m_Transform = t;
                    go.m_Components.Insert(0, t);
                    t.m_LocalPosition = source.m_LocalPosition;
                    t.m_LocalRotation = source.m_LocalRotation;
                    t.m_LocalScale = source.m_LocalScale;
                }
                else
                {
                    go.m_Components.Add(clone);
                }
                map[c] = clone;
                pairs.Add((c, clone));
            }
            if (parentClone != null)
            {
                transformClone.m_Parent = parentClone;
                parentClone.m_Children.Add(transformClone);
            }
            foreach (var child in source.m_Children)
                CloneTree(child, transformClone, scene, map, pairs);
            return transformClone;
        }
    }

    /// <summary>Object.Destroy (deferred, or after a delay) and DestroyImmediate.</summary>
    internal static class Destruction
    {
        static readonly List<(Object Target, double Time)> s_Delayed = new List<(Object, double)>();
        /// <summary>All deferred requests, in enqueue order. The scripting points remove manual
        /// requests early; end-of-frame delivery skips those while the complete scripting pump
        /// is present. Keeping one queue preserves the old interleaving with assets and due
        /// delayed requests if a custom loop requires the end-of-frame fallback.</summary>
        static readonly List<Object> s_EndOfFrame = new List<Object>();
        /// <summary>Zero-delay requests of scene GameObjects and scripts, in request order: their
        /// scripts were disabled by the request; <see cref="DeliverRequested"/> destroys them.</summary>
        static readonly List<Object> s_Requested = new List<Object>();

        internal static void Destroy(Object obj, float delay)
        {
            if (delay > 0f)
            {
                s_Delayed.Add((obj, Time.timeAsDouble + delay));
                return;
            }
            if (obj.m_PendingDestroy) return;
            obj.m_PendingDestroy = true;
            s_EndOfFrame.Add(obj);
            NotifyEngineDestroyRequested(obj);
            if (!IsScriptRequest(obj)) return;
            // Accounted before any callback: Destroy of the same object from its OnDisable returns
            // above, and destructions requested by the callbacks queue after this one.
            s_Requested.Add(obj);
            Lifecycle.DisableForDestroyRequest(obj);
        }

        static void NotifyEngineDestroyRequested(Object obj)
        {
            // A required component survives a refused removal. Destroying its whole
            // GameObject still notifies it through the hierarchy's request.
            if (obj is Component component && RequiredBy(component) != null) return;
            obj.NotifyEngineDestroyRequested();
        }

        /// <summary>Preserves the existing automatic particle stop-action delivery at the end
        /// of the frame, without applying the manual request's script-disable contract.</summary>
        internal static void DestroyAtEndOfFrame(Object obj)
        {
            if (obj == null || obj.m_PendingDestroy) return;
            obj.m_PendingDestroy = true;
            s_EndOfFrame.Add(obj);
            NotifyEngineDestroyRequested(obj);
        }

        /// <summary>A zero-delay request whose scripts Destroy disables before it returns (measured
        /// in Play mode for awakened MonoBehaviours, GameObject and component targets:
        /// tests/projects/destroycontract). Edit mode, assets, prefab assets, built-in components
        /// and a script that another component still requires keep the end-of-frame path.</summary>
        static bool IsScriptRequest(Object obj)
        {
            if (!EditorSession.IsPlaying) return false;
            switch (obj)
            {
                case GameObject go:
                    return !go.m_IsPrefabAsset && !go.IsPersistentAsset;
                case MonoBehaviour mb:
                    var owner = mb.m_GameObject;
                    return owner != null && !owner.m_IsPrefabAsset && !owner.IsPersistentAsset && RequiredBy(mb) == null;
                default:
                    return false;
            }
        }

        /// <summary>Destroys the pending GameObject and script requests. It runs at the delayed
        /// scripting points where Unity delivered their OnDestroy: FixedUpdate's and Update's
        /// after their behaviour updates, and PostLateUpdate's (measured: tests/projects/
        /// destroycontract brackets; a request after TriggerEndOfFrameCallbacks reaches the next
        /// frame's FixedUpdate point). The scripts are already disabled, so no second OnDisable.
        /// Requests made by OnDestroy during the pass finish in the same pass.</summary>
        internal static void DeliverRequested()
        {
            while (s_Requested.Count > 0)
            {
                if (!PlayerLoopDriver.HasDestroyDeliveryPoints) return;
                var batch = s_Requested.ToArray();
                s_Requested.Clear();
                var targets = new HashSet<Object>(batch, ReferenceEqualityComparer.Instance);
                s_EndOfFrame.RemoveAll(targets.Contains);
                foreach (var obj in batch)
                {
                    if (obj.m_Destroyed) continue;
                    try { DestroyImmediate(obj); }
                    catch (Exception e) { Debug.LogException(e); }
                }
            }
        }

        /// <summary>Moves due delayed destructions to the end-of-frame list.</summary>
        internal static void RunDelayed()
        {
            if (s_Delayed.Count == 0) return;
            double now = Time.timeAsDouble;
            for (int i = s_Delayed.Count - 1; i >= 0; i--)
            {
                if (s_Delayed[i].Time > now) continue;
                var target = s_Delayed[i].Target;
                s_Delayed.RemoveAt(i);
                if (!target.m_Destroyed && !target.m_PendingDestroy)
                {
                    target.m_PendingDestroy = true;
                    s_EndOfFrame.Add(target);
                    NotifyEngineDestroyRequested(target);
                }
            }
        }

        internal static void FlushEndOfFrame()
        {
            while (s_EndOfFrame.Count > 0)
            {
                var held = s_Requested.Count > 0 && PlayerLoopDriver.HasDestroyDeliveryPoints
                    ? new HashSet<Object>(s_Requested, ReferenceEqualityComparer.Instance) : null;
                var batch = s_EndOfFrame.ToArray();
                s_EndOfFrame.Clear();
                var due = new List<Object>();
                // Partition before callbacks: new requests must follow every retained request,
                // even if a legacy request's OnDestroy enqueues them during this flush.
                foreach (var obj in batch)
                    if (held != null && held.Contains(obj)) s_EndOfFrame.Add(obj);
                    else due.Add(obj);
                if (due.Count == 0) return;
                if (held == null) s_Requested.Clear();
                foreach (var obj in due)
                    if (!obj.m_Destroyed) DestroyImmediate(obj);
            }
        }

        internal static void DestroyImmediate(Object obj)
        {
            if (Physics2DWorld.IsStepping && HasPhysics2D(obj))
            {
                Debug.LogError("DestroyImmediate is not permitted during a 2D physics callback.", obj);
                obj.m_PendingDestroy = false;
                return;
            }
            switch (obj)
            {
                case GameObject go:
                    DestroyGameObject(go);
                    break;
                case Transform t:
                    if (t.m_GameObject != null && !t.m_GameObject.m_Destroyed)
                    {
                        Debug.LogError("Destroying the transform component is not allowed.", t);
                        obj.m_PendingDestroy = false;
                    }
                    break;
                case Component c:
                    DestroyComponent(c);
                    break;
                case ScriptableObject so:
                    so.RunDestroyCallbacks();
                    so.MarkDestroyed();
                    break;
                case CustomRenderTexture crt:
                    // textureUnloaded sees the texture before it is destroyed.
                    CustomRenderTextureManager.Unregister(crt);
                    crt.MarkDestroyed();
                    break;
                default:
                    obj.MarkDestroyed();
                    break;
            }
        }

        static bool HasPhysics2D(Object obj)
        {
            if (obj is Rigidbody2D || obj is Collider2D) return true;
            if (!(obj is GameObject go)) return false;
            foreach (var c in go.m_Components)
                if (c is Rigidbody2D || c is Collider2D) return true;
            foreach (var child in go.m_Transform.m_Children)
                if (HasPhysics2D(child.m_GameObject)) return true;
            return false;
        }

        /// <summary>The first live component of <paramref name="c"/>'s GameObject that requires
        /// c's type when no other component satisfies the requirement, or null.</summary>
        static Component RequiredBy(Component c)
        {
            var go = c.m_GameObject;
            if (go == null) return null;
            foreach (var other in go.m_Components)
            {
                if (other == c || other.m_Destroyed) continue;
                foreach (var required in ComponentFactory.RequiredComponents(other.GetType()))
                {
                    if (!required.IsInstanceOfType(c)) continue;
                    // Another component of the type could satisfy the requirement.
                    bool replaced = false;
                    foreach (var candidate in go.m_Components)
                        if (candidate != c && !candidate.m_Destroyed && required.IsInstanceOfType(candidate)) replaced = true;
                    if (!replaced) return other;
                }
            }
            return null;
        }

        static void DestroyComponent(Component c)
        {
            var go = c.m_GameObject;
            var other = RequiredBy(c);
            if (other != null)
            {
                Debug.LogError($"Can't remove {c.GetType().Name} because {other.GetType().Name} ({other.GetType().Name}) depends on it", c);
                c.m_PendingDestroy = false;
                return;
            }
            // Removing only the body converts its surviving colliders to static without exits.
            if (c is Rigidbody2D body2D) Physics2DWorld.Remove(body2D, silent: true);
            Lifecycle.OnComponentDestroyed(c);
            go?.m_Components.Remove(c);
            c.MarkDestroyed();
        }

        static void DestroyGameObject(GameObject root)
        {
            var all = new List<GameObject>();
            Collect(root.m_Transform, all);
            if (root.activeInHierarchy)
            {
                var active = new List<Component>();
                foreach (var go in all)
                    if (go.activeInHierarchy)
                        foreach (var c in go.m_Components)
                            if (!c.m_Destroyed) active.Add(c);
                Lifecycle.Deactivate(active);
            }
            foreach (var go in all)
                foreach (var c in go.m_Components.ToArray())
                    if (!c.m_Destroyed) Lifecycle.OnComponentDestroyed(c);

            var parent = root.m_Transform.m_Parent;
            if (parent != null)
            {
                parent.m_Children.Remove(root.m_Transform);
                root.m_Transform.m_Parent = null;
                parent.OnHierarchyChanged();
                Messaging.SendFast(parent.m_GameObject, "OnTransformChildrenChanged");
            }
            else
            {
                SceneRegistry.RemoveRoot(root);
            }
            foreach (var go in all)
            {
                foreach (var c in go.m_Components) c.MarkDestroyed();
                go.MarkDestroyed();
            }
        }

        static void Collect(Transform t, List<GameObject> into)
        {
            into.Add(t.m_GameObject);
            foreach (var child in t.m_Children) Collect(child, into);
        }

        internal static void Reset()
        {
            s_Delayed.Clear();
            s_EndOfFrame.Clear();
            s_Requested.Clear();
        }
    }
}
