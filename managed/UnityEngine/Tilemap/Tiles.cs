using System;
using Unity.Collections;
using Unbound.Runtime;

namespace UnityEngine.Tilemaps
{
    /// <summary>A tile: answers the tilemap's questions about what to show at a position.</summary>
    public abstract partial class TileBase : ScriptableObject
    {
        protected TileBase() { }

        public virtual void RefreshTile(Vector3Int position, ITilemap tilemap) => tilemap.RefreshTile(position);

        public virtual void GetTileData(Vector3Int position, ITilemap tilemap, ref TileData tileData) { }

        public virtual bool GetTileAnimationData(Vector3Int position, ITilemap tilemap, ref TileAnimationData tileAnimationData) => false;

        public virtual bool StartUp(Vector3Int position, ITilemap tilemap, GameObject go) => false;
    }

    /// <summary>The default tile: a sprite with a color, a transform, flags and a collider type.</summary>
    [Serializable]
    [HelpURL("https://docs.unity3d.com/Manual/Tilemap-TileAsset.html")]
    public partial class Tile : TileBase
    {
        [SerializeField] Sprite m_Sprite;
        [SerializeField] Color m_Color = Color.white;
        [SerializeField] Matrix4x4 m_Transform = Matrix4x4.identity;
        [SerializeField] GameObject m_InstancedGameObject;
        [SerializeField] TileFlags m_Flags = TileFlags.LockColor;
        [SerializeField] ColliderType m_ColliderType = ColliderType.Sprite;

        public Tile() { }

        public Sprite sprite { get => m_Sprite; set => m_Sprite = value; }
        public Color color { get => m_Color; set => m_Color = value; }
        public Matrix4x4 transform { get => m_Transform; set => m_Transform = value; }
        public GameObject gameObject { get => m_InstancedGameObject; set => m_InstancedGameObject = value; }
        public TileFlags flags { get => m_Flags; set => m_Flags = value; }
        public ColliderType colliderType { get => m_ColliderType; set => m_ColliderType = value; }

        public override void GetTileData(Vector3Int position, ITilemap tilemap, ref TileData tileData)
        {
            tileData.sprite = m_Sprite;
            tileData.color = m_Color;
            tileData.transform = m_Transform;
            tileData.gameObject = m_InstancedGameObject;
            tileData.flags = m_Flags;
            tileData.colliderType = m_ColliderType;
        }

        public enum ColliderType
        {
            None = 0,
            Sprite = 1,
            Grid = 2,
        }
    }

    /// <summary>The tilemap as tiles see it. While a tilemap collects the positions tiles ask to
    /// refresh (after tiles change), <see cref="RefreshTile"/> adds to that list instead of
    /// refreshing at once.</summary>
    public partial class ITilemap
    {
        internal Tilemap m_Tilemap;
        internal System.Collections.Generic.SortedSet<Vector3Int> m_Collect;

        internal ITilemap() { }

        public ITilemap(Tilemap tilemap)
        {
            if (tilemap == null) throw new ArgumentNullException("Argument tilemap cannot be null");
            m_Tilemap = tilemap;
        }

        public static implicit operator ITilemap(Tilemap tilemap) => new ITilemap(tilemap);

        public Vector3Int origin => m_Tilemap.origin;
        public Vector3Int size => m_Tilemap.size;
        public Bounds localBounds => m_Tilemap.localBounds;
        public BoundsInt cellBounds => m_Tilemap.cellBounds;

        public virtual Sprite GetSprite(Vector3Int position) => m_Tilemap.GetSprite(position);
        public virtual Color GetColor(Vector3Int position) => m_Tilemap.GetColor(position);
        public virtual Matrix4x4 GetTransformMatrix(Vector3Int position) => m_Tilemap.GetTransformMatrix(position);
        public virtual TileFlags GetTileFlags(Vector3Int position) => m_Tilemap.GetTileFlags(position);
        public virtual TileBase GetTile(Vector3Int position) => m_Tilemap.GetTile(position);
        public virtual T GetTile<T>(Vector3Int position) where T : TileBase => m_Tilemap.GetTile<T>(position);

        public void RefreshTile(Vector3Int position)
        {
            if (m_Collect != null) m_Collect.Add(position);
            else m_Tilemap.RefreshTile(position);
        }

        public T GetComponent<T>()
        {
            if (typeof(T) == typeof(Tilemap)) return (T)(object)m_Tilemap;
            return m_Tilemap.GetComponent<T>();
        }
    }

    /// <summary>What a tile tells the tilemap about a position. Objects are held by instance id, as
    /// in Unity (the struct is blittable).</summary>
    public partial struct TileData
    {
        int m_Sprite;
        Color m_Color;
        Matrix4x4 m_Transform;
        int m_GameObject;
        TileFlags m_Flags;
        Tile.ColliderType m_ColliderType;

        /// <summary>What a tile's GetTileData starts from: no sprite, white, identity, no flags.</summary>
        internal static readonly TileData Default = new TileData { m_Color = Color.white, m_Transform = Matrix4x4.identity };

        public Sprite sprite
        {
            get => ObjectRegistry.FindById(m_Sprite) as Sprite;
            set => m_Sprite = value != null ? value.GetInstanceID() : 0;
        }

        public Color color { get => m_Color; set => m_Color = value; }
        public Matrix4x4 transform { get => m_Transform; set => m_Transform = value; }

        public GameObject gameObject
        {
            get => ObjectRegistry.FindById(m_GameObject) as GameObject;
            set => m_GameObject = value != null ? value.GetInstanceID() : 0;
        }

        public TileFlags flags { get => m_Flags; set => m_Flags = value; }
        public Tile.ColliderType colliderType { get => m_ColliderType; set => m_ColliderType = value; }
    }

    [Serializable]
    public partial struct TileChangeData
    {
        [SerializeField] Vector3Int m_Position;
        [SerializeField] Object m_TileAsset;
        [SerializeField] Color m_Color;
        [SerializeField] Matrix4x4 m_Transform;

        public TileChangeData(Vector3Int position, TileBase tile, Color color, Matrix4x4 transform)
        {
            m_Position = position;
            m_TileAsset = tile;
            m_Color = color;
            m_Transform = transform;
        }

        public Vector3Int position { get => m_Position; set => m_Position = value; }
        public TileBase tile { get => (TileBase)m_TileAsset; set => m_TileAsset = value; }
        public Color color { get => m_Color; set => m_Color = value; }
        public Matrix4x4 transform { get => m_Transform; set => m_Transform = value; }
    }

    public partial struct TileAnimationData
    {
        Sprite[] m_AnimatedSprites;
        float m_AnimationSpeed;
        float m_AnimationStartTime;
        TileAnimationFlags m_Flags;

        public Sprite[] animatedSprites { get => m_AnimatedSprites; set => m_AnimatedSprites = value; }
        public float animationSpeed { get => m_AnimationSpeed; set => m_AnimationSpeed = value; }
        public float animationStartTime { get => m_AnimationStartTime; set => m_AnimationStartTime = value; }
        public TileAnimationFlags flags { get => m_Flags; set => m_Flags = value; }
    }

    [Flags]
    public enum TileFlags
    {
        None = 0,
        LockColor = 1,
        LockTransform = 2,
        InstantiateGameObjectRuntimeOnly = 4,
        KeepGameObjectRuntimeOnly = 8,
        LockAll = LockColor | LockTransform,
    }

    [Flags]
    public enum TileAnimationFlags
    {
        None = 0,
        LoopOnce = 1,
        PauseAnimation = 2,
        UpdatePhysics = 4,
        UnscaledTime = 8,
        SyncAnimation = 16,
    }
}
