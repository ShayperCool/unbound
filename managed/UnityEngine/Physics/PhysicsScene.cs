using System;
using UnityEngine.SceneManagement;
using Q = Unbound.Runtime.PhysicsQueries;
using W = Unbound.Runtime.PhysicsWorld;

namespace UnityEngine
{
    /// <summary>
    /// A 3D physics scene: Physics.defaultPhysicsScene, or the local one a scene created or loaded
    /// with LocalPhysicsMode.Physics3D owns (6000.0 Scripting API: PhysicsScene, LocalPhysicsMode,
    /// PhysicsScene.Simulate, PhysicsScene.IsValid, PhysicsSceneExtensions.GetPhysicsScene). Its
    /// queries search only its own colliders and its Simulate steps only its own components. A local
    /// physics scene is never simulated automatically, and it is no longer valid once its scene is
    /// unloaded; the default one is always valid.
    /// What Unity does with an invalid physics scene is not documented. Unbound's choice
    /// (unmeasured): IsEmpty and Simulate throw InvalidOperationException, and its queries find
    /// nothing.
    /// </summary>
    public partial struct PhysicsScene
    {
        internal PhysicsScene(int index, int version)
        {
            m_index = index;
            m_version = version;
        }

        /// <summary>The live physics scene this one names; null once it is gone.</summary>
        W.Space Live() => W.Find(m_index, m_version);

        public bool IsEmpty()
        {
            // Unbound's choice (unmeasured): the 6000.0 Scripting API documents only valid scenes.
            if (!IsValid())
                throw new InvalidOperationException($"PhysicsScene.IsEmpty: {this} is not valid (the Scene that owned it was unloaded or destroyed).");
            var space = Live();
            return W.IsEmpty(space);
        }

        public void Simulate(float step)
        {
            // Unbound's choice (unmeasured): the 6000.0 Scripting API documents only valid scenes.
            if (!IsValid())
                throw new InvalidOperationException($"PhysicsScene.Simulate: {this} is not valid (the Scene that owned it was unloaded or destroyed).");
            var space = Live();
            // The default physics scene steps as Physics.Simulate steps it, with that method's
            // simulation-mode requirement (Unbound's reading: the page documents no difference).
            if (ReferenceEquals(space, W.DefaultSpace))
            {
                Physics.Simulate(step);
                return;
            }
            W.EnsureRegistered();
            W.Simulate(space, step);
        }

        // ------------------------------------------------------------------ queries
        // Each is the matching Physics query, searching this physics scene's colliders only.

        Q.Target Searching() => Q.In(Live() ?? W.NoSpace);

        public bool Raycast(Vector3 origin, Vector3 direction, float maxDistance = float.PositiveInfinity, int layerMask = -5, QueryTriggerInteraction queryTriggerInteraction = QueryTriggerInteraction.UseGlobal)
        {
            using (Searching()) return Physics.Raycast(origin, direction, maxDistance, layerMask, queryTriggerInteraction);
        }

        public bool Raycast(Vector3 origin, Vector3 direction, out RaycastHit hitInfo, float maxDistance = float.PositiveInfinity, int layerMask = -5, QueryTriggerInteraction queryTriggerInteraction = QueryTriggerInteraction.UseGlobal)
        {
            using (Searching()) return Physics.Raycast(origin, direction, out hitInfo, maxDistance, layerMask, queryTriggerInteraction);
        }

        public int Raycast(Vector3 origin, Vector3 direction, RaycastHit[] raycastHits, float maxDistance = float.PositiveInfinity, int layerMask = -5, QueryTriggerInteraction queryTriggerInteraction = QueryTriggerInteraction.UseGlobal)
        {
            using (Searching()) return Physics.RaycastNonAlloc(origin, direction, raycastHits, maxDistance, layerMask, queryTriggerInteraction);
        }

        public bool CapsuleCast(Vector3 point1, Vector3 point2, float radius, Vector3 direction, out RaycastHit hitInfo, float maxDistance = float.PositiveInfinity, int layerMask = -5, QueryTriggerInteraction queryTriggerInteraction = QueryTriggerInteraction.UseGlobal)
        {
            using (Searching()) return Physics.CapsuleCast(point1, point2, radius, direction, out hitInfo, maxDistance, layerMask, queryTriggerInteraction);
        }

        public int CapsuleCast(Vector3 point1, Vector3 point2, float radius, Vector3 direction, RaycastHit[] results, float maxDistance = float.PositiveInfinity, int layerMask = -5, QueryTriggerInteraction queryTriggerInteraction = QueryTriggerInteraction.UseGlobal)
        {
            using (Searching()) return Physics.CapsuleCastNonAlloc(point1, point2, radius, direction, results, maxDistance, layerMask, queryTriggerInteraction);
        }

