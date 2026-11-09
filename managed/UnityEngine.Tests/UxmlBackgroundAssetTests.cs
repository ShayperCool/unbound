#pragma warning disable CS0618 // UxmlFactory and UxmlTraits are deprecated in Unity 6 and still supported
using System;
using System.Collections.Generic;
using Unbound.Runtime;
using Unbound.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using UxmlImageProbes;
using Xunit;
using Object = UnityEngine.Object;

// UXML asset references read as images: Button and Tab `icon-image`, and [UxmlAttribute] members of
// type Background or Texture. Each test names its source:
//   [doc]    the Unity 6000.0 Manual pages "Button" and "Tab" (attribute `icon-image`, type Object:
//            a Texture, Sprite or VectorImage asset; the Button page's text also names a Render
//            Texture), the Scripting API pages Background (FromTexture2D, FromRenderTexture,
//            FromSprite, FromVectorImage; GetSelectedImage "can be of a type of Texture, Sprite,
//            RenderTexture or VectorImage"), Button.iconImage, Tab.iconImage and
//            UxmlAttributeAttribute ("Unity objects can also be UxmlAttributes and will contain a
//            reference to the asset file"), and the Manual "Define UXML attributes for built-in types"
//            (my-texture="project://database/Assets/MyTexture.png");
//   [measured] the uxmlattributes probe (tests/projects/uxmlattributes, Unity 6000.0.71f1):
//            icon-image always replaces the icon with the Background made from the object the url
//            names (an empty or unresolved url warns and clears it, an object no Background can
//            show clears it silently); a [UxmlAttribute] Background or List<Background> member is
//            not read and its url not checked; Texture members resolve as object members do;
//   [choice] Unbound's choice where neither says anything (other texture types, a #name naming
//            several assets). These check Unbound's implementation, not Unity behaviour.
//
// Red base: 7718c50e (this file overlaid; it compiles there, every member it uses exists there).
// Each red is the named runtime assertion, not a compile failure.
//
// Assets are registered with AssetDatabaseRuntime.AddLoaded and found by the reference's guid and
// fileID: without the native side, the resolver cannot look a path up (AssetDatabaseRuntime.InfoByPath
// is a native call), so a reference by path alone resolves to nothing in these tests.

namespace UnityEngine.Tests
{
    [Collection("UXML custom controls")]
    public class UxmlBackgroundAssetTests
    {
        const string Path = "Assets/UI/Screens/UxmlBackgroundProbe.uxml";
        const string IconGuid = "6b0c3f1e2d4a4e5f8a9b0c1d2e3f4a5b";
        const string VectorGuid = "7c1d4e2f3a5b4c6d9e0f1a2b3c4d5e6f";
        const string RenderGuid = "8d2e5f3a4b6c4d7e0f1a2b3c4d5e6f70";
        const string CubeGuid = "9e3f6a4b5c7d4e8f1a2b3c4d5e6f7081";
        const string SheetGuid = "af4a7b5c6d8e4f9a2b3c4d5e6f708192";
        const string MissingGuid = "b05b8c6d7e9f4a0b3c4d5e6f708192a3";
        const long TextureId = 2800000;
        const long SpriteId = 21300000;

        /// <summary>The project-GUID reference UI Builder writes, here to <paramref name="fileId"/> of
        /// <paramref name="guid"/>, escaped for an XML attribute.</summary>
        static string Ref(string assetPath, string guid, long fileId, string name) =>
            $"project://database/{assetPath}?fileID={fileId}&amp;guid={guid}&amp;type=3#{name}";

        static (VisualElement Root, VisualTreeAsset Asset) Clone(string body)
        {
            var asset = ScriptableObject.CreateInstance<VisualTreeAsset>();
            asset.name = "UxmlBackgroundProbe";
            UxmlImporter.Import(Path, "<ui:UXML xmlns:ui=\"UnityEngine.UIElements\">" + body + "</ui:UXML>", asset);
            var root = new VisualElement();
            asset.CloneTree(root);
            return (root, asset);
        }

