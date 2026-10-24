using System;
using Unbound.Runtime;

namespace UnityEngine.AI
{
    /// <summary>
    /// An obstacle agents avoid; a carving obstacle cuts its footprint (grown by the agents'
    /// radius) out of the NavMesh while it stands still.
    /// </summary>
    [ClassId(208)]
    public sealed partial class NavMeshObstacle : Behaviour
    {
        float m_Height = 1f;
        float m_Radius = 0.5f;
        Vector3 m_Velocity;
        bool m_Carving;
        bool m_CarveOnlyStationary = true;
        float m_CarvingMoveThreshold = 0.1f;
        float m_CarvingTimeToStationary = 0.5f;
        NavMeshObstacleShape m_Shape = NavMeshObstacleShape.Box;
        Vector3 m_Center;
        Vector3 m_Size = Vector3.one;

        public NavMeshObstacle() { }

        public float height { get => m_Height; set { m_Height = Mathf.Max(0f, value); ObstacleSystem.Changed(this); } }
        public float radius { get => m_Radius; set { m_Radius = Mathf.Max(0f, value); ObstacleSystem.Changed(this); } }
        public Vector3 velocity { get => m_Velocity; set => m_Velocity = value; }
        public bool carving { get => m_Carving; set { m_Carving = value; ObstacleSystem.Changed(this); } }
        public bool carveOnlyStationary { get => m_CarveOnlyStationary; set => m_CarveOnlyStationary = value; }
        public float carvingMoveThreshold { get => m_CarvingMoveThreshold; set => m_CarvingMoveThreshold = Mathf.Max(0f, value); }
        public float carvingTimeToStationary { get => m_CarvingTimeToStationary; set => m_CarvingTimeToStationary = Mathf.Max(0f, value); }
        public NavMeshObstacleShape shape { get => m_Shape; set { m_Shape = value; ObstacleSystem.Changed(this); } }
        public Vector3 center { get => m_Center; set { m_Center = value; ObstacleSystem.Changed(this); } }
        public Vector3 size { get => m_Size; set { m_Size = Vector3.Max(value, Vector3.zero); ObstacleSystem.Changed(this); } }

        /// <summary>Fits the obstacle to the GameObject's mesh, as Unity does when it is added.</summary>
        internal void FitExtents()
        {
            var filter = m_GameObject.GetComponent<MeshFilter>();
            if (filter != null && filter.sharedMesh != null)
            {
                var b = filter.sharedMesh.bounds;
                m_Center = b.center;
                m_Size = b.size;
                m_Radius = Mathf.Max(b.extents.x, b.extents.z);
                m_Height = b.size.y;
            }
        }

        internal override void OnAttached()
        {
            base.OnAttached();
            FitExtents();
        }

        internal override void OnEngineEnable() => ObstacleSystem.Add(this);

        internal override void OnEngineDisable() => ObstacleSystem.Remove(this);

        internal override void OnEngineDestroy() => ObstacleSystem.Remove(this);

        internal override void ReadSerialized(YNode body, IReferenceResolver resolver)
        {
            m_Shape = (NavMeshObstacleShape)body.Int("m_Shape", 1);
            m_Size = body.Vector3("m_Extents", new Vector3(0.5f, 0.5f, 0.5f)) * 2f;
            m_Center = body.Vector3("m_Center");
            m_Carving = body.Bool("m_Carve");
            m_CarveOnlyStationary = body.Bool("m_CarveOnlyStationary", true);
            m_CarvingMoveThreshold = body.Float("m_MoveThreshold", 0.1f);
            m_CarvingTimeToStationary = body.Float("m_TimeToStationary", 0.5f);
            m_Radius = m_Size.x * 0.5f;
            m_Height = m_Size.y;
        }

        internal override void CopyFrom(Component source, Func<Object, Object> remap)
        {
            var s = (NavMeshObstacle)source;
            m_Height = s.m_Height;
            m_Radius = s.m_Radius;
            m_Carving = s.m_Carving;
            m_CarveOnlyStationary = s.m_CarveOnlyStationary;
            m_CarvingMoveThreshold = s.m_CarvingMoveThreshold;
            m_CarvingTimeToStationary = s.m_CarvingTimeToStationary;
            m_Shape = s.m_Shape;
            m_Center = s.m_Center;
            m_Size = s.m_Size;
        }
    }

    /// <summary>The legacy link component: a link between two transforms.</summary>
    [Obsolete("The OffMeshLink component is no longer supported and will be removed. Use NavMeshLink instead.")]
    [ClassId(191)]
    public sealed partial class OffMeshLink : Behaviour
    {
        Transform m_Start;
        Transform m_End;
        float m_CostOverride = -1f;
        bool m_BiDirectional = true;
        bool m_Activated = true;
        int m_Area;
        bool m_AutoUpdatePositions;
        int m_Link;

        public OffMeshLink() { }

