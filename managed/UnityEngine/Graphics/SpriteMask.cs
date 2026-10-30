using System;
using System.Collections.Generic;
using Unbound.Runtime;
using UnityEngine.Rendering;

namespace UnityEngine
{
    /// <summary>
    /// A sprite mask: it draws nothing itself; renderers with a mask interaction are drawn through
    /// the stencil the masks covering them write (<see cref="SpriteMasking"/>).
    /// </summary>
    [ClassId(331)]
    public sealed partial class SpriteMask : Renderer
    {
        internal Sprite m_Sprite;
        internal float m_MaskAlphaCutoff = 0.2f;
        internal bool m_IsCustomRangeActive;
        internal int m_FrontSortingLayerID;
        internal int m_FrontSortingOrder;
        internal int m_BackSortingLayerID;
        internal int m_BackSortingOrder;
        SpriteSortPoint m_SpriteSortPoint;
        internal MaskSource m_MaskSource;

        public SpriteMask() { }

        public enum MaskSource
        {
            Sprite = 0,
            SupportedRenderers = 1,
        }

        /// <summary>A mask added by a script gets the built-in Sprites-Mask material (measured).</summary>
        internal override void OnAttached()
        {
            base.OnAttached();
            var material = BuiltinObjects.SpritesMask;
            if (material != null) m_Materials = new[] { material };
        }

        public Sprite sprite { get => m_Sprite; set => m_Sprite = value; }
        public float alphaCutoff { get => m_MaskAlphaCutoff; set => m_MaskAlphaCutoff = value; }
        public bool isCustomRangeActive { get => m_IsCustomRangeActive; set => m_IsCustomRangeActive = value; }
        public MaskSource maskSource { get => m_MaskSource; set => m_MaskSource = value; }

        /// <summary>The renderer a SupportedRenderers mask takes its shape from: the sprite renderer
        /// beside it.</summary>
        internal Renderer cachedSupportedRenderer => m_GameObject != null ? m_GameObject.GetComponent<SpriteRenderer>() : null;

        // The range setters keep the back from sorting after the front: the back's layer, then its
        // order, are pulled down to the front's (measured: tests/projects/resources mask.range.*).
        public int frontSortingLayerID
        {
            get => m_FrontSortingLayerID;
            set
            {
                m_FrontSortingLayerID = value;
                ClampBack();
            }
        }

        public int frontSortingOrder
        {
            get => m_FrontSortingOrder;
            set
            {
                m_FrontSortingOrder = value;
                ClampBack();
            }
        }

        public int backSortingLayerID
        {
            get => m_BackSortingLayerID;
            set
            {
                m_BackSortingLayerID = value;
                ClampBack();
            }
        }

        public int backSortingOrder
        {
            get => m_BackSortingOrder;
            set
            {
                m_BackSortingOrder = value;
                ClampBack();
            }
        }

        void ClampBack()
        {
            int back = SortingLayer.GetLayerValueFromID(m_BackSortingLayerID), front = SortingLayer.GetLayerValueFromID(m_FrontSortingLayerID);
            if (back > front)
            {
                m_BackSortingLayerID = m_FrontSortingLayerID;
                back = front;
            }
            if (back == front && m_BackSortingOrder > m_FrontSortingOrder) m_BackSortingOrder = m_FrontSortingOrder;
        }

        public SpriteSortPoint spriteSortPoint { get => m_SpriteSortPoint; set => m_SpriteSortPoint = value; }

        /// <summary>The shape the mask writes: its sprite's mesh, or the sprite renderer's beside it.</summary>
        internal bool Shape(out Mesh mesh, out Matrix4x4 model, out Texture texture)
        {
            if (m_MaskSource == MaskSource.SupportedRenderers)
            {
                var source = cachedSupportedRenderer as SpriteRenderer;
                if (source != null && source.MaskShape(out mesh, out model, out texture)) return true;
            }
            else if (m_Sprite != null && m_Sprite.texture != null)
            {
                mesh = m_Sprite.RenderMesh();
                model = transform.localToWorldMatrix;
                texture = m_Sprite.texture;
                return true;
            }
            mesh = null;
            model = default;
            texture = null;
            return false;
        }

        internal override Bounds GetLocalBounds() => m_Sprite != null ? m_Sprite.bounds : new Bounds(Vector3.zero, Vector3.zero);

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
            m_MaskSource = (MaskSource)body.Int("m_MaskSource");
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
            m_MaskSource = s.m_MaskSource;
        }
    }
}