        /// <summary>Warnings and errors logged while <paramref name="action"/> runs that name this
        /// file's document (other tests may log at the same time).</summary>
        static List<string> Logged(Action action)
        {
            var logs = new List<string>();
            Application.LogCallback capture = (message, stack, type) =>
            {
                if (type != LogType.Log && message != null && message.Contains(Path)) lock (logs) logs.Add(message);
            };
            Application.logMessageReceived += capture;
            try { action(); }
            finally { Application.logMessageReceived -= capture; }
            return logs;
        }

        /// <summary>Warnings and errors logged while <paramref name="action"/> runs that name this
        /// file's document or are the UXML importer's url warnings, as "Type: message".</summary>
        static List<string> LoggedAll(Action action)
        {
            var logs = new List<string>();
            Application.LogCallback capture = (message, stack, type) =>
            {
                if (type != LogType.Log && message != null && (message.Contains(Path) || message.Contains("): Semantic - ")))
                    lock (logs) logs.Add(type + ": " + message);
            };
            Application.logMessageReceived += capture;
            try { action(); }
            finally { Application.logMessageReceived -= capture; }
            return logs;
        }

        /// <summary>An image file as Unity's texture importer makes it in single sprite mode: a
        /// Texture2D main asset and a Sprite of the same name.</summary>
        sealed class IconFile : IDisposable
        {
            internal readonly Texture2D Texture;
            internal readonly Sprite Sprite;
            readonly LoadedFile m_File;

            internal IconFile(string guid = IconGuid, string assetPath = "Assets/UI/Icons/Icon.png")
            {
                Texture = new Texture2D(4, 4) { name = "Icon", IsPersistentAsset = true };
                Sprite = Sprite.Create(Texture, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f));
                Sprite.name = "Icon";
                Sprite.IsPersistentAsset = true;
                m_File = new LoadedFile { Guid = guid, Path = assetPath, MainObject = Texture };
                m_File.Objects[TextureId] = Texture;
                m_File.Objects[SpriteId] = Sprite;
                AssetDatabaseRuntime.AddLoaded(m_File);
            }

            public void Dispose()
            {
                AssetDatabaseRuntime.Invalidate(m_File.Guid);
                Object.DestroyImmediate(Sprite, true);
                Object.DestroyImmediate(Texture, true);
            }
        }

        /// <summary>A file whose only asset is <paramref name="main"/>.</summary>
        static IDisposable OneAssetFile(string guid, string assetPath, Object main, long fileId)
        {
            main.IsPersistentAsset = true;
            var file = new LoadedFile { Guid = guid, Path = assetPath, MainObject = main };
            file.Objects[fileId] = main;
            AssetDatabaseRuntime.AddLoaded(file);
            return new Cleanup(() =>
            {
                AssetDatabaseRuntime.Invalidate(guid);
                Object.DestroyImmediate(main, true);
            });
        }

        sealed class Cleanup : IDisposable
        {
            readonly Action m_Action;
            internal Cleanup(Action action) => m_Action = action;
            public void Dispose() => m_Action();
        }

        // ------------------------------------------------------------------ Button and Tab

        // [doc] Button icon-image takes a Texture asset of the project; Button.iconImage is "The
        // Texture, Sprite, or VectorImage that will represent an icon"; Background.FromTexture2D.
        // [choice] The project-GUID reference to the texture's fileID gives that texture.
        // Red at 7718c50e: the builder cannot convert the text to a Background and drops it silently;
        // Assert.Same(icon.Texture, button.iconImage.texture) fails (null).
        [Fact]
        public void ButtonIconImageResolvesAProjectGuidReference()
        {
            using var icon = new IconFile();
            List<string> logs = null;
            VisualElement root = null;
            logs = Logged(() => root = Clone($"<ui:Button text=\"Go\" icon-image=\"{Ref("Assets/UI/Icons/Icon.png", IconGuid, TextureId, "Icon")}\" />").Root);
            var button = Assert.IsType<Button>(root[0]);
            Assert.Same(icon.Texture, button.iconImage.texture);
            Assert.Null(button.iconImage.sprite);
            Assert.Empty(logs);
        }

