using System.Collections;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Tilemaps;

/// <summary>Sprite masks: what each mask interaction shows, the alpha cutoff, the sorting range
/// a mask affects (its own order, a custom range, across sorting layers), overlapping masks,
/// sorting groups, a mask whose source is the renderer beside it, and the renderers besides sprites
/// that interact with masks (lines, trails, particles, tilemaps).</summary>
public partial class ResProbe
{
    static readonly Color s_Bg = new Color(0.1f, 0.1f, 0.15f, 1f);
    static readonly Color s_Red = new Color(1f, 0.25f, 0.2f, 1f);
    static readonly Color s_Green = new Color(0.3f, 1f, 0.3f, 1f);
    static readonly Color s_Blue = new Color(0.3f, 0.4f, 1f, 1f);
    static readonly Color s_Yellow = new Color(1f, 0.9f, 0.2f, 1f);

    /// <summary>A row of tall squares across a horizontal band mask (y 0..1), with the given
    /// sorting layers and orders, all visible inside (or outside) the mask.</summary>
    void Row((string Layer, int Order)[] sprites, SpriteMaskInteraction interaction)
    {
        float x = -2.1f;
        for (int i = 0; i < sprites.Length; i++, x += 4.2f / (sprites.Length - 1))
            SpriteAt("Square", new Vector3(x, 0f, 0f), Hue(i), sprites[i].Order, sprites[i].Layer, interaction, new Vector3(0.55f, 4f, 1f));
    }

    static Color Hue(int i) => Color.HSVToRGB((i % 7) / 7f, 0.7f, 1f);

    SpriteMask Band(int order = 5, string layer = "Default") => MaskAt("Bar", new Vector3(0f, 0.5f, 0f), order, layer, new Vector3(1.2f, 2f, 1f));

    /// <summary>Two circle masks with custom ranges (left A, right B), a red sprite across the
    /// top at <paramref name="top"/>, a green one across the bottom at <paramref name="bottom"/>.</summary>
    void TwoRanges(string name, int aBack, int aFront, int bBack, int bFront, int top, int bottom, bool bFirst = false, int aOrder = 0, int bOrder = 0)
    {
        var camera = Camera2D(s_Bg);
        SpriteMask a = null, b = null;
        if (bFirst) b = MaskAt("Circle", new Vector3(0.6f, 0f, 0f), bOrder, scale: new Vector3(1.4f, 1.4f, 1f));
        a = MaskAt("Circle", new Vector3(-0.6f, 0f, 0f), aOrder, scale: new Vector3(1.4f, 1.4f, 1f));
        if (!bFirst) b = MaskAt("Circle", new Vector3(0.6f, 0f, 0f), bOrder, scale: new Vector3(1.4f, 1.4f, 1f));
        if (aBack != aFront)
        {
            a.isCustomRangeActive = true;
            a.backSortingOrder = aBack;
            a.frontSortingOrder = aFront;
        }
        if (bBack != bFront)
        {
            b.isCustomRangeActive = true;
            b.backSortingOrder = bBack;
            b.frontSortingOrder = bFront;
        }
        SpriteAt("Square", new Vector3(0f, 1.2f, 0f), s_Red, top, interaction: SpriteMaskInteraction.VisibleInsideMask, scale: new Vector3(5f, 2.4f, 1f));
        SpriteAt("Square", new Vector3(0f, -1.2f, 0f), s_Green, bottom, interaction: SpriteMaskInteraction.VisibleInsideMask, scale: new Vector3(5f, 2.4f, 1f));
        Capture(camera, name);
        Clear();
    }

    /// <summary>Up to three small circle masks side by side (each with its back and front order,
    /// equal for no custom range, and its own order) and up to three bands of sprites visible
    /// inside the masks at the given orders, top to bottom.</summary>
    void Ranges(string name, (int Back, int Front, int Own)[] masks, int[] bands)
    {
        var camera = Camera2D(s_Bg);
        for (int i = 0; i < masks.Length; i++)
        {
            var m = MaskAt("Circle", new Vector3(-1.6f + 1.6f * i, 0f, 0f), masks[i].Own, scale: new Vector3(0.75f, 2.4f, 1f));
            if (masks[i].Back != masks[i].Front)
            {
                m.isCustomRangeActive = true;
                m.frontSortingOrder = masks[i].Front;
                m.backSortingOrder = masks[i].Back;
            }
        }
        var colors = new[] { s_Red, s_Green, s_Blue };
        for (int i = 0; i < bands.Length; i++)
            SpriteAt("Square", new Vector3(0f, 1.6f - 1.6f * i, 0f), colors[i], bands[i], interaction: SpriteMaskInteraction.VisibleInsideMask, scale: new Vector3(5f, 1.5f, 1f));
        Capture(camera, name);
        Clear();
    }

