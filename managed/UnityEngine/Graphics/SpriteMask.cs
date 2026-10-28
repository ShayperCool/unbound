using System;
using Unbound.Runtime;

namespace UnityEngine
{
    /// <summary>
    /// A sprite mask: its data and API. The masking itself (the stencil the mask writes and the
    /// renderers' mask interaction test) is not drawn yet: masked renderers draw unmasked, and the
    /// first mask of a run says so.
    /// </summary>
    [ClassId(331)]
    public sealed partial class SpriteMask : Renderer
    {
        Sprite m_Sprite;
        float m_MaskAlphaCutoff = 0.2f;
        bool m_IsCustomRangeActive;
        int m_FrontSortingLayerID;
        int m_FrontSortingOrder;
        int m_BackSortingLayerID;
        int m_BackSortingOrder;
        SpriteSortPoint m_SpriteSortPoint;
        static bool s_Warned;

        public SpriteMask() { }

        public Sprite sprite { get => m_Sprite; set => m_Sprite = value; }
        public float alphaCutoff { get => m_MaskAlphaCutoff; set => m_MaskAlphaCutoff = value; }
        public bool isCustomRangeActive { get => m_IsCustomRangeActive; set => m_IsCustomRangeActive = value; }
        public int frontSortingLayerID { get => m_FrontSortingLayerID; set => m_FrontSortingLayerID = value; }
        public int frontSortingOrder { get => m_FrontSortingOrder; set => m_FrontSortingOrder = value; }
        public int backSortingLayerID { get => m_BackSortingLayerID; set => m_BackSortingLayerID = value; }
        public int backSortingOrder { get => m_BackSortingOrder; set => m_BackSortingOrder = value; }
        public SpriteSortPoint spriteSortPoint { get => m_SpriteSortPoint; set => m_SpriteSortPoint = value; }

        internal override Bounds GetLocalBounds() => m_Sprite != null ? m_Sprite.bounds : new Bounds(Vector3.zero, Vector3.zero);

        internal override void CollectDraws(FrameBuilder.CameraContext camera, System.Collections.Generic.List<FrameBuilder.DrawItem> into)
        {
            if (s_Warned || m_Sprite == null) return;
            s_Warned = true;
            Debug.LogWarning("Unbound: SpriteMask masking is not drawn yet; masked sprites draw unmasked.");
        }

        internal override void ReadSerialized(YNode body, IReferenceResolver resolver)
        {
            ReadRendererData(body, resolver);
            var r = body.Ref("m_Sprite");
            m_Sprite = r.IsNull ? null : resolver.Resolve(r, typeof(Sprite)) as Sprite;
            m_MaskAlphaCutoff = body.Float("m_MaskAlphaCutoff", 0.2f);
            m_IsCustomRangeActive = body.Bool("m_IsCustomRangeActive");
            m_FrontSortingLayerID = body.Int("m_FrontSortingLayerID");
            m_FrontSortingOrder = body.Int("m_FrontSortingOrder");
            m_BackSortingLayerID = body.Int("m_BackSortingLayerID");
            m_BackSortingOrder = body.Int("m_BackSortingOrder");
            m_SpriteSortPoint = (SpriteSortPoint)body.Int("m_SpriteSortPoint");
        }

        internal override void CopyFrom(Component source, Func<Object, Object> remap)
        {
            var s = (SpriteMask)source;
            CopyRendererData(s);
            m_Sprite = s.m_Sprite;
            m_MaskAlphaCutoff = s.m_MaskAlphaCutoff;
            m_IsCustomRangeActive = s.m_IsCustomRangeActive;
            m_FrontSortingLayerID = s.m_FrontSortingLayerID;
            m_FrontSortingOrder = s.m_FrontSortingOrder;
            m_BackSortingLayerID = s.m_BackSortingLayerID;
            m_BackSortingOrder = s.m_BackSortingOrder;
            m_SpriteSortPoint = s.m_SpriteSortPoint;
        }
    }
}
