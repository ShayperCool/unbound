using UnityEngine;

/// <summary>Sprite secondary textures: an imported sprite's (from its importer's list), a sprite made
/// by script with and without them, drawn with a shader that shows _MaskTex and _NormalMap.</summary>
public partial class ResProbe
{
    string Secondary(Sprite sprite)
    {
        var textures = new SecondarySpriteTexture[sprite.GetSecondaryTextureCount() + 1];
        int n = sprite.GetSecondaryTextures(textures);
        var sb = new System.Text.StringBuilder("[" + I(sprite.GetSecondaryTextureCount()) + "," + I(n));
        for (int i = 0; i < n; i++) sb.Append(",").Append(S(textures[i].name)).Append(",").Append(N(textures[i].texture));
        var small = new SecondarySpriteTexture[1];
        sb.Append(",").Append(I(sprite.GetSecondaryTextures(small)));
        return sb.Append("]").ToString();
    }

    /// <summary>The default textures' texels (what an unset texture property samples).</summary>
    void DefaultTextures()
    {
        var rt = Add(new RenderTexture(8, 1, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear));
        Graphics.Blit(null, rt, Add(new Material(Shader.Find("Probe/Defaults"))));
        var t = Read(rt);
        var sb = new System.Text.StringBuilder("[");
        for (int i = 0; i < 8; i++)
        {
            Color32 c = t.GetPixel(i, 0);
            sb.Append(i == 0 ? "" : ",").Append("[" + c.r + "," + c.g + "," + c.b + "," + c.a + "]");
        }
        DestroyImmediate(t);
        Value("defaults.texels", sb.Append("]").ToString());
    }

    /// <summary>The normal and tangent a sprite renderer gives its vertices (simple, sliced, flipped).</summary>
    void SpriteAttributes()
    {
        // Three draw modes, each with none/x/y/xy flipped, three attributes per sprite.
        const int count = 36;
        var rt = Add(new RenderTexture(count, 1, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear));
        var camera = Camera2D(Color.black, rt);
        camera.orthographicSize = 0.5f;
        camera.aspect = count;
        var shader = Shader.Find("Probe/SpriteAttribs");
        for (int i = 0; i < count; i++)
        {
            var m = Add(new Material(shader));
            m.SetFloat("_Which", i % 3);
            var r = SpriteAt("Square", new Vector3(-(count - 1) * 0.5f + i, 0f, 0f), Color.white);
            r.sharedMaterial = m;
            r.drawMode = (SpriteDrawMode)(i / 12);
            r.size = Vector2.one;
            int flip = i / 3 % 4;
            r.flipX = (flip & 1) != 0;
            r.flipY = (flip & 2) != 0;
        }
        camera.Render();
        var t = Read(rt);
        var sb = new System.Text.StringBuilder("[");
        for (int i = 0; i < count; i++)
        {
            Color32 c = t.GetPixel(i, 0);
            sb.Append(i == 0 ? "" : ",").Append("[" + c.r + "," + c.g + "," + c.b + "]");
        }
        DestroyImmediate(t);
        Value("sprite.attributes", sb.Append("]").ToString());
        Clear();
    }

    void SecondaryTextures()
    {
        DefaultTextures();
        SpriteAttributes();
        var lit = LoadSprite("Lit");
        var square = LoadSprite("Square");
        Value("secondary.imported", Secondary(lit));
        Value("secondary.none", Secondary(square));
        var normal = Resources.Load<Texture2D>("LitNormal");
        var mask = Resources.Load<Texture2D>("LitMask");
        var made = Add(Sprite.Create(lit.texture, lit.rect, new Vector2(0.5f, 0.5f), 16f, 0, SpriteMeshType.FullRect, Vector4.zero, false,
            new[] { new SecondarySpriteTexture { name = "_MaskTex", texture = mask } }));
        Value("secondary.created", Secondary(made));
        var plain = Add(Sprite.Create(lit.texture, lit.rect, new Vector2(0.5f, 0.5f), 16f));
        Value("secondary.created_none", Secondary(plain));
        Try("secondary.null_array", () => I(lit.GetSecondaryTextures(null)));

        var camera = Camera2D(s_Bg);
        var material = Add(new Material(Shader.Find("Probe/SecondaryTex")));
        material.SetTexture("_MaskTex", Texture2D.blackTexture);
        var sprites = new[] { lit, made, plain, square };
        for (int i = 0; i < sprites.Length; i++)
        {
            var r = SpriteAt("Square", new Vector3(-1.8f + 1.2f * i, 0f, 0f), Color.white, scale: new Vector3(1f, 3f, 1f));
            r.sprite = sprites[i];
            r.sharedMaterial = material;
        }
        // A property block's texture against the sprite's.
        var blocked = SpriteAt("Square", new Vector3(0f, 1.9f, 0f), Color.white, scale: new Vector3(5f, 0.5f, 1f));
        blocked.sprite = lit;
        blocked.sharedMaterial = material;
        var block = new MaterialPropertyBlock();
        block.SetTexture("_MaskTex", Texture2D.whiteTexture);
        blocked.SetPropertyBlock(block);
        Capture(camera, "secondary_textures");
        Clear();
    }
}