    string MaskState(SpriteMask m) => "[" + m.backSortingLayerID + "," + m.backSortingOrder + "," + m.frontSortingLayerID + "," + m.frontSortingOrder + ","
        + SortingLayer.GetLayerValueFromID(m.backSortingLayerID) + "," + SortingLayer.GetLayerValueFromID(m.frontSortingLayerID) + "," + B(m.isCustomRangeActive) + "]";

    /// <summary>The range setters: the back never sorts after the front (the back's layer, then its
    /// order, are pulled down to the front's).</summary>
    void MaskSetters()
    {
        int back = SortingLayer.NameToID("Back"), front = SortingLayer.NameToID("Front");
        var m = Add(new GameObject("m")).AddComponent<SpriteMask>();
        Value("mask.range.new", MaskState(m));
        Value("mask.cutoff.new", F(m.alphaCutoff));
        Value("mask.source.new", S(m.maskSource.ToString()));
        Value("mask.material.new", m.sharedMaterial == null ? "null" : S(m.sharedMaterial.name + "|" + m.sharedMaterial.shader.name));
        m.backSortingOrder = 3;
        Value("mask.range.0", MaskState(m));
        m.frontSortingOrder = 8; m.backSortingOrder = 3;
        Value("mask.range.1", MaskState(m));
        m.frontSortingOrder = 1;
        Value("mask.range.2", MaskState(m));
        m.backSortingOrder = 10;
        Value("mask.range.3", MaskState(m));
        m.frontSortingOrder = 6;
        m.backSortingOrder = 2;
        m.frontSortingLayerID = back;
        Value("mask.range.4", MaskState(m));
        m.frontSortingLayerID = 0;
        m.backSortingLayerID = front;
        Value("mask.range.5", MaskState(m));
        var n = Add(new GameObject("n")).AddComponent<SpriteMask>();
        n.backSortingLayerID = back;
        n.backSortingOrder = 5;
        Value("mask.range.6", MaskState(n));
        n.backSortingLayerID = front;
        Value("mask.range.7", MaskState(n));
        n.frontSortingLayerID = front;
        n.frontSortingOrder = -3;
        Value("mask.range.8", MaskState(n));
        n.backSortingLayerID = front;
        n.backSortingOrder = -1;
        Value("mask.range.9", MaskState(n));
        n.backSortingOrder = -7;
        Value("mask.range.10", MaskState(n));
        n.isCustomRangeActive = true;
        Value("mask.range.11", MaskState(n));
        Clear();
    }

    /// <summary>A sorting group at order 5 with a custom-range mask (back, front) and bands of
    /// sprites: (in group?, order) from top to bottom.</summary>
    /// <summary>Sorting groups (parent index or -1, order), masks (group or -1, back, front:
    /// equal for no custom range; side by side), bands of sprites (group or -1, order; top to
    /// bottom), the masks made before the sprites or after. Captured a frame later.</summary>
    IEnumerator GroupCase(string name, (int Parent, int Order)[] groups, (int Group, int Back, int Front)[] masks, (int Group, int Order)[] sprites, bool masksLast = false)
    {
        var camera = Camera2D(s_Bg);
        var gs = new Transform[groups.Length];
        for (int i = 0; i < groups.Length; i++)
        {
            var go = Add(new GameObject("group" + i));
            if (groups[i].Parent >= 0) go.transform.SetParent(gs[groups[i].Parent], false);
            go.AddComponent<SortingGroup>().sortingOrder = groups[i].Order;
            gs[i] = go.transform;
        }
        System.Action makeMasks = () =>
        {
            for (int i = 0; i < masks.Length; i++)
            {
                var m = MaskAt("Circle", new Vector3(-1.6f + 3.2f / Mathf.Max(1, masks.Length - 1) * i, 0f, 0f), 0, scale: new Vector3(0.7f, 2.4f, 1f),
                    parent: masks[i].Group >= 0 ? gs[masks[i].Group] : null);
                if (masks[i].Back != masks[i].Front)
                {
                    m.isCustomRangeActive = true;
                    m.frontSortingOrder = masks[i].Front;
                    m.backSortingOrder = masks[i].Back;
                }
            }
        };
        if (!masksLast) makeMasks();
        for (int i = 0; i < sprites.Length; i++)
            SpriteAt("Square", new Vector3(0f, 2f - 4f / Mathf.Max(1, sprites.Length - 1) * i, 0f), Hue(i), sprites[i].Order, interaction: SpriteMaskInteraction.VisibleInsideMask,
                scale: new Vector3(5f, 4f / sprites.Length * 0.9f, 1f), parent: sprites[i].Group >= 0 ? gs[sprites[i].Group] : null);
        if (masksLast) makeMasks();
        // Sorting groups made by a script sort from the next frame (renderers drawn at once sort
        // by their own orders).
        yield return null;
        Capture(camera, name);
        Clear();
    }

