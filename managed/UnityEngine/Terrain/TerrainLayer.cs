using Unbound.Runtime;

namespace UnityEngine
{
    /// <summary>A terrain's paint layer (a `.terrainlayer` asset): its textures and how they tile.</summary>
    [ClassId(1953259897)]
    public sealed partial class TerrainLayer : Object, ISerializedAsset
    {
        Texture2D m_DiffuseTexture, m_NormalMapTexture, m_MaskMapTexture;
        Vector2 m_TileSize = new Vector2(15f, 15f);
        Vector2 m_TileOffset;
        Color m_Specular = new Color(0f, 0f, 0f, 0f);
        float m_Metallic, m_Smoothness, m_NormalScale = 1f;
        Vector4 m_DiffuseRemapMin, m_DiffuseRemapMax = Vector4.one, m_MaskMapRemapMin, m_MaskMapRemapMax = Vector4.one;
        TerrainLayerSmoothnessSource m_SmoothnessSource = TerrainLayerSmoothnessSource.DiffuseAlphaChannel;

        public TerrainLayer() { }

        public Texture2D diffuseTexture { get => m_DiffuseTexture; set => m_DiffuseTexture = value; }
        public Texture2D normalMapTexture { get => m_NormalMapTexture; set => m_NormalMapTexture = value; }
        public Texture2D maskMapTexture { get => m_MaskMapTexture; set => m_MaskMapTexture = value; }
        public Vector2 tileSize { get => m_TileSize; set => m_TileSize = value; }
        public Vector2 tileOffset { get => m_TileOffset; set => m_TileOffset = value; }
        public Color specular { get => m_Specular; set => m_Specular = value; }
        public float metallic { get => m_Metallic; set => m_Metallic = value; }
        public float smoothness { get => m_Smoothness; set => m_Smoothness = value; }
        public float normalScale { get => m_NormalScale; set => m_NormalScale = value; }
        public Vector4 diffuseRemapMin { get => m_DiffuseRemapMin; set => m_DiffuseRemapMin = value; }
        public Vector4 diffuseRemapMax { get => m_DiffuseRemapMax; set => m_DiffuseRemapMax = value; }
        public Vector4 maskMapRemapMin { get => m_MaskMapRemapMin; set => m_MaskMapRemapMin = value; }
        public Vector4 maskMapRemapMax { get => m_MaskMapRemapMax; set => m_MaskMapRemapMax = value; }
        public TerrainLayerSmoothnessSource smoothnessSource { get => m_SmoothnessSource; set => m_SmoothnessSource = value; }

        void ISerializedAsset.ReadSerialized(YNode body, IReferenceResolver resolver)
        {
            Texture2D Texture(string key)
            {
                var r = body.Ref(key);
                return r.IsNull ? null : resolver.Resolve(r, typeof(Texture2D)) as Texture2D;
            }
            m_DiffuseTexture = Texture("m_DiffuseTexture");
            m_NormalMapTexture = Texture("m_NormalMapTexture");
            m_MaskMapTexture = Texture("m_MaskMapTexture");
            m_TileSize = body.Vector2("m_TileSize");
            m_TileOffset = body.Vector2("m_TileOffset");
            m_Specular = body.Color("m_Specular", new Color(0f, 0f, 0f, 0f));
            m_Metallic = body.Float("m_Metallic");
            m_Smoothness = body.Float("m_Smoothness");
            m_NormalScale = body.Float("m_NormalScale", 1f);
            m_DiffuseRemapMin = body.Vector4("m_DiffuseRemapMin");
            m_DiffuseRemapMax = body.Get("m_DiffuseRemapMax") != null ? body.Vector4("m_DiffuseRemapMax") : Vector4.one;
            m_MaskMapRemapMin = body.Vector4("m_MaskMapRemapMin");
            m_MaskMapRemapMax = body.Get("m_MaskMapRemapMax") != null ? body.Vector4("m_MaskMapRemapMax") : Vector4.one;
            m_SmoothnessSource = (TerrainLayerSmoothnessSource)body.Int("m_SmoothnessSource", 1);
        }
    }
}
