using System;
using System.Collections.Generic;
using Unbound.Runtime;

namespace UnityEngine
{
    /// <summary>A lens flare asset: its texture, how the texture is split into images, and the
    /// elements drawn along the line through the screen's center.</summary>
    [ClassId(121)]
    public sealed partial class Flare : Object, ISerializedAsset
    {
        internal struct Element
        {
            internal int ImageIndex;
            internal float Position, Size;
            internal Color Color;
            internal bool UseLightColor, Rotate, Zoom, Fade;
        }

        internal Texture m_FlareTexture;
        internal int m_TextureLayout;
        internal Element[] m_Elements = Array.Empty<Element>();
        internal bool m_UseFog = true;

        public Flare() { }

        void ISerializedAsset.ReadSerialized(YNode body, IReferenceResolver resolver)
        {
            var texture = body.Ref("m_FlareTexture");
            m_FlareTexture = texture.IsNull ? null : resolver.Resolve(texture, typeof(Texture)) as Texture;
            m_TextureLayout = body.Int("m_TextureLayout");
            m_UseFog = body.Bool("m_UseFog", true);
            var elements = new List<Element>();
            var list = body.Get("m_Elements");
            if (list != null)
                foreach (var e in list.Seq())
                    elements.Add(new Element
                    {
                        ImageIndex = e.Int("m_ImageIndex"),
                        Position = e.Float("m_Position"),
                        Size = e.Float("m_Size"),
                        Color = e.Color("m_Color", Color.white),
                        UseLightColor = e.Bool("m_UseLightColor"),
                        Rotate = e.Bool("m_Rotate"),
                        Zoom = e.Bool("m_Zoom"),
                        Fade = e.Bool("m_Fade"),
                    });
            m_Elements = elements.ToArray();
        }
    }

    /// <summary>Lets a camera show lens flares (the flares themselves are not drawn by Unbound:
    /// they warn once).</summary>
    [ClassId(124)]
    public partial class FlareLayer : Behaviour
    {
        internal FlareLayer() { }
    }

    /// <summary>A lens flare at the object's position, seen by cameras with a FlareLayer.</summary>
    [ClassId(123)]
    public sealed partial class LensFlare : Behaviour
    {
        Flare m_Flare;
        Color m_Color = Color.white;
        float m_Brightness = 1f;
        float m_FadeSpeed = 3f;
        bool m_Directional;

        public LensFlare() { }

        public Flare flare { get => m_Flare; set => m_Flare = value; }
        public Color color { get => m_Color; set => m_Color = value; }
        public float brightness { get => m_Brightness; set => m_Brightness = value; }
        public float fadeSpeed { get => m_FadeSpeed; set => m_FadeSpeed = value; }

        internal override void OnEngineEnable() => Halos.WarnFlare();

        internal override void CopyFrom(Component source, Func<Object, Object> remap)
        {
            var o = (LensFlare)source;
            m_Flare = remap(o.m_Flare) as Flare;
            m_Color = o.m_Color;
            m_Brightness = o.m_Brightness;
            m_FadeSpeed = o.m_FadeSpeed;
            m_Directional = o.m_Directional;
        }

        internal override void ReadSerialized(YNode body, IReferenceResolver resolver)
        {
            var flare = body.Ref("m_Flare");
            m_Flare = flare.IsNull ? null : resolver.Resolve(flare, typeof(Flare)) as Flare;
            m_Color = body.Color("m_Color", Color.white);
            m_Brightness = body.Float("m_Brightness", 1f);
            m_FadeSpeed = body.Float("m_FadeSpeed", 3f);
            m_Directional = body.Bool("m_Directional");
        }
    }

    /// <summary>A halo around the object (an internal class in Unity: scenes and AddComponent by
    /// name make it).</summary>
    [ClassId(122)]
    internal sealed partial class Halo : Behaviour
    {
        internal Color m_Color = new Color(0.5f, 0.5f, 0.5f, 0.5f);
        internal float m_Size = 5f;

        public Halo() { }

        internal override void OnEngineEnable() => Halos.WarnHalo();

        internal override void CopyFrom(Component source, Func<Object, Object> remap)
        {
            var o = (Halo)source;
            m_Color = o.m_Color;
            m_Size = o.m_Size;
        }

        internal override void ReadSerialized(YNode body, IReferenceResolver resolver)
        {
            m_Color = body.Color("m_Color", m_Color);
            m_Size = body.Float("m_Size", 5f);
        }
    }

    /// <summary>An area occlusion culling bakes for (data only: Unbound bakes no occlusion data,
    /// so cameras cull by their frustums alone, as the Editor does without baked data).</summary>
    [ClassId(192)]
    public sealed partial class OcclusionArea : Component
    {
        Vector3 m_Center;
        Vector3 m_Size = Vector3.one;

        public OcclusionArea() { }

        public Vector3 center { get => m_Center; set => m_Center = value; }
        public Vector3 size { get => m_Size; set => m_Size = new Vector3(Mathf.Abs(value.x), Mathf.Abs(value.y), Mathf.Abs(value.z)); }

        internal override void CopyFrom(Component source, Func<Object, Object> remap)
        {
            var o = (OcclusionArea)source;
            m_Center = o.m_Center;
            m_Size = o.m_Size;
        }

        internal override void ReadSerialized(YNode body, IReferenceResolver resolver)
        {
            m_Center = body.Vector3("m_Center");
            m_Size = body.Get("m_Size") != null ? body.Vector3("m_Size") : Vector3.one;
        }
    }

    /// <summary>A portal of baked occlusion data that games open and close (with no baked data,
    /// as here, it culls nothing either way).</summary>
    [ClassId(41)]
    public sealed partial class OcclusionPortal : Component
    {
        bool m_Open = true;

        public OcclusionPortal() { }

        public bool open { get => m_Open; set => m_Open = value; }

        internal override void CopyFrom(Component source, Func<Object, Object> remap) => m_Open = ((OcclusionPortal)source).m_Open;

        internal override void ReadSerialized(YNode body, IReferenceResolver resolver) => m_Open = body.Bool("m_Open", true);
    }
}

namespace Unbound.Runtime
{
    /// <summary>Halos and lens flares: Unity draws them with textures and meshes its native code
    /// builds (the halo falloff is not among its shipped resources), so Unbound does not draw them
    /// yet; they warn once when one is enabled.</summary>
    internal static class Halos
    {
        internal static void WarnHalo() => Stubs.Ignored("Halo", "halos are not drawn: Unity builds their falloff texture in native code");

        internal static void WarnFlare() => Stubs.Ignored("LensFlare", "lens flares are not drawn yet");
    }
}