    IEnumerator MaskGroups()
    {
        var none = new (int, int)[0];
        var one = new[] { (-1, 5) };
        // Sorting groups: a mask inside a group, sprites inside and outside it.
        {
            var camera = Camera2D(s_Bg);
            var group = Add(new GameObject("group"));
            var sg = group.AddComponent<SortingGroup>();
            sg.sortingOrder = 2;
            MaskAt("Circle", new Vector3(-0.8f, 0f, 0f), 0, scale: new Vector3(1.4f, 1.4f, 1f), parent: group.transform);
            SpriteAt("Square", new Vector3(-0.8f, 1f, 0f), s_Red, 1, interaction: SpriteMaskInteraction.VisibleInsideMask, scale: new Vector3(3f, 2f, 1f), parent: group.transform);
            SpriteAt("Square", new Vector3(-0.8f, -1f, 0f), s_Yellow, -1, interaction: SpriteMaskInteraction.VisibleInsideMask, scale: new Vector3(3f, 2f, 1f), parent: group.transform);
            SpriteAt("Square", new Vector3(0.8f, 1f, 0f), s_Green, 1, interaction: SpriteMaskInteraction.VisibleInsideMask, scale: new Vector3(3f, 2f, 1f));
            SpriteAt("Square", new Vector3(0.8f, -1f, 0f), s_Blue, 5, interaction: SpriteMaskInteraction.VisibleOutsideMask, scale: new Vector3(3f, 2f, 1f));
            yield return null;
            Capture(camera, "mask_sorting_group");
            Clear();
        }
        // A mask outside a group and sprites inside one.
        {
            var camera = Camera2D(s_Bg);
            MaskAt("Circle", Vector3.zero, 0, scale: new Vector3(1.5f, 1.5f, 1f));
            var group = Add(new GameObject("group"));
            group.AddComponent<SortingGroup>().sortingOrder = 3;
            SpriteAt("Square", new Vector3(-1f, 0f, 0f), s_Red, 0, interaction: SpriteMaskInteraction.VisibleInsideMask, scale: new Vector3(2f, 4f, 1f), parent: group.transform);
            SpriteAt("Square", new Vector3(1f, 0f, 0f), s_Green, 0, interaction: SpriteMaskInteraction.VisibleOutsideMask, scale: new Vector3(2f, 4f, 1f), parent: group.transform);
            yield return null;
            Capture(camera, "mask_group_sprites");
            Clear();
        }
        // A custom range inside a sorting group.
        {
            var camera = Camera2D(s_Bg);
            var group = Add(new GameObject("group"));
            group.AddComponent<SortingGroup>().sortingOrder = 5;
            var m = MaskAt("Circle", Vector3.zero, 0, scale: new Vector3(1.5f, 1.5f, 1f), parent: group.transform);
            m.isCustomRangeActive = true;
            m.frontSortingOrder = 2;
            m.backSortingOrder = 0;
            SpriteAt("Square", new Vector3(-1.2f, 1.2f, 0f), s_Red, 1, interaction: SpriteMaskInteraction.VisibleInsideMask, scale: new Vector3(2.4f, 2.4f, 1f), parent: group.transform);
            SpriteAt("Square", new Vector3(1.2f, 1.2f, 0f), s_Yellow, 3, interaction: SpriteMaskInteraction.VisibleInsideMask, scale: new Vector3(2.4f, 2.4f, 1f), parent: group.transform);
            SpriteAt("Square", new Vector3(-1.2f, -1.2f, 0f), s_Green, 1, interaction: SpriteMaskInteraction.VisibleInsideMask, scale: new Vector3(2.4f, 2.4f, 1f));
            SpriteAt("Square", new Vector3(1.2f, -1.2f, 0f), s_Blue, 6, interaction: SpriteMaskInteraction.VisibleInsideMask, scale: new Vector3(2.4f, 2.4f, 1f));
            yield return null;
            Capture(camera, "mask_group_range");
            Clear();
        }
        yield return GroupCase("mask_group_1", none, new[] { (-1, 2, 6) }, new[] { (-1, 1), (-1, 2), (-1, 3), (-1, 6), (-1, 7) });
        yield return GroupCase("mask_group_2", one, new[] { (0, 2, 6) }, new[] { (0, 1), (0, 2), (0, 3), (0, 6), (0, 7) });
        yield return GroupCase("mask_group_3", one, new[] { (0, 2, 6) }, new[] { (0, 1), (0, 2), (0, 3), (0, 6), (0, 7) }, true);
        yield return GroupCase("mask_group_4", one, new[] { (-1, 0, 0) }, new[] { (0, 0), (0, 3), (-1, 0), (-1, 7) });
        yield return GroupCase("mask_group_5", new[] { (-1, 4) }, new[] { (-1, 2, 6) }, new[] { (0, 0), (0, 3), (-1, 3), (-1, 7), (0, 9) });
        yield return GroupCase("mask_group_6", new[] { (-1, 5), (0, 1) }, new[] { (0, 0, 0) }, new[] { (0, 0), (1, 0), (-1, 0), (1, 3) });
        yield return GroupCase("mask_group_7", new[] { (-1, 5), (-1, 6) }, new[] { (0, 0, 0), (1, 0, 0) }, new[] { (0, 0), (1, 0), (-1, 0), (0, 3) });
        yield return GroupCase("mask_group_8", new[] { (-1, 5), (0, 1) }, new[] { (1, 0, 0) }, new[] { (0, 0), (1, 0), (-1, 0), (1, 3) });
        yield return GroupCase("mask_group_9", none, new[] { (-1, 2, 6) }, new[] { (-1, 1), (-1, 2), (-1, 3), (-1, 6), (-1, 7) }, true);
    }