namespace UnityEngine
{
    /// <summary>What the 2D renderer asks about the masks (whether a layer batch needs the stencil).</summary>
    internal static class SpriteMaskUtility
    {
        /// <summary>Whether an enabled mask lies in (or, with a custom range, reaches into) a
        /// sorting layer range.</summary>
        internal static bool HasSpriteMaskInLayerRange(UnityEngine.Rendering.SortingLayerRange range)
        {
            foreach (var renderer in RenderRegistry.Renderers)
            {
                if (!(renderer is SpriteMask mask) || !mask || !mask.m_Enabled || !mask.gameObject.activeInHierarchy) continue;
                int low, high;
                if (mask.m_IsCustomRangeActive)
                {
                    low = SortingLayer.GetLayerValueFromID(mask.m_BackSortingLayerID);
                    high = SortingLayer.GetLayerValueFromID(mask.m_FrontSortingLayerID);
                }
                else low = high = SortingLayer.GetLayerValueFromID(mask.m_SortingLayerID);
                if (high >= range.lowerBound && low <= range.upperBound) return true;
            }
            return false;
        }
    }
}

namespace Unbound.Runtime
{
    using UnityEngine;

    /// <summary>
    /// Sprite masks as Unity draws them (measured: tests/projects/resources mask_*): a mask writes the
    /// stencil over the stretch of the draw order its range covers, within its sorting group (none:
    /// every draw; its innermost group: that group's draws, nested groups included). Without a
    /// custom range it covers the whole stretch; with one, the draws after its back and up to its
    /// front (at the root a draw tying with the back or the front comes before the mask's own draw,
    /// in a group the order the renderers were turned on decides). A renderer with a mask
    /// interaction is drawn inside (stencil 1 or more) or outside (0) the masks covering its place.
    /// Before such a draw the stencil is brought to exactly those masks: masks that left are drawn
    /// again decrementing it, masks that joined incrementing it (with the mask material, its alpha
    /// cutoff discarding pixels, nothing written but the stencil).
    /// </summary>
    internal static class SpriteMasking
    {
        sealed class MaskInfo
        {
            internal SpriteMask Mask;
            internal FrameBuilder.SortLevel[] Groups;
            internal float Distance;
            internal int Index;
            internal bool Custom;
            internal int BackLayer, BackOrder, FrontLayer, FrontOrder;
        }

        static Camera s_Camera;
        static FrameBuilder.CameraContext s_Context;
        static List<MaskInfo> s_Masks;
        /// <summary>The masks in each target's stencil.</summary>
        static readonly Dictionary<(ulong, ulong), List<SpriteMask>> s_Stencil = new Dictionary<(ulong, ulong), List<SpriteMask>>();
        static readonly List<SpriteMask> s_Wanted = new List<SpriteMask>();
        static readonly int s_CutoffId = Shader.PropertyToID("_Cutoff");

        /// <summary>A camera (or a renderer list of it) starts drawing: the masks are looked up again.</summary>
        internal static void Begin(Camera camera, FrameBuilder.CameraContext context)
        {
            s_Camera = camera;
            s_Context = context;
            s_Masks = null;
        }

        internal static void NewFrame()
        {
            s_Stencil.Clear();
            s_Masks = null;
        }

        /// <summary>A target's depth and stencil were cleared.</summary>
        internal static void Cleared(ulong color, ulong depth) => s_Stencil.Remove((color, depth));

        static List<MaskInfo> Masks()
        {
            if (s_Masks != null) return s_Masks;
            s_Masks = new List<MaskInfo>();
            int cullingMask = s_Camera != null ? s_Camera.cullingMask : -1;
            foreach (var renderer in RenderRegistry.Renderers)
            {
                if (!(renderer is SpriteMask mask) || !mask || !mask.m_Enabled || !mask.gameObject.activeInHierarchy) continue;
                if ((cullingMask & (1 << mask.gameObject.layer)) == 0) continue;
                s_Masks.Add(new MaskInfo
                {
                    Mask = mask,
                    Groups = s_Context != null ? FrameBuilder.GroupsOf(mask, s_Context) : null,
                    Distance = s_Context != null ? FrameBuilder.Distance(s_Context, mask.transform.position) : 0f,
                    Index = RenderRegistry.Renderers.IndexOf(mask),
                    Custom = mask.m_IsCustomRangeActive,
                    BackLayer = SortingLayer.GetLayerValueFromID(mask.m_BackSortingLayerID),
                    BackOrder = mask.m_BackSortingOrder,
                    FrontLayer = SortingLayer.GetLayerValueFromID(mask.m_FrontSortingLayerID),
                    FrontOrder = mask.m_FrontSortingOrder,
                });
            }
            return s_Masks;
        }