        // [doc] Tab icon-image ("Sets the icon for the Tab's header") takes a Sprite asset;
        // Background.FromSprite. [choice] A reference relative to the document (here with the
        // guid and fileID UI Builder adds) gives the Sprite its fileID names.
        // Red at 7718c50e: the attribute is dropped silently; Assert.Same(icon.Sprite,
        // tab.iconImage.sprite) fails (null).
        [Fact]
        public void TabIconImageResolvesARelativeReferenceToASprite()
        {
            using var icon = new IconFile();
            List<string> logs = null;
            VisualElement root = null;
            logs = Logged(() => root = Clone(
                $"<ui:TabView><ui:Tab label=\"A\" icon-image=\"../Icons/Icon.png?fileID={SpriteId}&amp;guid={IconGuid}&amp;type=3#Icon\" /></ui:TabView>").Root);
            var tab = Assert.IsType<TabView>(root[0]).GetTab(0);
            Assert.Same(icon.Sprite, tab.iconImage.sprite);
            Assert.Null(tab.iconImage.texture);
            Assert.Empty(logs);
        }

        // [doc] Tab icon-image with the texture of the project-GUID reference; Button icon-image with
        // the relative reference to the sprite (both elements, both reference forms).
        // Red at 7718c50e: both attributes are dropped silently; Assert.Same(icon.Texture,
        // tab.iconImage.texture) fails (null).
        [Fact]
        public void TabAndButtonTakeBothReferenceForms()
        {
            using var icon = new IconFile();
            var (root, _) = Clone(
                $"<ui:TabView><ui:Tab label=\"A\" icon-image=\"{Ref("Assets/UI/Icons/Icon.png", IconGuid, TextureId, "Icon")}\" /></ui:TabView>"
                + $"<ui:Button icon-image=\"../Icons/Icon.png?fileID={SpriteId}&amp;guid={IconGuid}&amp;type=3#Icon\" />");
            var tab = Assert.IsType<TabView>(root[0]).GetTab(0);
            Assert.Same(icon.Texture, tab.iconImage.texture);
            Assert.Same(icon.Sprite, Assert.IsType<Button>(root[1]).iconImage.sprite);
        }

        // [doc] The Button page's text also names a Render Texture, the attribute table a
        // VectorImage; Background.FromRenderTexture and FromVectorImage.
        // Red at 7718c50e: both attributes are dropped silently; Assert.Same(render,
        // ...iconImage.renderTexture) fails (null).
        [Fact]
        public void ButtonIconImageTakesRenderTexturesAndVectorImages()
        {
            var render = new RenderTexture(4, 4, 0) { name = "Target" };
            var vector = ScriptableObject.CreateInstance<VectorImage>();
            vector.name = "Shape";
            using var renderFile = OneAssetFile(RenderGuid, "Assets/UI/Icons/Target.renderTexture", render, 8400000);
            using var vectorFile = OneAssetFile(VectorGuid, "Assets/UI/Icons/Shape.svg", vector, 11400000);
            var (root, _) = Clone(
                $"<ui:Button icon-image=\"{Ref("Assets/UI/Icons/Target.renderTexture", RenderGuid, 8400000, "Target")}\" />"
                + $"<ui:Button icon-image=\"{Ref("Assets/UI/Icons/Shape.svg", VectorGuid, 11400000, "Shape")}\" />");
            Assert.Same(render, Assert.IsType<Button>(root[0]).iconImage.renderTexture);
            Assert.Same(vector, Assert.IsType<Button>(root[1]).iconImage.vectorImage);
        }

        // ------------------------------------------------------------------ diagnostics

        // [measured] presetIconMissing: a reference that resolves to nothing logs the importer's
        // warnings (for a guid: its path hint, then the url) at the element's position, once however
        // often the document is cloned, and clears the icon the element already had (here one its
        // constructor set, in a game [UxmlElement] subclass whose icon-image is Button's own).
        [Fact]
        public void MissingReferenceWarnsOnceAndClearsTheIcon()
        {
            var url = Ref("Assets/UI/Icons/Missing.png", MissingGuid, TextureId, "Missing");
            VisualElement root = null;
            VisualTreeAsset asset = null;
            var logs = LoggedAll(() => (root, asset) = Clone($"<UxmlImageProbes.PresetIconButton icon-image=\"{url}\" />"));
            Assert.True(Assert.IsType<PresetIconButton>(root[0]).iconImage.IsEmpty());
            Assert.Equal(2, logs.Count);
            Assert.All(logs, l => Assert.StartsWith("Warning:  (1,45): Semantic - The specified URI does not exist in the current project : ", l));
            Assert.Empty(LoggedAll(() => asset.CloneTree(new VisualElement())));
        }