    void Masks()
    {
        // What each interaction shows.
        {
            var camera = Camera2D(s_Bg);
            MaskAt("Circle", Vector3.zero, 0, scale: new Vector3(1.5f, 1.5f, 1f));
            SpriteAt("Square", new Vector3(-1.2f, 0.8f, 0f), s_Red, 1, interaction: SpriteMaskInteraction.VisibleInsideMask, scale: new Vector3(1.6f, 1.6f, 1f));
            SpriteAt("Square", new Vector3(1.2f, 0.8f, 0f), s_Green, 1, interaction: SpriteMaskInteraction.VisibleOutsideMask, scale: new Vector3(1.6f, 1.6f, 1f));
            SpriteAt("Square", new Vector3(0f, -1.2f, 0f), s_Blue, 1, scale: new Vector3(1.6f, 1.6f, 1f));
            Capture(camera, "mask_basic");
            Clear();
        }
        // Alpha cutoff of a soft mask.
        foreach (var cutoff in new[] { 0f, 0.1f, 0.5f, 0.9f })
        {
            var camera = Camera2D(s_Bg);
            var m = MaskAt("Soft", Vector3.zero, 0, scale: new Vector3(2f, 2f, 1f));
            m.alphaCutoff = cutoff;
            SpriteAt("Square", new Vector3(-1f, 0f, 0f), s_Red, 1, interaction: SpriteMaskInteraction.VisibleInsideMask, scale: new Vector3(2f, 4.5f, 1f));
            SpriteAt("Square", new Vector3(1f, 0f, 0f), s_Green, 1, interaction: SpriteMaskInteraction.VisibleOutsideMask, scale: new Vector3(2f, 4.5f, 1f));
            Capture(camera, "mask_cutoff_" + Mathf.RoundToInt(cutoff * 10));
            Clear();
        }
        // The range a mask affects without a custom range: sprites below, at and above its order,
        // on the layers before and after its own.
        var row = new[] { ("Back", 9), ("Default", -3), ("Default", 4), ("Default", 5), ("Default", 6), ("Default", 30), ("Front", -9) };
        foreach (var interaction in new[] { SpriteMaskInteraction.VisibleInsideMask, SpriteMaskInteraction.VisibleOutsideMask })
        {
            var camera = Camera2D(s_Bg);
            Band();
            Row(row, interaction);
            Capture(camera, "mask_range_default_" + (interaction == SpriteMaskInteraction.VisibleInsideMask ? "inside" : "outside"));
            Clear();
        }
        // A custom range within the mask's layer.
        {
            var camera = Camera2D(s_Bg);
            var m = Band();
            m.isCustomRangeActive = true;
            m.frontSortingLayerID = SortingLayer.NameToID("Default");
            m.frontSortingOrder = 8;
            m.backSortingLayerID = SortingLayer.NameToID("Default");
            m.backSortingOrder = 3;
            Value("mask.range.custom", MaskState(m));
            Row(new[] { ("Back", 0), ("Default", 2), ("Default", 3), ("Default", 4), ("Default", 8), ("Default", 9), ("Front", 0) }, SpriteMaskInteraction.VisibleInsideMask);
            Capture(camera, "mask_range_custom");
            Clear();
        }
        // A custom range across layers.
        {
            var camera = Camera2D(s_Bg);
            var m = Band();
            m.isCustomRangeActive = true;
            m.frontSortingLayerID = SortingLayer.NameToID("Front");
            m.frontSortingOrder = 0;
            m.backSortingLayerID = SortingLayer.NameToID("Back");
            m.backSortingOrder = 0;
            Row(new[] { ("Back", -1), ("Back", 0), ("Back", 1), ("Default", 0), ("Front", -1), ("Front", 0), ("Front", 1) }, SpriteMaskInteraction.VisibleInsideMask);
            Capture(camera, "mask_range_layers");
            Clear();
        }
        // A custom range whose back is above its front.
        {
            var camera = Camera2D(s_Bg);
            var m = Band();
            m.isCustomRangeActive = true;
            m.frontSortingLayerID = 0;
            m.frontSortingOrder = 2;
            m.backSortingLayerID = 0;
            m.backSortingOrder = 6;
            Row(new[] { ("Default", 1), ("Default", 2), ("Default", 3), ("Default", 6), ("Default", 7), ("Back", 0), ("Front", 0) }, SpriteMaskInteraction.VisibleInsideMask);
            Capture(camera, "mask_range_inverted");
            Clear();
        }
        // Overlapping masks: the union shows inside, the rest outside.
        {
            var camera = Camera2D(s_Bg);
            MaskAt("Circle", new Vector3(-0.6f, 0.3f, 0f), 0, scale: new Vector3(1.2f, 1.2f, 1f));
            MaskAt("Circle", new Vector3(0.6f, -0.3f, 0f), 0, scale: new Vector3(1.2f, 1.2f, 1f));
            SpriteAt("Square", new Vector3(0f, 1f, 0f), s_Red, 1, interaction: SpriteMaskInteraction.VisibleInsideMask, scale: new Vector3(5f, 2.5f, 1f));
            SpriteAt("Square", new Vector3(0f, -1.25f, 0f), s_Green, 1, interaction: SpriteMaskInteraction.VisibleOutsideMask, scale: new Vector3(5f, 2.5f, 1f));
            Capture(camera, "mask_overlap");
            Clear();
        }
        // Overlapping masks with different ranges.
        {
            var camera = Camera2D(s_Bg);
            var a = MaskAt("Circle", new Vector3(-0.6f, 0f, 0f), 0, scale: new Vector3(1.4f, 1.4f, 1f));
            a.isCustomRangeActive = true;
            a.frontSortingOrder = 10;
            a.backSortingOrder = 0;
            var b = MaskAt("Circle", new Vector3(0.6f, 0f, 0f), 0, scale: new Vector3(1.4f, 1.4f, 1f));
            b.isCustomRangeActive = true;
            b.frontSortingOrder = 10;
            b.backSortingOrder = 5;
            SpriteAt("Square", new Vector3(0f, 1.2f, 0f), s_Red, 3, interaction: SpriteMaskInteraction.VisibleInsideMask, scale: new Vector3(5f, 2.4f, 1f));
            SpriteAt("Square", new Vector3(0f, -1.2f, 0f), s_Green, 7, interaction: SpriteMaskInteraction.VisibleInsideMask, scale: new Vector3(5f, 2.4f, 1f));
            SpriteAt("Square", new Vector3(0f, 0f, 0f), s_Blue, 8, interaction: SpriteMaskInteraction.VisibleOutsideMask, scale: new Vector3(0.6f, 5f, 1f));
            Capture(camera, "mask_overlap_ranges");
            Clear();
        }
        // A disabled mask and a mask without a sprite.
        {
            var camera = Camera2D(s_Bg);
            MaskAt("Circle", new Vector3(-1f, 0f, 0f), 0, scale: new Vector3(1.2f, 1.2f, 1f)).enabled = false;
            MaskAt("Circle", new Vector3(1f, 0f, 0f), 0, scale: new Vector3(1.2f, 1.2f, 1f)).sprite = null;
            SpriteAt("Square", new Vector3(0f, 1f, 0f), s_Red, 1, interaction: SpriteMaskInteraction.VisibleInsideMask, scale: new Vector3(5f, 2f, 1f));
            SpriteAt("Square", new Vector3(0f, -1f, 0f), s_Green, 1, interaction: SpriteMaskInteraction.VisibleOutsideMask, scale: new Vector3(5f, 2f, 1f));
            Capture(camera, "mask_disabled");
            Clear();
        }
        // A mask whose source is the sprite renderer beside it.
        {
            var camera = Camera2D(s_Bg);
            var m = MaskAt("Circle", Vector3.zero, 0, scale: new Vector3(1.5f, 1.5f, 1f));
            m.sprite = null;
            var r = m.gameObject.AddComponent<SpriteRenderer>();
            r.sprite = LoadSprite("Soft");
            r.color = new Color(1f, 1f, 1f, 0.3f);
            r.sortingOrder = -5;
            m.maskSource = SpriteMask.MaskSource.SupportedRenderers;
            Value("mask.source", S(m.maskSource.ToString()));
            SpriteAt("Square", new Vector3(-1f, 0f, 0f), s_Red, 1, interaction: SpriteMaskInteraction.VisibleInsideMask, scale: new Vector3(2f, 4f, 1f));
            SpriteAt("Square", new Vector3(1f, 0f, 0f), s_Green, 1, interaction: SpriteMaskInteraction.VisibleOutsideMask, scale: new Vector3(2f, 4f, 1f));
            Capture(camera, "mask_source_renderer");
            Clear();
        }
        // A target without a depth-stencil buffer.
        {
            var camera = Camera2D(s_Bg, Add(new RenderTexture(Width, Height, 0)));
            MaskAt("Circle", Vector3.zero, 0, scale: new Vector3(1.5f, 1.5f, 1f));
            SpriteAt("Square", new Vector3(-1f, 0f, 0f), s_Red, 1, interaction: SpriteMaskInteraction.VisibleInsideMask, scale: new Vector3(2f, 4f, 1f));
            SpriteAt("Square", new Vector3(1f, 0f, 0f), s_Green, 1, interaction: SpriteMaskInteraction.VisibleOutsideMask, scale: new Vector3(2f, 4f, 1f));
            Capture(camera, "mask_no_stencil");
            Clear();
        }
        // Other renderers with a mask interaction: a line, a trail, particles, a tilemap.
        {
            var camera = Camera2D(s_Bg);
            MaskAt("Circle", Vector3.zero, 0, scale: new Vector3(1.5f, 1.5f, 1f));
            var sprites = Add(new Material(Shader.Find("Sprites/Default")));
            var line = Add(new GameObject("line")).AddComponent<LineRenderer>();
            line.sharedMaterial = sprites;
            line.useWorldSpace = true;
            line.positionCount = 2;
            line.SetPositions(new[] { new Vector3(-2.4f, 1.2f, 0f), new Vector3(2.4f, 1.2f, 0f) });
            line.widthMultiplier = 0.5f;
            line.startColor = line.endColor = s_Red;
            line.alignment = LineAlignment.TransformZ;
            line.maskInteraction = SpriteMaskInteraction.VisibleInsideMask;
            var line2 = Add(new GameObject("line2")).AddComponent<LineRenderer>();
            line2.sharedMaterial = sprites;
            line2.positionCount = 2;
            line2.SetPositions(new[] { new Vector3(-2.4f, 0.4f, 0f), new Vector3(2.4f, 0.4f, 0f) });
            line2.widthMultiplier = 0.5f;
            line2.startColor = line2.endColor = s_Green;
            line2.alignment = LineAlignment.TransformZ;
            line2.maskInteraction = SpriteMaskInteraction.VisibleOutsideMask;
            var trailObject = Add(new GameObject("trail"));
            trailObject.transform.position = new Vector3(-2.4f, -0.4f, 0f);
            var trail = trailObject.AddComponent<TrailRenderer>();
            trail.sharedMaterial = sprites;
            trail.time = 100f;
            trail.widthMultiplier = 0.5f;
            trail.startColor = trail.endColor = s_Blue;
            trail.alignment = LineAlignment.TransformZ;
            trail.maskInteraction = SpriteMaskInteraction.VisibleInsideMask;
            trail.AddPositions(new[] { new Vector3(-2.4f, -0.4f, 0f), new Vector3(0f, -0.4f, 0f), new Vector3(2.4f, -0.4f, 0f) });
            var grid = Add(new GameObject("grid")).AddComponent<Grid>();
            grid.transform.position = new Vector3(-2.5f, -2f, 0f);
            var tilemap = Add(new GameObject("tilemap"));
            tilemap.transform.SetParent(grid.transform, false);
            var map = tilemap.AddComponent<Tilemap>();
            var tr = tilemap.AddComponent<TilemapRenderer>();
            tr.maskInteraction = SpriteMaskInteraction.VisibleInsideMask;
            tr.sortingOrder = 1;
            var tile = Add(ScriptableObject.CreateInstance<Tile>());
            tile.sprite = LoadSprite("Square");
            tile.color = s_Yellow;
            for (int x = 0; x < 5; x++) map.SetTile(new Vector3Int(x, 0, 0), tile);
            Capture(camera, "mask_other_renderers");
            Clear();
        }
        // Particles visible inside the mask.
        {
            var camera = Camera2D(s_Bg);
            MaskAt("Circle", Vector3.zero, 0, scale: new Vector3(1.5f, 1.5f, 1f));
            var go = Add(new GameObject("particles"));
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.startSpeed = 0f;
            main.startSize = 1.4f;
            main.startLifetime = 100f;
            main.startColor = s_Red;
            main.playOnAwake = false;
            var emission = ps.emission;
            emission.enabled = false;
            var shape = ps.shape;
            shape.enabled = false;
            var pr = go.GetComponent<ParticleSystemRenderer>();
            pr.sharedMaterial = Add(new Material(Shader.Find("Sprites/Default")));
            pr.maskInteraction = SpriteMaskInteraction.VisibleInsideMask;
            pr.sortingOrder = 1;
            for (int i = 0; i < 4; i++)
            {
                var p = new ParticleSystem.EmitParams { position = new Vector3(-1.8f + i * 1.2f, 0.6f * (i % 2), 0f), applyShapeToPosition = false };
                ps.Emit(p, 1);
            }
            Capture(camera, "mask_particles");
            Clear();
        }
        // Ranges of one, two and three masks.
        Ranges("mask_ranges_one", new[] { (6, 8, 0) }, new[] { 3, 7, 9 });
        Ranges("mask_ranges_own", new[] { (3, 8, 5) }, new[] { 3, 4, 8 });
        Ranges("mask_ranges_two", new[] { (2, 4, 3), (6, 8, 7) }, new[] { 1, 5, 7 });
        Ranges("mask_ranges_nested", new[] { (0, 10, 0), (2, 4, 0) }, new[] { 3, 7, 11 });
        Ranges("mask_ranges_three", new[] { (0, 2, 0), (4, 6, 0), (8, 10, 0) }, new[] { 1, 5, 9 });
        Ranges("mask_ranges_default", new[] { (6, 8, 0), (0, 0, 20) }, new[] { -30, 7, 30 });
        MaskSetters();
    }
}