        public int OverlapCapsule(Vector3 point0, Vector3 point1, float radius, Collider[] results, int layerMask = -1, QueryTriggerInteraction queryTriggerInteraction = QueryTriggerInteraction.UseGlobal)
        {
            using (Searching()) return Physics.OverlapCapsuleNonAlloc(point0, point1, radius, results, layerMask, queryTriggerInteraction);
        }

        public bool SphereCast(Vector3 origin, float radius, Vector3 direction, out RaycastHit hitInfo, float maxDistance = float.PositiveInfinity, int layerMask = -5, QueryTriggerInteraction queryTriggerInteraction = QueryTriggerInteraction.UseGlobal)
        {
            using (Searching()) return Physics.SphereCast(origin, radius, direction, out hitInfo, maxDistance, layerMask, queryTriggerInteraction);
        }

        public int SphereCast(Vector3 origin, float radius, Vector3 direction, RaycastHit[] results, float maxDistance = float.PositiveInfinity, int layerMask = -5, QueryTriggerInteraction queryTriggerInteraction = QueryTriggerInteraction.UseGlobal)
        {
            using (Searching()) return Physics.SphereCastNonAlloc(origin, radius, direction, results, maxDistance, layerMask, queryTriggerInteraction);
        }

        public int OverlapSphere(Vector3 position, float radius, Collider[] results, int layerMask, QueryTriggerInteraction queryTriggerInteraction)
        {
            using (Searching()) return Physics.OverlapSphereNonAlloc(position, radius, results, layerMask, queryTriggerInteraction);
        }

        public bool BoxCast(Vector3 center, Vector3 halfExtents, Vector3 direction, out RaycastHit hitInfo, Quaternion orientation, float maxDistance = float.PositiveInfinity, int layerMask = -5, QueryTriggerInteraction queryTriggerInteraction = QueryTriggerInteraction.UseGlobal)
        {
            using (Searching()) return Physics.BoxCast(center, halfExtents, direction, out hitInfo, orientation, maxDistance, layerMask, queryTriggerInteraction);
        }

        public bool BoxCast(Vector3 center, Vector3 halfExtents, Vector3 direction, out RaycastHit hitInfo)
        {
            using (Searching()) return Physics.BoxCast(center, halfExtents, direction, out hitInfo);
        }

        public int BoxCast(Vector3 center, Vector3 halfExtents, Vector3 direction, RaycastHit[] results, Quaternion orientation, float maxDistance = float.PositiveInfinity, int layerMask = -5, QueryTriggerInteraction queryTriggerInteraction = QueryTriggerInteraction.UseGlobal)
        {
            using (Searching()) return Physics.BoxCastNonAlloc(center, halfExtents, direction, results, orientation, maxDistance, layerMask, queryTriggerInteraction);
        }

        public int BoxCast(Vector3 center, Vector3 halfExtents, Vector3 direction, RaycastHit[] results)
        {
            using (Searching()) return Physics.BoxCastNonAlloc(center, halfExtents, direction, results);
        }

        public int OverlapBox(Vector3 center, Vector3 halfExtents, Collider[] results, Quaternion orientation, int layerMask = -5, QueryTriggerInteraction queryTriggerInteraction = QueryTriggerInteraction.UseGlobal)
        {
            using (Searching()) return Physics.OverlapBoxNonAlloc(center, halfExtents, results, orientation, layerMask, queryTriggerInteraction);
        }

        public int OverlapBox(Vector3 center, Vector3 halfExtents, Collider[] results)
        {
            // The stub's signature defaults the full overload's mask to DefaultRaycastLayers (-5).
            using (Searching()) return Physics.OverlapBoxNonAlloc(center, halfExtents, results, Quaternion.identity, Physics.DefaultRaycastLayers, QueryTriggerInteraction.UseGlobal);
        }
    }

    public static partial class PhysicsSceneExtensions
    {
        /// <summary>The scene's own local 3D physics scene, or Physics.defaultPhysicsScene when it
        /// has none (6000.0 Scripting API, PhysicsSceneExtensions.GetPhysicsScene). A scene whose
        /// local physics scene went with its unload answers that physics scene, now not valid.</summary>
        public static PhysicsScene GetPhysicsScene(this Scene scene)
        {
            // Unbound's choice (unmeasured): the 6000.0 Scripting API documents only valid scenes.
            if (!scene.IsValid())
                throw new ArgumentException("GetPhysicsScene: the Scene is not valid (it does not exist or has been unloaded).", nameof(scene));
            var space = W.LocalOf(scene.handle);
            return space != null ? W.SceneOf(space) : Physics.defaultPhysicsScene;
        }
    }
}