        // Measured: a path reference that does not resolve (relative, without guid) logs one
        // warning naming the project path it resolves to; the Tab's icon stays empty.
        [Fact]
        public void MissingRelativeReferenceOnATabWarns()
        {
            VisualElement root = null;
            var logs = LoggedAll(() => root = Clone("<ui:TabView><ui:Tab label=\"A\" icon-image=\"Icons/Missing.png\" /></ui:TabView>").Root);
            var tab = Assert.IsType<TabView>(root[0]).GetTab(0);
            Assert.True(tab.iconImage.IsEmpty());
            Assert.Equal(new[] { "Warning:  (1,57): Semantic - The specified URI does not exist in the current project : Assets/UI/Screens/Icons/Missing.png" }, logs);
        }

        // [measured] presetIconWrongType, iconTemplate: an icon-image naming an asset no Background can
        // show (a style sheet) clears the icon without a log. Control: without the attribute the
        // preset icon stays (presetIconNone).
        [Fact]
        public void WrongClassClearsTheIconSilently()
        {
            var sheet = ScriptableObject.CreateInstance<StyleSheet>();
            sheet.name = "Theme";
            using var file = OneAssetFile(SheetGuid, "Assets/UI/Theme.uss", sheet, 7433441132597879392);
            List<string> logs = null;
            VisualElement root = null;
            logs = LoggedAll(() => root = Clone(
                $"<UxmlImageProbes.PresetIconButton icon-image=\"{Ref("Assets/UI/Theme.uss", SheetGuid, 7433441132597879392, "Theme")}\" />"
                + "<UxmlImageProbes.PresetIconButton />").Root);
            Assert.True(Assert.IsType<PresetIconButton>(root[0]).iconImage.IsEmpty());
            Assert.Same(PresetIconButton.Preset, Assert.IsType<PresetIconButton>(root[1]).iconImage.texture);
            Assert.Empty(logs);
        }

        // [doc] A Background holds a Texture2D (texture), RenderTexture, Sprite or VectorImage.
        // [choice] Another texture type (a Cubemap) follows the measured rule for objects no
        // Background can show: the icon is cleared without a log.
        [Fact]
        public void OtherTextureTypesClearTheIconSilently()
        {
            var cube = new Cubemap(2, TextureFormat.RGBA32, false) { name = "Sky" };
            using var file = OneAssetFile(CubeGuid, "Assets/UI/Sky.cubemap", cube, 8900000);
            List<string> logs = null;
            VisualElement root = null;
            logs = LoggedAll(() => root = Clone($"<UxmlImageProbes.PresetIconButton icon-image=\"{Ref("Assets/UI/Sky.cubemap", CubeGuid, 8900000, "Sky")}\" />").Root);
            Assert.True(Assert.IsType<PresetIconButton>(root[0]).iconImage.IsEmpty());
            Assert.Empty(logs);
        }

        // [choice] A #name without a fileID that names both the texture and the sprite of an image
        // file names the first of them in the file, as for object members: the texture, its main
        // asset; nothing is logged.
        [Fact]
        public void AmbiguousNameTakesTheFirstNamedAsset()
        {
            using var icon = new IconFile();
            List<string> logs = null;
            VisualElement root = null;
            logs = LoggedAll(() => root = Clone($"<ui:Button icon-image=\"project://database/Assets/UI/Icons/Icon.png?guid={IconGuid}#Icon\" />").Root);
            Assert.Same(icon.Texture, Assert.IsType<Button>(root[0]).iconImage.texture);
            Assert.Empty(logs);
        }

        // ------------------------------------------------------------------ [UxmlAttribute] members

