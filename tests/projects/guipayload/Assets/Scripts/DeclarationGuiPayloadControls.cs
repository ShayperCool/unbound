using System;
using System.Reflection;
using UnityEngine;

public static class DeclarationGuiPayloadControls
{
    [Serializable] public class Holder { public GUIStyle style; public GUIStyle[] styles; public int control = 31; }
    [Serializable] public class StateHolder { public GUIStyleState state; public int control = 37; }
    public class Asset : ScriptableObject { public GUIStyle style; public GUIStyle[] styles; public int control = 31; }

    public static GUIStyle Make()
    {
        var style = new GUIStyle { name = "payload", fontSize = 17, fontStyle = FontStyle.BoldAndItalic,
            alignment = TextAnchor.LowerRight, wordWrap = true, richText = false, clipping = TextClipping.Clip,
            imagePosition = ImagePosition.ImageAbove, contentOffset = new Vector2(3.25f, -7.5f),
            fixedWidth = 29.5f, fixedHeight = 41.25f, stretchWidth = false, stretchHeight = true,
            border = new RectOffset(1, 2, 3, 4), margin = new RectOffset(5, 6, 7, 8),
            padding = new RectOffset(9, 10, 11, 12), overflow = new RectOffset(13, 14, 15, 16) };
        GUIStyleState[] states = { style.normal, style.hover, style.active, style.focused, style.onNormal, style.onHover, style.onActive, style.onFocused };
        for (int i = 0; i < states.Length; i++) states[i].textColor = new Color(i + .25f, i + .5f, i + .75f, i + 1);
        return style;
    }