        [Obsolete("activated has been deprecated together with the class. Declare the object as NavMeshLink and use activated as before.")]
        public bool activated
        {
            get => m_Activated;
            set
            {
                m_Activated = value;
                if (m_Link != 0) NavMesh.SetOffMeshConnectionActive(m_Link, value);
            }
        }

        [Obsolete("occupied has been deprecated together with the class. Declare the object as NavMeshLink and use occupied as before.")]
        public bool occupied => m_Link != 0 && NavMesh.IsOffMeshConnectionOccupied(m_Link);

        [Obsolete("costOverride has been deprecated together with the class. Declare the object as NavMeshLink and use costModifier instead.")]
        public float costOverride { get => m_CostOverride; set { m_CostOverride = value; Relink(); } }

        [Obsolete("biDirectional has been deprecated together with the class. Declare the object as NavMeshLink and use bidirectional instead.")]
        public bool biDirectional { get => m_BiDirectional; set { m_BiDirectional = value; Relink(); } }

        [Obsolete("navMeshLayer has been deprecated together with the class. Declare the object as NavMeshLink and use area instead. (UnityUpgradable) -> area")]
        public int navMeshLayer { get => m_Area; set { m_Area = value; Relink(); } }

        [Obsolete("area has been deprecated together with the class. Declare the object as NavMeshLink and use area as before.")]
        public int area { get => m_Area; set { m_Area = value; Relink(); } }

        [Obsolete("autoUpdatePositions has been deprecated together with the class. Declare the object as NavMeshLink and use autoUpdate instead.")]
        public bool autoUpdatePositions { get => m_AutoUpdatePositions; set => m_AutoUpdatePositions = value; }

        [Obsolete("startTransform has been deprecated together with the class. Declare the object as NavMeshLink and use startTransform as before.")]
        public Transform startTransform { get => m_Start; set { m_Start = value; Relink(); } }

        [Obsolete("endTransform has been deprecated together with the class. Declare the object as NavMeshLink and use endTransform as before.")]
        public Transform endTransform { get => m_End; set { m_End = value; Relink(); } }

        [Obsolete("UpdatePositions() has been deprecated together with the class. Declare the object as NavMeshLink and use UpdateLink() instead.")]
        public void UpdatePositions() => Relink();

        void Relink()
        {
            if (m_Link != 0) NavMesh.RemoveLinkInternal(m_Link);
            m_Link = 0;
            if (!isActiveAndEnabled || m_Start == null || m_End == null) return;
            var data = new NavMeshLinkData
            {
                startPosition = m_Start.position,
                endPosition = m_End.position,
                bidirectional = m_BiDirectional,
                costModifier = m_CostOverride,
                width = 0f,
                area = m_Area,
            };
            m_Link = NavMesh.AddLinkInternal(data, Vector3.zero, Quaternion.identity);
            NavMesh.InternalSetLinkOwner(m_Link, GetInstanceID());
            NavMesh.SetOffMeshConnectionActive(m_Link, m_Activated);
        }

        internal override void OnEngineEnable() => Relink();

        internal override void OnEngineDisable()
        {
            if (m_Link != 0) NavMesh.RemoveLinkInternal(m_Link);
            m_Link = 0;
        }

        internal override void OnEngineDestroy() => OnEngineDisable();

        internal override void ReadSerialized(YNode body, IReferenceResolver resolver)
        {
            var start = body.Ref("m_Start");
            var end = body.Ref("m_End");
            m_Start = start.IsNull ? null : resolver.Resolve(start, typeof(Transform)) as Transform;
            m_End = end.IsNull ? null : resolver.Resolve(end, typeof(Transform)) as Transform;
            m_CostOverride = body.Float("m_CostOverride", -1f);
            m_BiDirectional = body.Bool("m_BiDirectional", true);
            m_Activated = body.Bool("m_Activated", true);
            m_Area = body.Int("m_AreaIndex");
            m_AutoUpdatePositions = body.Bool("m_AutoUpdatePositions");
        }
    }
}

namespace Unbound.Runtime
{
    using System.Collections.Generic;
    using UnityEngine;
    using UnityEngine.AI;

    /// <summary>The enabled obstacles. Carving is not supported yet: a carving obstacle is
    /// reported once and leaves the NavMesh as it is.</summary>
    internal static class ObstacleSystem
    {
        static readonly List<NavMeshObstacle> s_Obstacles = new List<NavMeshObstacle>();
        static bool s_Warned;

        internal static void Add(NavMeshObstacle obstacle)
        {
            if (!s_Obstacles.Contains(obstacle)) s_Obstacles.Add(obstacle);
            Changed(obstacle);
        }

        internal static void Remove(NavMeshObstacle obstacle) => s_Obstacles.Remove(obstacle);

        internal static void Changed(NavMeshObstacle obstacle)
        {
            if (obstacle.carving && obstacle.isActiveAndEnabled && !s_Warned)
            {
                s_Warned = true;
                Debug.LogWarning("Unbound: NavMeshObstacle carving is not supported yet; carving obstacles leave the NavMesh unchanged.", obstacle);
            }
        }
    }
}