        // [measured] bgTexture, bgSprite, presetRefWrongType, presetRefMissing: a [UxmlAttribute] of
        // type Background or List<Background> (no game converter) is not read from UXML: a texture
        // or sprite reference leaves the member as it was (here its initializer), without a log.
        [Fact]
        public void DeclaredBackgroundAttributesAreNotRead()
        {
            using var icon = new IconFile();
            List<string> logs = null;
            VisualElement root = null;
            var texture = Ref("Assets/UI/Icons/Icon.png", IconGuid, TextureId, "Icon");
            logs = LoggedAll(() => root = Clone(
                $"<UxmlImageProbes.BadgeProbe badge=\"{texture}\" />"
                + $"<UxmlImageProbes.BadgeProbe badge=\"../Icons/Icon.png?fileID={SpriteId}&amp;guid={IconGuid}&amp;type=3#Icon\" />"
                + $"<UxmlImageProbes.BadgeListProbe badges=\"{texture}\" />").Root);
            Assert.Same(PresetIconButton.Preset, Assert.IsType<BadgeProbe>(root[0]).badge.texture);
            Assert.Same(PresetIconButton.Preset, Assert.IsType<BadgeProbe>(root[1]).badge.texture);
            Assert.Null(Assert.IsType<BadgeListProbe>(root[2]).badges);
            Assert.Empty(logs);
        }

        // [measured] bgMissing, bgEmpty, presetRefMissing: a declared Background's url is
        // not checked: a missing reference logs nothing.
        [Fact]
        public void DeclaredBackgroundAttributeUrlsAreNotChecked()
        {
            var url = Ref("Assets/UI/Icons/Missing.png", MissingGuid, TextureId, "Missing");
            VisualElement root = null;
            var logs = LoggedAll(() => root = Clone($"<UxmlImageProbes.BadgeProbe badge=\"{url}\" />").Root);
            Assert.Same(PresetIconButton.Preset, Assert.IsType<BadgeProbe>(root[0]).badge.texture);
            Assert.Empty(logs);
        }

        // [doc] The Manual's [UxmlAttribute] Texture2D written as a project reference. Measured
        // (tests/projects/uxmlattributes): a reference to the file's sprite gives a Texture2D member
        // the file's texture, without a log.
        [Fact]
        public void DeclaredTextureAttributesTakeTheTextureOfTheReferencedFile()
        {
            using var icon = new IconFile();
            VisualElement root = null;
            var logs = LoggedAll(() => root = Clone($"<UxmlImageProbes.TextureProbe picture=\"{Ref("Assets/UI/Icons/Icon.png", IconGuid, SpriteId, "Icon")}\" />").Root);
            Assert.Same(icon.Texture, Assert.IsType<TextureProbe>(root[0]).picture);
            Assert.Empty(logs);
        }

        // [doc] A [UxmlAttribute] of type Texture holds any texture asset, here a Cubemap and the
        // texture of an image file. Control, not red: 7718c50e reads it the same way (as any Unity
        // object); it pins that the image reader keeps this.
        [Fact]
        public void DeclaredTextureBaseAttributesTakeAnyTexture()
        {
            using var icon = new IconFile();
            var cube = new Cubemap(2, TextureFormat.RGBA32, false) { name = "Sky" };
            using var file = OneAssetFile(CubeGuid, "Assets/UI/Sky.cubemap", cube, 8900000);
            var (root, _) = Clone(
                $"<UxmlImageProbes.AnyTextureProbe image=\"{Ref("Assets/UI/Sky.cubemap", CubeGuid, 8900000, "Sky")}\" />"
                + $"<UxmlImageProbes.AnyTextureProbe image=\"{Ref("Assets/UI/Icons/Icon.png", IconGuid, TextureId, "Icon")}\" />");
            Assert.Same(cube, Assert.IsType<AnyTextureProbe>(root[0]).image);
            Assert.Same(icon.Texture, Assert.IsType<AnyTextureProbe>(root[1]).image);
        }

        // ------------------------------------------------------------------ unchanged behaviour