    public static void Record(Action<string, object> row)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        foreach (var type in new[] { typeof(GUIStyle), typeof(GUISkin) })
            foreach (var field in type.GetFields(flags))
                if (type == typeof(GUIStyle) && (field.FieldType == typeof(GUIStyleState) || field.FieldType == typeof(RectOffset) || field.Name == "m_Name")
                    || type == typeof(GUISkin) && (field.Name == "m_horizontalSliderThumbExtent" || field.Name == "m_verticalSliderThumbExtent" || field.Name == "m_SliderMixed"))
                    row("gui.payload.metadata." + type.Name + "." + field.Name, field.IsNotSerialized + "/" + field.IsDefined(typeof(SerializeField), false));
        var yamlSkin = UnityEditor.AssetDatabase.LoadAssetAtPath<GUISkin>("Assets/Payload.guiskin");
        row("gui.payload.yaml-box", JsonUtility.ToJson(new Holder { style = yamlSkin.box }));
        row("gui.payload.yaml-custom", JsonUtility.ToJson(new Holder { style = yamlSkin.customStyles[0] }));
        var style = Make();
        string json = JsonUtility.ToJson(style);
        row("gui.payload.root", json);
        row("gui.payload.state-root", JsonUtility.ToJson(style.normal));
        row("gui.payload.state-nested", JsonUtility.ToJson(new StateHolder { state = style.normal }));
        row("gui.payload.nested", JsonUtility.ToJson(new Holder { style = style, styles = new[] { style } }));
        var holder = JsonUtility.FromJson<Holder>(JsonUtility.ToJson(new Holder { style = style, styles = new[] { style } }));
        row("gui.payload.roundtrip", JsonUtility.ToJson(holder));
        holder.control = 79;
        JsonUtility.FromJsonOverwrite("{\"style\":{\"m_Name\":\"changed\",\"m_Normal\":{\"m_TextColor\":{\"r\":19}},\"m_Padding\":{\"m_Left\":27}}}", holder);
        row("gui.payload.overwrite", JsonUtility.ToJson(holder));
        JsonUtility.FromJsonOverwrite("{\"m_Name\":\"root-change\",\"m_FontSize\":55}", style);
        row("gui.payload.original", JsonUtility.ToJson(new Holder { style = style }));
        row("gui.payload.nulls", JsonUtility.ToJson(new Holder { styles = new GUIStyle[] { null } }));
        var sdf = JsonUtility.FromJson<Holder>("{\"style\":{\"m_IsSDF\":true,\"m_FontSize\":23}}");
        row("gui.payload.sdf", JsonUtility.ToJson(sdf));
        row("gui.payload.sdf-copy", JsonUtility.ToJson(new Holder { style = new GUIStyle(sdf.style) }));
        var texture = new Texture2D(2, 2);
        var referenceStyle = Make();
        referenceStyle.normal.background = texture;
        referenceStyle.normal.scaledBackgrounds = new[] { texture };
        var referenceHolder = new Holder { style = referenceStyle };
        string referenceJson = JsonUtility.ToJson(referenceHolder);
        row("gui.payload.reference-json", referenceJson.Contains("\"instanceID\":" + texture.GetInstanceID()));
        var referenceRead = JsonUtility.FromJson<Holder>(referenceJson);
        row("gui.payload.reference-read", referenceRead.style.normal.background == texture);
        row("gui.payload.scaled-read", referenceRead.style.normal.scaledBackgrounds.Length + "/" + (referenceRead.style.normal.scaledBackgrounds[0] == texture));
        var referenceCopy = new GUIStyle(referenceStyle);
        var scaled = referenceCopy.normal.scaledBackgrounds;
        scaled[0] = null;
        row("gui.payload.scaled-independent", referenceStyle.normal.scaledBackgrounds[0] == texture);
        var asset = ScriptableObject.CreateInstance<Asset>();
        asset.style = style; asset.styles = new[] { style }; asset.control = 73;
        var copy = UnityEngine.Object.Instantiate(asset);
        row("gui.payload.clone", JsonUtility.ToJson(new Holder { style = copy.style, styles = copy.styles, control = copy.control }));
        copy.style.normal.textColor = Color.red; copy.style.padding.left = 97; copy.styles[0].name = "array-copy";
        row("gui.payload.clone-independent", JsonUtility.ToJson(new Holder { style = style }));
        row("gui.payload.clone-array-distinct", copy.style.name);
        asset.style = referenceStyle;
        var referenceAsset = UnityEngine.Object.Instantiate(asset);
        row("gui.payload.clone-reference", referenceAsset.style.normal.background == texture);
        row("gui.payload.clone-scaled", referenceAsset.style.normal.scaledBackgrounds[0] == texture);
        var skin = ScriptableObject.CreateInstance<GUISkin>();
        skin.name = "payload-skin"; skin.box = style; skin.customStyles = new[] { Make() };
        skin.settings.selectionColor = new Color(.125f, .25f, .5f, 1f);
        var skinCopy = UnityEngine.Object.Instantiate(skin);
        row("gui.payload.skin-clone", JsonUtility.ToJson(new Holder { style = skinCopy.box }));
        row("gui.payload.skin-custom", JsonUtility.ToJson(new Holder { style = skinCopy.customStyles[0] }));
        var copyStyle = new GUIStyle(style);
        row("gui.payload.copy-constructor", JsonUtility.ToJson(new Holder { style = copyStyle }));
        copyStyle.padding.top = 89; copyStyle.hover.textColor = Color.green;
        row("gui.payload.copy-independent", style.padding.top + "/" + style.hover.textColor.r);
        skinCopy.box.padding.right = 101; skinCopy.customStyles[0].fontSize = 43;
        row("gui.payload.skin-independent", skin.box.padding.right + "/" + skin.customStyles[0].fontSize);
        UnityEngine.Object.DestroyImmediate(skinCopy); UnityEngine.Object.DestroyImmediate(skin);
        UnityEngine.Object.DestroyImmediate(copy); UnityEngine.Object.DestroyImmediate(asset);
        UnityEngine.Object.DestroyImmediate(referenceAsset); UnityEngine.Object.DestroyImmediate(texture);
    }
}
