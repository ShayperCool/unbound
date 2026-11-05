using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Unbound.Runtime;
using UnityEngine.Rendering;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>
    /// GUI.DrawTexture / GUI.DrawTextureWithTexCoords with alphaBlend false. Documented (Unity
    /// 6000.0 Scripting API and the installed IMGUIModule doc XML): alphaBlend is "whether to apply
    /// alpha blending when drawing the image (enabled by default)", and for DrawTextureWithTexCoords
    /// "if false, the picture is drawn on to the display". The docs say nothing about batching,
    /// render state, stencil, clipping, GUI.matrix or ordering for these draws: those cases check
    /// Unbound's choice (unmeasured) that an opaque draw is a blended draw with blending turned off
    /// for that draw alone. No Unity reference capture covers them; no pixel is asserted.
    ///
    /// Red recipes. At b3398e3b alphaBlend false only logged a once-per-session warning and drew
    /// through the blended path, and DrawItem.OpaqueImgui / GuiRender.CollectItems /
    /// FrameBuilder.BeginOpaqueImgui do not exist, so a literal b3398e3b build of this file does not
    /// compile; that is NOT counted as a per-case red. Each case instead names (1) what b3398e3b does
    /// for its input and (2) a compiling mutation of the new head that restores that behaviour (or
    /// breaks the guarded property) and turns the case red. All UNEXECUTED.
    /// </summary>
    public class GuiOpaqueDrawTests
    {
        static GuiOpaqueDrawTests()
        {
            foreach (var type in new[] { typeof(FrameBuilder), typeof(SpriteMasking), typeof(Material) })
                RuntimeHelpers.RunClassConstructor(type.TypeHandle);
        }

        // ------------------------------------------------------------------ batch identity and order

        /// <summary>Unbound's choice (unmeasured): the same texture drawn blended, opaque, blended
        /// under one clip stays three draws, in call order, each with its own blending; the shared
        /// UI material is not touched. b3398e3b: one merged blended batch. Red: drop
        /// <c>&amp;&amp; last.Opaque == opaque</c> from GuiRender.BatchFor (one item), or make
        /// CollectItems set <c>OpaqueImgui = false</c> (flags all false).</summary>
        [Fact]
        public void BlendedOpaqueBlendedOfOneTextureStayThreeDrawsInCallOrder()
        {
            using var f = new Fixture();
            var texture = f.Texture(601);
            var rect = new Rect(10f, 10f, 20f, 20f);
            f.Repaint(() =>
            {
                GUI.DrawTexture(rect, texture, ScaleMode.StretchToFill, true);
                GUI.DrawTexture(rect, texture, ScaleMode.StretchToFill, false);
                GUI.DrawTexture(rect, texture, ScaleMode.StretchToFill, true);
            });
            var items = f.Collect();
            Assert.Equal(new[] { false, true, false }, items.Select(i => i.OpaqueImgui).ToArray());
            Assert.Equal(new[] { 0, 1, 2 }, items.Select(i => i.Submesh).ToArray());
            Assert.All(items, i => Assert.Equal(601ul, i.MainTexture));
            Assert.All(items, i => Assert.Same(f.UiMaterial, i.Material));
            Assert.True(items[0].Sequence < items[1].Sequence && items[1].Sequence < items[2].Sequence);
            Assert.Equal(new[] { 6, 6, 6 }, f.BatchIndexCounts());
        }

        /// <summary>Unbound's choice (unmeasured): consecutive opaque draws of one texture and clip
        /// share one batch, as blended ones do. b3398e3b: one blended batch (flag absent). Red:
        /// return a new batch whenever <c>opaque</c> is true in BatchFor (two items), or invert the
        /// flag passed by GuiRender.DrawTexture (item not opaque).</summary>
        [Fact]
        public void ConsecutiveOpaqueDrawsOfOneTextureShareABatch()
        {
            using var f = new Fixture();
            var texture = f.Texture(602);
            f.Repaint(() =>
            {
                GUI.DrawTexture(new Rect(0f, 0f, 8f, 8f), texture, ScaleMode.StretchToFill, false);
                GUI.DrawTextureWithTexCoords(new Rect(8f, 0f, 8f, 8f), texture, new Rect(0f, 0f, 1f, 1f), false);
            });
            var item = Assert.Single(f.Collect());
            Assert.True(item.OpaqueImgui);
            Assert.Equal(new[] { 12 }, f.BatchIndexCounts());
        }

        /// <summary>Documented default: alphaBlend is enabled by default. Default calls, the
        /// DrawTextureWithTexCoords three-argument overload and the style path (GuiRender.DrawTexture
        /// without alphaBlend, used by GUIStyle) stay blended and keep merging. b3398e3b: same (this
        /// guards the default). Red: change the four-argument GuiRender.DrawTexture to pass
        /// <c>false</c>, or pass <c>alphaBlend</c> un-negated to Quad.</summary>
        [Fact]
        public void DefaultCallsKeepTheBlendedPath()
        {
            using var f = new Fixture();
            var texture = f.Texture(603);
            f.Repaint(() =>
            {
                GUI.DrawTexture(new Rect(0f, 0f, 8f, 8f), texture);
                GUI.DrawTexture(new Rect(0f, 0f, 8f, 8f), texture, ScaleMode.ScaleToFit);
                GUI.DrawTextureWithTexCoords(new Rect(0f, 0f, 8f, 8f), texture, new Rect(0f, 0f, 1f, 1f));
                GuiRender.DrawTexture(new Rect(0f, 0f, 8f, 8f), texture, new Rect(0f, 0f, 1f, 1f), Color.white);
            });
            var item = Assert.Single(f.Collect());
            Assert.False(item.OpaqueImgui);
            Assert.Equal(new[] { 24 }, f.BatchIndexCounts());
        }

        // ------------------------------------------------------------------ call-site plumbing

        /// <summary>Documented: every overload with alphaBlend takes it. Each alphaBlend false call
        /// form reaches one opaque draw: the plain quad (all scale modes, imageAspect), the
        /// border/rounded-corner triangles (float and Vector4 forms) and DrawTextureWithTexCoords.
        /// b3398e3b: every form drew blended (one warning). Red, per case: in GUI.DrawTexture pass
        /// <c>true</c> instead of <c>alphaBlend</c> to GuiRender.DrawTexture (cases 0-3) or to
        /// GuiRender.DrawTriangles (cases 4-7); in DrawTextureWithTexCoords pass <c>true</c> (case 8).</summary>
        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(3)]
        [InlineData(4)]
        [InlineData(5)]
        [InlineData(6)]
        [InlineData(7)]
        [InlineData(8)]
        public void EveryAlphaBlendFalseCallFormDrawsOpaque(int form)
        {
            using var f = new Fixture();
            var texture = f.Texture(604);
            var rect = new Rect(4f, 6f, 40f, 30f);
            f.Repaint(() =>
            {
                switch (form)
                {
                    case 0: GUI.DrawTexture(rect, texture, ScaleMode.StretchToFill, false); break;
                    case 1: GUI.DrawTexture(rect, texture, ScaleMode.ScaleToFit, false, 2f); break;
                    case 2: GUI.DrawTexture(rect, texture, ScaleMode.ScaleAndCrop, false, 0.5f); break;
                    case 3: GUI.DrawTexture(rect, texture, ScaleMode.StretchToFill, false, 0f, Color.red, 0f, 0f); break;
                    case 4: GUI.DrawTexture(rect, texture, ScaleMode.StretchToFill, false, 0f, Color.white, 3f, 0f); break;
                    case 5: GUI.DrawTexture(rect, texture, ScaleMode.StretchToFill, false, 0f, Color.white, 0f, 6f); break;
                    case 6: GUI.DrawTexture(rect, texture, ScaleMode.StretchToFill, false, 0f, Color.white, new Vector4(1f, 2f, 3f, 4f), 5f); break;
                    case 7: GUI.DrawTexture(rect, texture, ScaleMode.StretchToFill, false, 0f, Color.white, new Vector4(2f, 2f, 2f, 2f), new Vector4(8f, 0f, 4f, 2f)); break;
                    case 8: GUI.DrawTextureWithTexCoords(rect, texture, new Rect(0.25f, 0.25f, 0.5f, 0.5f), false); break;
                }
            });
            var item = Assert.Single(f.Collect());
            Assert.True(item.OpaqueImgui);
            Assert.True(Assert.Single(f.BatchIndexCounts()) > 0);
        }

        // ------------------------------------------------------------------ clipping, matrix, geometry

        /// <summary>Unbound's choice (unmeasured): an opaque draw is clipped, transformed by the group
        /// offset and GUI.matrix, textured and tinted exactly as the blended draw of the same call.
        /// b3398e3b: a single blended path, so there was nothing to differ; this guards the new
        /// branch. Red: give the opaque branch its own geometry, e.g. in Quad add <c>tl</c> instead of
        /// <c>GUIClip.GUIToScreen(tl)</c> when <c>opaque</c> (positions differ), or skip the
        /// <c>CurrentClip().width &lt;= 0</c> early-out for opaque quads (the fully clipped call
        /// adds a batch).</summary>
        [Fact]
        public void OpaqueDrawsAreClippedAndTransformedAsBlendedOnes()
        {
            using var f = new Fixture();
            var blended = f.Texture(605);
            var opaque = f.Texture(606);
            var rect = new Rect(1f, 2f, 3f, 4f);
            f.Repaint(() =>
            {
                GUIClip.Push(new Rect(10f, 20f, 50f, 30f), Vector2.zero, Vector2.zero, false);
                GUI.matrix = Matrix4x4.TRS(new Vector3(5f, 7f, 0f), Quaternion.identity, new Vector3(2f, 2f, 1f));
                GUI.color = new Color(1f, 0.5f, 0.25f, 0.75f);
                GUI.DrawTexture(rect, blended, ScaleMode.StretchToFill, true);
                GUI.DrawTexture(rect, opaque, ScaleMode.StretchToFill, false);
                GUI.matrix = Matrix4x4.identity;
                // Fully clipped: neither path draws.
                GUIClip.Push(new Rect(100f, 0f, 0f, 10f), Vector2.zero, Vector2.zero, false);
                GUI.DrawTexture(rect, opaque, ScaleMode.StretchToFill, false);
                GUIClip.Pop();
                GUIClip.Pop();
            });
            var items = f.Collect();
            Assert.Equal(2, items.Count);
            Assert.False(items[0].OpaqueImgui);
            Assert.True(items[1].OpaqueImgui);
            Assert.Equal(items[0].Clip, items[1].Clip);
            Assert.True(items[1].ClipEnabled);
            var positions = f.Positions();
            var uvs = f.Uvs();
            var colors = f.Colors();
            Assert.Equal(8, positions.Count);
            for (int i = 0; i < 4; i++)
            {
                Assert.Equal(positions[i], positions[i + 4]);
                Assert.Equal(uvs[i], uvs[i + 4]);
                Assert.Equal(colors[i], colors[i + 4]);
            }
            // (1, 2) in the group at (10, 20), then scaled by 2 and moved by (5, 7).
            Assert.Equal(new Vector3(27f, 51f, 0f), positions[4]);
        }

        // ------------------------------------------------------------------ command and state selection

        /// <summary>Unbound's choice (unmeasured): the opaque draw alone gets a Blend-only override,
        /// One Zero for color and alpha with every channel written (the renderer's Blend Off), and
        /// the previous override (none) is put back before the next draw. b3398e3b: no override at
        /// all, every draw blended. Red: remove the BeginOpaqueImgui call in EmitDraw (the middle
        /// draw has no override), or remove EndOpaqueImgui (the third draw keeps blending off).</summary>
        [Fact]
        public void OnlyTheOpaqueDrawGetsBlendingOffAndTheOverrideIsPutBack()
        {
            using var f = new Fixture();
            var draws = f.Emit(f.Item(701, false), f.Item(702, true), f.Item(703, false));
            Assert.Equal(new ulong[] { 701, 702, 703 }, draws.Select(d => d.Mesh).ToArray());
            Assert.False(draws[0].Override.HasValue);
            Assert.False(draws[2].Override.HasValue);
            Assert.True(draws[1].Override.HasValue);
            var state = draws[1].Override.Value;
            Assert.Equal(RenderStateMask.Blend, state.Mask);
            AssertOpaqueBlend(state);
            Assert.Null(GfxStream.CurrentOverride);
        }

        /// <summary>Unbound's choice (unmeasured): over an override already in force (depth and
        /// stencil, as a renderer list's block sets), the opaque draw changes only the blend state,
        /// keeps the stencil test and reference, and the outer override is put back exactly. b3398e3b:
        /// the draw ran under the outer override, blended. Red: start the block from
        /// <c>new RenderStateBlock(RenderStateMask.Nothing)</c> instead of <c>previous ??</c> (stencil
        /// lost), or restore <c>null</c> in EndOpaqueImgui (outer override dropped).</summary>
        [Fact]
        public void AnOuterStencilOverrideIsKeptForTheOpaqueDrawAndRestoredAfter()
        {
            using var f = new Fixture();
            var outer = new RenderStateBlock(RenderStateMask.Depth | RenderStateMask.Stencil)
            {
                depthState = new DepthState(false, CompareFunction.Always),
                stencilReference = 7,
                stencilState = new StencilState(true, 31, 15, CompareFunction.Equal, StencilOp.Replace),
            };
            GfxStream.StateOverride(outer);
            var draws = f.Emit(f.Item(711, true), f.Item(712, false));
            Assert.True(draws[0].Override.HasValue);
            var opaque = draws[0].Override.Value;
            Assert.Equal(RenderStateMask.Blend | RenderStateMask.Depth | RenderStateMask.Stencil, opaque.Mask);
            AssertOpaqueBlend(opaque);
            Assert.Equal(CompareFunction.Always, opaque.DepthComparison);
            Assert.True(opaque.StencilEnabled);
            Assert.Equal(7, opaque.StencilReference);
            Assert.Equal((byte)31, opaque.StencilRead);
            Assert.Equal((byte)15, opaque.StencilWrite);
            Assert.Equal(CompareFunction.Equal, opaque.StencilComparison);
            Assert.Equal(StencilOp.Replace, opaque.StencilPass);
            Assert.True(draws[1].Override.HasValue);
            var after = draws[1].Override.Value;
            Assert.Equal(RenderStateMask.Depth | RenderStateMask.Stencil, after.Mask);
            Assert.Equal(7, after.StencilReference);
            Assert.Equal(CompareFunction.Equal, after.StencilComparison);
            Assert.Equal((RenderStateBlock?)outer, GfxStream.CurrentOverride);
        }

        /// <summary>Unbound's choice (unmeasured): a draw that is both masked and opaque gets the
        /// mask's stencil test with blending off; the opaque scope closes inside the mask's, so the
        /// overrides come back in reverse order and none remains. (IMGUI draws themselves are never
        /// masked; this guards the composition with the mask override.) b3398e3b: masked and blended.
        /// Red: restore <c>null</c> in EndOpaqueImgui (the override sequence loses the mask block
        /// between the draw and the mask's After), or begin the opaque scope before
        /// SpriteMasking.Before (the mask's After then restores the opaque block, which stays in
        /// force).</summary>
        [Fact]
        public void AMaskedOpaqueDrawNestsInsideTheMaskOverride()
        {
            using var f = new Fixture();
            var item = f.Item(721, true);
            item.Masked = f.MaskedRenderer(SpriteMaskInteraction.VisibleInsideMask);
            var overrides = new List<State?>();
            var draws = f.Emit(overrides, item);
            var draw = Assert.Single(draws);
            Assert.True(draw.Override.HasValue);
            var state = draw.Override.Value;
            Assert.Equal(RenderStateMask.Blend | RenderStateMask.Stencil, state.Mask);
            AssertOpaqueBlend(state);
            Assert.Equal(1, state.StencilReference);
            Assert.Equal(CompareFunction.LessEqual, state.StencilComparison);
            // Mask on; mask + opaque; (draw); mask again; nothing.
            Assert.Equal(4, overrides.Count);
            Assert.Equal((RenderStateMask?)RenderStateMask.Stencil, overrides[0]?.Mask);
            Assert.Equal((RenderStateMask?)(RenderStateMask.Blend | RenderStateMask.Stencil), overrides[1]?.Mask);
            Assert.Equal((RenderStateMask?)RenderStateMask.Stencil, overrides[2]?.Mask);
            Assert.False(overrides[3].HasValue);
            Assert.Null(GfxStream.CurrentOverride);
        }

        static void AssertOpaqueBlend(State state)
        {
            Assert.Equal((byte)ColorWriteMask.All, state.WriteMask);
            Assert.Equal(new[] { BlendMode.One, BlendMode.Zero, BlendMode.One, BlendMode.Zero }, state.Factors);
            Assert.Equal(new[] { BlendOp.Add, BlendOp.Add }, state.Ops);
        }

        // ------------------------------------------------------------------ fixture

        internal readonly record struct State(RenderStateMask Mask, byte WriteMask, BlendMode[] Factors, BlendOp[] Ops,
            CompareFunction DepthComparison, bool StencilEnabled, int StencilReference, byte StencilRead, byte StencilWrite,
            CompareFunction StencilComparison, StencilOp StencilPass);

        internal readonly record struct Draw(ulong Mesh, State? Override);

        /// <summary>
        /// An OnGUI Repaint context and a command-stream recorder, with every touched engine static
        /// saved and put back: the GUI clip stack and matrix, GUI color/enabled, Event.current, the
        /// command stream (bytes, state, override), FrameBuilder's items and sequence, and the sprite
        /// mask state. The IMGUI batches are cleared before and after (each frame's Repaint clears
        /// them too). No native engine; synthetic handles only; nothing is flushed.
        /// </summary>
        internal sealed class Fixture : IDisposable
        {
            readonly List<Action> restore = new List<Action>();
            internal readonly Material UiMaterial;
            internal readonly int UiMaterialQueue;

            internal Fixture()
            {
                try
                {
                    Assert.False(Native.IsReady);
                    Save(typeof(ObjectRegistry), "s_Objects");
                    Save(typeof(ObjectRegistry), "s_NextRuntimeId", "s_NextAssetId");
                    Save(typeof(Shader), "s_PropertyIds", "s_PropertyNames", "s_TagIds", "s_TagNames", clear: false);
                    Save(typeof(ShaderPassInfo), "s_Cache");
                    Save(typeof(GfxStream), "s_Buffer", "s_Length", "s_State", "s_PassTags", "s_Properties", "<CurrentOverride>k__BackingField");
                    Save(typeof(FrameBuilder), "s_Items", "s_DrawKeywords", "s_DrawDescriptions", "s_PassTagScratch");
                    Save(typeof(FrameBuilder), "s_MaterialHandles", "s_DrawCount", "s_Sequence");
                    Save(typeof(SpriteMasking), "s_Camera", "s_Context", "s_Masks", "s_Stencil", "s_Wanted");
                    Save(typeof(GUIClip), "s_Stack", "s_Matrix", "s_Inverse");
                    Save(typeof(GUI), "s_Color", "s_Enabled");
                    Save(typeof(Event), "s_Current");
                    GuiRender.Clear();
                    restore.Add(GuiRender.Clear);
                    UiMaterial = Material(3000, 801);
                    UiMaterialQueue = UiMaterial.renderQueue;
                    GfxStream.SetTarget(901, 902);
                    GfxStream.Scissor(null);
                    GfxStream.StateOverride(null);
                }
                catch
                {
                    Dispose();
                    throw;
                }
            }

            internal Texture2D Texture(ulong handle)
            {
                var texture = (Texture2D)Activator.CreateInstance(typeof(Texture2D), true);
                texture.m_Width = texture.m_Height = 4;
                texture.m_NativeHandle = handle;
                return texture;
            }

            /// <summary>Runs <paramref name="draw"/> as a Repaint over a 200x100 screen.</summary>
            internal void Repaint(Action draw)
            {
                Event.current = new Event { type = EventType.Repaint };
                GUIClip.Begin(new Rect(0f, 0f, 200f, 100f));
                GUI.color = Color.white;
                GUI.enabled = true;
                draw();
            }

            internal List<FrameBuilder.DrawItem> Collect()
            {
                var items = new List<FrameBuilder.DrawItem>();
                GuiRender.CollectItems(1001, UiMaterial, 100f, items);
                return items;
            }

            internal int[] BatchIndexCounts() => ((IList)Field(typeof(GuiRender), "s_Batches").GetValue(null)).Cast<object>()
                .Select(b => ((List<int>)b.GetType().GetField("Indices", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(b)).Count)
                .ToArray();

            internal List<Vector3> Positions() => (List<Vector3>)Field(typeof(GuiRender), "s_Positions").GetValue(null);
            internal List<Vector2> Uvs() => (List<Vector2>)Field(typeof(GuiRender), "s_Uvs").GetValue(null);
            internal List<Color32> Colors() => (List<Color32>)Field(typeof(GuiRender), "s_Colors").GetValue(null);

            /// <summary>An IMGUI-like overlay draw item (as CollectItems makes) of a synthetic mesh.</summary>
            internal FrameBuilder.DrawItem Item(ulong mesh, bool opaque) => new FrameBuilder.DrawItem
            {
                Mesh = mesh, Material = UiMaterial, Model = Matrix4x4.identity, Color = Color.white,
                MainTexture = 601, Clip = new Rect(0f, 0f, 10f, 10f), ClipEnabled = true,
                OpaqueImgui = opaque, Queue = UiMaterialQueue,
            };

            /// <summary>A sprite renderer with a mask interaction, and no masks in the scene: the
            /// mask's Before only sets its stencil test.</summary>
            internal Renderer MaskedRenderer(SpriteMaskInteraction interaction)
            {
                var go = new GameObject("opaque gui fixture", true);
                var transform = (Transform)Activator.CreateInstance(typeof(Transform), true);
                transform.m_Destroyed = false;
                transform.m_GameObject = go;
                go.m_Transform = transform;
                go.m_Components.Add(transform);
                var renderer = (SpriteRenderer)Activator.CreateInstance(typeof(SpriteRenderer), true);
                renderer.m_Destroyed = false;
                renderer.m_GameObject = go;
                go.m_Components.Add(renderer);
                renderer.maskInteraction = interaction;
                var masks = Field(typeof(SpriteMasking), "s_Masks");
                masks.SetValue(null, Activator.CreateInstance(masks.FieldType));
                return renderer;
            }

            internal List<Draw> Emit(params FrameBuilder.DrawItem[] items) => Emit(new List<State?>(), items);

            /// <summary>The items through FrameBuilder's emission; the draws with the state override
            /// each one got, and every override set on the way (<paramref name="overrides"/>).</summary>
            internal List<Draw> Emit(List<State?> overrides, params FrameBuilder.DrawItem[] items)
            {
                var list = (List<FrameBuilder.DrawItem>)Field(typeof(FrameBuilder), "s_Items").GetValue(null);
                list.Clear();
                list.AddRange(items);
                Field(typeof(GfxStream), "s_Length").SetValue(null, 0);
                typeof(FrameBuilder).GetMethod("EmitItems", BindingFlags.Static | BindingFlags.NonPublic)
                    .Invoke(null, new object[] { 0, items.Length, Type.Missing });
                return Decode(GfxStream.Recorded.ToArray(), overrides);
            }

            static Material Material(int queue, ulong handle)
            {
                var shader = Shader.Create("Opaque GUI fixture", queue);
                var info = new ShaderPassInfo { Found = true, DrawnShader = shader.name };
                info.Passes.Add(("", new Dictionary<string, string>()));
                ((IDictionary)Field(typeof(ShaderPassInfo), "s_Cache").GetValue(null))[(shader.DrawnName, Shader.globalRenderPipeline)] = info;
                return new Material(shader) { m_Handle = handle, m_Dirty = false };
            }

            static FieldInfo Field(Type type, string name) => type.GetField(name,
                BindingFlags.Static | BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
                ?? throw new InvalidOperationException($"Missing fixture field {type.Name}.{name}");

            void Save(Type type, params string[] names) => Save(type, names, true);
            void Save(Type type, string a, string b, string c, string d, bool clear) => Save(type, new[] { a, b, c, d }, clear);
            void Save(Type type, string[] names, bool clear)
            {
                foreach (var name in names)
                {
                    var field = Field(type, name);
                    var value = field.GetValue(null);
                    if (value is Array array)
                    {
                        var copy = (Array)array.Clone();
                        restore.Add(() => { Array.Copy(copy, array, copy.Length); field.SetValue(null, array); });
                    }
                    else if (value is IDictionary dictionary)
                    {
                        var entries = dictionary.Keys.Cast<object>().Select(key => new DictionaryEntry(key, dictionary[key])).ToArray();
                        var lists = entries.Select(e => e.Value).OfType<IList>().Select(l => (List: l, Items: l.Cast<object>().ToArray())).ToArray();
                        restore.Add(() =>
                        {
                            foreach (var list in lists) { list.List.Clear(); foreach (var entry in list.Items) list.List.Add(entry); }
                            dictionary.Clear();
                            foreach (var entry in entries) dictionary.Add(entry.Key, entry.Value);
                            if (!field.IsInitOnly) field.SetValue(null, value);
                        });
                        if (clear) dictionary.Clear();
                    }
                    else if (value is IList list)
                    {
                        var entries = list.Cast<object>().ToArray();
                        restore.Add(() =>
                        {
                            list.Clear();
                            foreach (var entry in entries) list.Add(entry);
                            if (!field.IsInitOnly) field.SetValue(null, value);
                        });
                        if (clear) list.Clear();
                    }
                    else restore.Add(() => field.SetValue(null, value));
                }
            }

            public void Dispose()
            {
                // Restores the original pending bytes too. No Flush, Destroy or native release.
                for (int i = restore.Count - 1; i >= 0; i--) restore[i]();
            }

            /// <summary>Decodes the stream protocol's StateOverride, Draw and PassTags commands; each
            /// draw carries the override in force when it was recorded.</summary>
            static List<Draw> Decode(byte[] bytes, List<State?> overrides)
            {
                using var input = new BinaryReader(new MemoryStream(bytes));
                var draws = new List<Draw>();
                State? current = null;
                void Skip(int count) => input.ReadBytes(count);
                while (input.BaseStream.Position < input.BaseStream.Length)
                {
                    switch (input.ReadByte())
                    {
                        case 18: // StateOverride
                            if (input.ReadByte() == 0)
                            {
                                current = null;
                                overrides.Add(null);
                                break;
                            }
                            var mask = (RenderStateMask)input.ReadInt32();
                            byte write = input.ReadByte();
                            var factors = new[] { (BlendMode)input.ReadByte(), (BlendMode)input.ReadByte(), (BlendMode)input.ReadByte(), (BlendMode)input.ReadByte() };
                            var ops = new[] { (BlendOp)input.ReadByte(), (BlendOp)input.ReadByte() };
                            Skip(1 + 4 + 4 + 1); // cull, offset units, offset factor, depth write
                            var depth = (CompareFunction)input.ReadByte();
                            bool enabled = input.ReadByte() != 0;
                            int reference = input.ReadByte();
                            byte read = input.ReadByte();
                            byte stencilWrite = input.ReadByte();
                            var comparison = (CompareFunction)input.ReadByte();
                            var pass = (StencilOp)input.ReadByte();
                            Skip(6);
                            current = new State(mask, write, factors, ops, depth, enabled, reference, read, stencilWrite, comparison, pass);
                            overrides.Add(current);
                            break;
                        case 9: // Draw
                            ulong mesh = input.ReadUInt64();
                            Skip(4 + 8 + 4 + 64 + 16 + 8 + 1 + 16);
                            Assert.Equal(0, input.ReadInt32()); // no per-draw properties
                            draws.Add(new Draw(mesh, current));
                            break;
                        case 16: // PassTags
                            int count = input.ReadInt32();
                            for (int i = 0; i < count; i++) Skip(input.ReadInt32());
                            break;
                        default: throw new InvalidOperationException($"Unexpected command at {input.BaseStream.Position - 1}");
                    }
                }
                return draws;
            }
        }
    }
}