        /// <summary>Whether a draw (its sort key at the mask's level) comes after a boundary of the
        /// mask's range.</summary>
        static bool After(int layer, int order, float distance, int index, int boundLayer, int boundOrder, MaskInfo mask, bool root)
        {
            if (layer != boundLayer) return layer > boundLayer;
            if (order != boundOrder) return order > boundOrder;
            if (root) return false;
            if (distance != mask.Distance) return distance < mask.Distance;
            return index > mask.Index;
        }

        static bool Covers(MaskInfo mask, in FrameBuilder.DrawItem item, Renderer renderer)
        {
            int depth = mask.Groups?.Length ?? 0;
            var groups = item.Groups;
            if ((groups?.Length ?? 0) < depth) return false;
            for (int i = 0; i < depth; i++)
                if (groups[i].Id != mask.Groups[i].Id) return false;
            if (!mask.Custom) return true;
            int layer, order, index;
            float distance;
            if (groups != null && groups.Length > depth)
            {
                // A nested group takes part as one unit.
                layer = groups[depth].SortingLayer;
                order = groups[depth].SortingOrder;
                distance = groups[depth].Distance;
                index = int.MaxValue;
            }
            else
            {
                layer = item.SortingLayer;
                order = item.SortingOrder;
                distance = item.Distance;
                index = RenderRegistry.Renderers.IndexOf(renderer);
            }
            bool root = depth == 0;
            return After(layer, order, distance, index, mask.BackLayer, mask.BackOrder, mask, root)
                && !After(layer, order, distance, index, mask.FrontLayer, mask.FrontOrder, mask, root);
        }

        /// <summary>Before a draw of a renderer with a mask interaction: brings the stencil to the
        /// masks covering it and sets the stencil test (over the current state override) to draw it
        /// with; returns the override to put back after.</summary>
        internal static RenderStateBlock? Before(in FrameBuilder.DrawItem item, Renderer renderer)
        {
            var interaction = renderer.MaskInteraction;
            s_Wanted.Clear();
            foreach (var mask in Masks())
                if (mask.Mask && Covers(mask, item, renderer)) s_Wanted.Add(mask.Mask);
            var state = GfxStream.Current;
            var key = (state.Color, state.Depth);
            if (!s_Stencil.TryGetValue(key, out var current)) s_Stencil[key] = current = new List<SpriteMask>();
            var previous = GfxStream.CurrentOverride;
            for (int i = current.Count - 1; i >= 0; i--)
            {
                if (s_Wanted.Contains(current[i])) continue;
                DrawMask(current[i], StencilOp.DecrementSaturate);
                current.RemoveAt(i);
            }
            foreach (var mask in s_Wanted)
            {
                if (current.Contains(mask)) continue;
                DrawMask(mask, StencilOp.IncrementSaturate);
                current.Add(mask);
            }
            var block = previous ?? new RenderStateBlock(RenderStateMask.Nothing);
            block.mask |= RenderStateMask.Stencil;
            block.stencilReference = 1;
            block.stencilState = new StencilState(true, 255, 0,
                interaction == SpriteMaskInteraction.VisibleInsideMask ? CompareFunction.LessEqual : CompareFunction.Greater,
                StencilOp.Keep, StencilOp.Keep, StencilOp.Keep);
            GfxStream.StateOverride(block);
            return previous;
        }
        /// <summary>After the draw: the state override before it.</summary>
        internal static void After(RenderStateBlock? previous) => GfxStream.StateOverride(previous);

        static readonly Dictionary<int, object> s_MaskProperties = new Dictionary<int, object>();

        static bool DrawMask(SpriteMask mask, StencilOp op)
        {
            if (!mask || !mask.Shape(out var mesh, out var model, out var texture)) return false;
            var material = mask.sharedMaterial != null ? mask.sharedMaterial : BuiltinObjects.SpritesMask;
            if (material == null) return false;
            ulong meshHandle = mesh.Sync();
            ulong materialHandle = material.Sync();
            ulong textureHandle = TextureHandles.Of(texture);
            GfxStream.StateOverride(new RenderStateBlock(RenderStateMask.Stencil)
            {
                stencilReference = 1,
                stencilState = new StencilState(true, 255, 255, CompareFunction.Always, op, StencilOp.Keep, StencilOp.Keep),
            });
            s_MaskProperties.Clear();
            s_MaskProperties[s_CutoffId] = mask.m_MaskAlphaCutoff;
            GfxStream.Draw(meshHandle, 0, materialHandle, 0, model, Color.white, textureHandle, null, s_MaskProperties);
            return true;
        }
    }
}