        // [doc] UxmlAttributeAttribute declares a UXML attribute; a game's own public Background
        // property without it is not one (UXML P2-FUNC-1), for an [UxmlElement] and for a UxmlFactory
        // over Button's traits alike, and nothing is logged for it. Button's own icon-image still
        // applies on that factory element.
        // Red at 7718c50e: the factory element's icon-image is dropped silently;
        // Assert.Same(icon.Texture, factoryButton.iconImage.texture) fails (null). The `badge`
        // assertions are a guard that passes there too; their red recipe is a mutation passing
        // engineDeclaredOnly: false from ApplyUxmlAttributes and ApplyBuiltInTraitAttributes, which
        // sets `badge` (Assert.True(...badge.IsEmpty()) fails).
        [Fact]
        public void UndeclaredGameBackgroundPropertiesStayUntouched()
        {
            using var icon = new IconFile();
            List<string> logs = null;
            VisualElement root = null;
            var reference = Ref("Assets/UI/Icons/Icon.png", IconGuid, TextureId, "Icon");
            logs = Logged(() => root = Clone(
                $"<UxmlImageProbes.UndeclaredBadge badge=\"{reference}\" />"
                + $"<UxmlImageProbes.FactoryIconButton badge=\"{reference}\" icon-image=\"{reference}\" />").Root);
            Assert.True(Assert.IsType<UndeclaredBadge>(root[0]).badge.IsEmpty());
            var factoryButton = Assert.IsType<FactoryIconButton>(root[1]);
            Assert.True(factoryButton.badge.IsEmpty());
            Assert.Same(icon.Texture, factoryButton.iconImage.texture);
            Assert.DoesNotContain(logs, l => l.Contains("'badge'"));
        }

        // [choice] UXML EnumField `type` and `value` are applied as before, an empty icon-image is no
        // icon without an error (the importer's warning is not this document's), and an attribute Unbound cannot convert that is not an asset
        // reference (MinMaxSlider low-limit="not-a-number") stays silent, the property unchanged.
        // Guard, not red: 7718c50e behaves the same. Red recipe: a mutation logging every failed
        // TryConvert in ApplyAttribute makes Assert.Empty(logs) fail.
        [Fact]
        public void EnumFieldAndOtherConversionsAreUnchanged()
        {
            var type = typeof(ProbeShade).FullName + ", " + typeof(ProbeShade).Assembly.GetName().Name;
            List<string> logs = null;
            VisualElement root = null;
            logs = Logged(() => root = Clone(
                $"<ui:EnumField type=\"{type}\" value=\"Dark\" />"
                + "<ui:Button text=\"Plain\" icon-image=\"\" />"
                + "<ui:MinMaxSlider low-limit=\"not-a-number\" />").Root);
            var field = Assert.IsType<EnumField>(root[0]);
            Assert.Equal(ProbeShade.Dark, field.value);
            Assert.True(Assert.IsType<Button>(root[1]).iconImage.IsEmpty());
            Assert.Equal(new MinMaxSlider().lowLimit, Assert.IsType<MinMaxSlider>(root[2]).lowLimit);
            Assert.Empty(logs);
        }
    }
}

namespace UxmlImageProbes
{
    public enum ProbeShade
    {
        Light,
        Dark,
    }

    [UxmlElement]
    public partial class PresetIconButton : Button
    {
        internal static readonly Texture2D Preset = new Texture2D(2, 2) { name = "Preset" };

        public PresetIconButton() => iconImage = Background.FromTexture2D(Preset);
    }

    [UxmlElement]
    public partial class BadgeProbe : VisualElement
    {
        [UxmlAttribute] public Background badge = Background.FromTexture2D(PresetIconButton.Preset);
    }

    [UxmlElement]
    public partial class BadgeListProbe : VisualElement
    {
        [UxmlAttribute] public List<Background> badges;
    }

    [UxmlElement]
    public partial class TextureProbe : VisualElement
    {
        [UxmlAttribute] public Texture2D picture = PresetIconButton.Preset;
    }

    [UxmlElement]
    public partial class AnyTextureProbe : VisualElement
    {
        [UxmlAttribute] public Texture image;
    }

    [UxmlElement]
    public partial class UndeclaredBadge : VisualElement
    {
        public Background badge { get; set; }
    }

    public class FactoryIconButton : Button
    {
        public Background badge { get; set; }

        public new class UxmlFactory : UxmlFactory<FactoryIconButton, Button.UxmlTraits> { }
    }
}
