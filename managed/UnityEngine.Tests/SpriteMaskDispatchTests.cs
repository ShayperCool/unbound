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
    [CollectionDefinition("Sprite mask dispatch", DisableParallelization = true)]
    public class SpriteMaskDispatchCollection { }

    // Source preparation only: these tests must be built/run in Root's combined managed batch.
    [Collection("Sprite mask dispatch")]
    public class SpriteMaskDispatchTests
    {
        static SpriteMaskDispatchTests()
        {
            // Normal one-time engine initialization must survive these tests. In particular, do
            // not remove the permanent FrameBuilder registrations after its static ctor has run.
            foreach (var type in new[] { typeof(FrameBuilder), typeof(SpriteMasking), typeof(Material) })
                RuntimeHelpers.RunClassConstructor(type.TypeHandle);
        }

        [Theory]
        [InlineData(SpriteMaskInteraction.VisibleInsideMask, CompareFunction.LessEqual)]
        [InlineData(SpriteMaskInteraction.VisibleOutsideMask, CompareFunction.Greater)]
        public void TaggedOpaqueEmissionKeepsTheVisibilityTestWithoutANewTransparentMaskWrite(
            SpriteMaskInteraction interaction, CompareFunction comparison)
        {
            using var f = new Fixture();
            f.Renderer.maskInteraction = interaction;
            var original = new RenderStateBlock(RenderStateMask.Depth | RenderStateMask.Stencil)
            {
                depthState = new DepthState(false, CompareFunction.Always),
                stencilReference = 7,
                stencilState = new StencilState(true, 31, 15, CompareFunction.Equal, StencilOp.Replace),
            };
            GfxStream.StateOverride(original);
            var draw = Assert.Single(f.Emit(RenderDispatchContext.BuiltinForwardOpaque));
            Assert.Equal(f.Item.Mesh, draw.Mesh);
            Assert.Equal(1, draw.Stencil.Reference);
            Assert.Equal(comparison, draw.Stencil.Comparison);
            Assert.True(draw.Stencil.Enabled);
            Assert.Equal(255, draw.Stencil.ReadMask);
            Assert.Equal(0, draw.Stencil.WriteMask);
            Assert.Equal(StencilOp.Keep, draw.Stencil.Pass);
            Assert.Equal(RenderStateMask.Depth | RenderStateMask.Stencil, draw.Stencil.Mask);
            Assert.Equal(CompareFunction.Always, draw.Stencil.DepthComparison);
            Assert.Equal((RenderStateBlock?)original, GfxStream.CurrentOverride);
        }

        [Theory]
        [InlineData(2000)]
        [InlineData(3000)]
        public void UntaggedOpaqueAndTransparentEmissionKeepExistingMaskWrites(int queue)
        {
            using var f = new Fixture();
            f.Item.Queue = queue;
            var original = new RenderStateBlock(RenderStateMask.Depth)
                { depthState = new DepthState(true, CompareFunction.Greater) };
            GfxStream.StateOverride(original);
            AssertMaskThenRenderer(f, f.Emit());
            Assert.Equal((RenderStateBlock?)original, GfxStream.CurrentOverride);
        }

        [Theory]
        [InlineData(3000, -1, false)]
        [InlineData(3000, 2500, true)]
        [InlineData(2000, 2501, false)]
        [InlineData(2000, -1, true)]
        public void OnlyTheEffectiveMaskQueueConstrainsNewTaggedOpaqueWrites(int shaderQueue, int customQueue, bool writes)
        {
            using var f = new Fixture();
            f.Mask.sharedMaterial.shader.m_RenderQueue = shaderQueue;
            f.Mask.sharedMaterial.renderQueue = customQueue;
            var draws = f.Emit(RenderDispatchContext.BuiltinForwardOpaque);
            if (writes) AssertMaskThenRenderer(f, draws);
            else Assert.Equal(f.Item.Mesh, Assert.Single(draws).Mesh);
        }

        [Fact]
        public void APreviouslyWrittenMaskSurvivesStageFilteringAndCameraBeginOnItsOwnTarget()
        {
            using var f = new Fixture();
            AssertMaskThenRenderer(f, f.Emit()); // Earlier explicit/untagged contribution.
            // Begin does not clear a retained target's stencil, including a different camera.
            var camera = f.Component<Camera>();
            SpriteMasking.Begin(camera, null);
            Assert.Equal(f.Item.Mesh, Assert.Single(f.Emit(RenderDispatchContext.BuiltinForwardOpaque)).Mesh);
            GfxStream.SetTarget(903, 904);
            Assert.Equal(f.Item.Mesh, Assert.Single(f.Emit(RenderDispatchContext.BuiltinForwardOpaque)).Mesh);
            GfxStream.SetTarget(901, 902);
            Assert.Equal(f.Item.Mesh, Assert.Single(f.Emit(RenderDispatchContext.BuiltinForwardOpaque)).Mesh);
            // A subsequent untagged draw also finds the original contribution, without rewriting it.
            Assert.Equal(f.Item.Mesh, Assert.Single(f.Emit()).Mesh);
        }

        [Theory]
        [InlineData(GfxStream.ClearDepth)]
        [InlineData(GfxStream.ClearStencil)]
        public void DepthOrStencilClearDropsTrackingWhileColorClearRetainsIt(byte flags)
        {
            using var f = new Fixture();
            AssertMaskThenRenderer(f, f.Emit());
            GfxStream.Clear(GfxStream.ClearColor, Color.black);
            Assert.Single(f.Emit(RenderDispatchContext.BuiltinForwardOpaque));
            GfxStream.Clear(flags, Color.black);
            Assert.Single(f.Emit(RenderDispatchContext.BuiltinForwardOpaque));
            AssertMaskThenRenderer(f, f.Emit()); // The clear permits a new write on the old path.
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void ExistingRangeAndGroupExitsStillRemoveTrackedContributions(bool group)
        {
            using var f = new Fixture();
            if (group)
            {
                var sorting = f.Component<SortingGroup>(f.Mask.gameObject);
                f.Item.Groups = new[] { new FrameBuilder.SortLevel { Id = sorting.GetInstanceID() } };
                SpriteMasking.Begin(null, new FrameBuilder.CameraContext());
            }
            else
            {
                f.Mask.isCustomRangeActive = true;
                f.Mask.backSortingOrder = -1;
                f.Mask.frontSortingOrder = 1;
            }
            AssertMaskThenRenderer(f, f.Emit());
            if (group) f.Item.Groups = null;
            else f.Item.SortingOrder = 2;
            var draws = f.Emit(RenderDispatchContext.BuiltinForwardOpaque);
            Assert.Equal(2, draws.Count);
            Assert.Equal(f.MaskMesh.m_Handle, draws[0].Mesh);
            Assert.Equal(StencilOp.DecrementSaturate, draws[0].Stencil.Pass);
            Assert.Equal(f.Item.Mesh, draws[1].Mesh);
        }

        [Fact]
        public void OpaqueContextDoesNotBecomeAmbientForCallbacksOrRendererLists()
        {
            using var f = new Fixture();
            Assert.Single(f.Emit(RenderDispatchContext.BuiltinForwardOpaque));
            var camera = f.Component<Camera>();
            Camera.onPostRender = _ => AssertMaskThenRenderer(f, f.EmitForward());
            Camera.onPostRender(camera);

            GfxStream.SetTarget(903, 904);
            SpriteMasking.Begin(camera, null);
            Assert.Single(f.Emit(RenderDispatchContext.BuiltinForwardOpaque));
            // An actual renderer-list entry point on a fresh stencil, with the SAME opaque item.
            // Its default dispatch must remain Unknown even after a tagged emission.
            var source = new Source { Item = f.Item };
            FrameBuilder.Sources.Add(source);
            var data = new CullingData { Camera = camera };
            var sorting = new SortingSettings(camera);
            var tags = new List<ShaderTagId> { new ShaderTagId("SRPDefaultUnlit") };
            var drawing = new DrawingSettings(tags[0], sorting);
            var filtering = FilteringSettings.defaultValue;
            AssertMaskThenRenderer(f, f.Record(() => FrameBuilder.DrawRendererList(data, drawing, filtering, tags)));
        }

        [Fact]
        public void FixtureRestoresTheOriginalRegistrationsPendingCommandsAndTrackedStencil()
        {
            using var outer = new Fixture();
            AssertMaskThenRenderer(outer, outer.Emit());
            var pending = GfxStream.Recorded.ToArray();
            var state = GfxStream.Current;
            var renderers = RenderRegistry.Renderers.ToArray();
            var indices = renderers.Select(RenderRegistry.Renderers.IndexOf).ToArray();
            var objects = ObjectRegistry.All(typeof(Object)).ToArray();
            var source = new Source();
            FrameBuilder.Sources.Add(source);
            Camera.CameraCallback callback = _ => { };
            Camera.onPostRender = callback;
            using (var inner = new Fixture())
            {
                Assert.Single(inner.Emit(RenderDispatchContext.BuiltinForwardOpaque));
                Camera.onPostRender = null;
            }
            Assert.Equal(pending, GfxStream.Recorded.ToArray());
            Assert.Equal(state, GfxStream.Current);
            Assert.Equal(renderers, RenderRegistry.Renderers.ToArray());
            Assert.Equal(indices, renderers.Select(RenderRegistry.Renderers.IndexOf));
            Assert.Equal(objects, ObjectRegistry.All(typeof(Object)));
            Assert.Same(source, Assert.Single(FrameBuilder.Sources));
            Assert.Same(callback, Camera.onPostRender);
            Assert.Equal(outer.Item.Mesh, Assert.Single(outer.Emit(RenderDispatchContext.BuiltinForwardOpaque)).Mesh);
        }

        static void AssertMaskThenRenderer(Fixture f, List<Draw> draws)
        {
            Assert.Equal(2, draws.Count);
            Assert.Equal(f.MaskMesh.m_Handle, draws[0].Mesh);
            Assert.Equal(StencilOp.IncrementSaturate, draws[0].Stencil.Pass);
            Assert.Equal(255, draws[0].Stencil.WriteMask);
            Assert.Equal(f.Item.Mesh, draws[1].Mesh);
            Assert.Equal(CompareFunction.LessEqual, draws[1].Stencil.Comparison);
            Assert.Equal(0, draws[1].Stencil.WriteMask);
        }

        sealed class Source : FrameBuilder.IDrawSource
        {
            internal FrameBuilder.DrawItem Item;
            public void CollectCameraDraws(FrameBuilder.CameraContext camera, List<FrameBuilder.DrawItem> into) => into.Add(Item);
            public void CollectOverlayDraws(List<FrameBuilder.DrawItem> into) { }
        }

        sealed class Fixture : IDisposable
        {
            readonly List<Action> restore = new List<Action>();
            internal readonly SpriteMask Mask;
            internal readonly SpriteRenderer Renderer;
            internal readonly Mesh MaskMesh;
            internal FrameBuilder.DrawItem Item;

            internal Fixture()
            {
                try
                {
                    // Never submit to an installed engine. These are synthetic, already uploaded handles.
                    Assert.False(Native.IsReady);
                    Save(typeof(PlayerLoopDriver), "s_Engine", clear: false);
                    Save(typeof(ObjectRegistry), "s_Objects");
                    Save(typeof(ObjectRegistry), "s_NextRuntimeId", "s_NextAssetId");
                    Save(typeof(Shader), "s_PropertyIds", "s_PropertyNames", "s_TagIds", "s_TagNames", clear: false);
                    Save(typeof(ShaderPassInfo), "s_Cache");
                    Save(typeof(GfxStream), "s_Buffer", "s_Length", "s_State", "s_PassTags", "s_Properties", "<CurrentOverride>k__BackingField");
                    Save(typeof(FrameBuilder), "s_Items", "Sources", "s_DrawKeywords", "s_DrawDescriptions", "s_PassTagScratch");
                    Save(typeof(FrameBuilder), "s_MaterialHandles", "s_DrawCount");
                    Save(typeof(SpriteMasking), "s_Camera", "s_Context", "s_Masks", "s_Stencil", "s_Wanted", "s_MaskProperties");
                    Save(typeof(Camera), "onPostRender");
                    Save(RenderRegistry.Renderers, "m_Items", "m_Index", "m_Holes");
                    // No scene roots, OnAttached, lifecycle messages, player-loop steps or registrations.
                    Mask = Component<SpriteMask>();
                    Renderer = Component<SpriteRenderer>();
                    Renderer.maskInteraction = SpriteMaskInteraction.VisibleInsideMask;
                    Mask.sharedMaterial = Material(3000, 501);
                    MaskMesh = new Mesh { m_Handle = 502, m_Dirty = false };
                    var texture = (Texture2D)Activator.CreateInstance(typeof(Texture2D), true);
                    texture.m_Width = texture.m_Height = 4;
                    texture.m_NativeHandle = 503;
                    Mask.sprite = Sprite.Create(texture, new Rect(0, 0, 4, 4), new Vector2(.5f, .5f), 4);
                    Mask.sprite.m_Mesh = MaskMesh;
                    RenderRegistry.Renderers.Add(Mask);
                    Item = new FrameBuilder.DrawItem
                    {
                        Mesh = 504, Material = Material(2000, 505), Model = Matrix4x4.identity,
                        Color = Color.white, Queue = 2000, Masked = Renderer,
                    };
                    SpriteMasking.Begin(null, null);
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

            internal T Component<T>(GameObject go = null) where T : Component
            {
                go ??= new GameObject("mask dispatch fixture", true);
                // Avoid OnAttached's built-in asset loads and all component lifecycle hooks.
                if (go.m_Transform == null)
                {
                    var transform = (Transform)Activator.CreateInstance(typeof(Transform), true);
                    transform.m_Destroyed = false;
                    transform.m_GameObject = go;
                    go.m_Transform = transform;
                    go.m_Components.Add(transform);
                }
                var component = (T)Activator.CreateInstance(typeof(T), true);
                component.m_Destroyed = false;
                component.m_GameObject = go;
                go.m_Components.Add(component);
                return component;
            }

            static Material Material(int queue, ulong handle)
            {
                var shader = Shader.Create("Mask dispatch fixture", queue);
                var info = new ShaderPassInfo { Found = true, DrawnShader = shader.name };
                info.Passes.Add(("", new Dictionary<string, string>()));
                ((IDictionary)Field(typeof(ShaderPassInfo), "s_Cache").GetValue(null))[(shader.DrawnName, Shader.globalRenderPipeline)] = info;
                return new Material(shader) { m_Handle = handle, m_Dirty = false };
            }

            internal List<Draw> Emit(RenderDispatchContext? dispatch = null)
            {
                var items = (List<FrameBuilder.DrawItem>)Field(typeof(FrameBuilder), "s_Items").GetValue(null);
                items.Clear();
                items.Add(Item);
                return Record(() => Invoke("EmitItems", 0, 1, dispatch.HasValue ? (object)dispatch.Value : Type.Missing));
            }

            internal List<Draw> EmitForward() => Record(() => Invoke("EmitForward", Item, Item.Material.Sync(),
                Type.Missing, Type.Missing, Type.Missing, Type.Missing, Type.Missing, Type.Missing));

            internal List<Draw> Record(Action action)
            {
                Field(typeof(GfxStream), "s_Length").SetValue(null, 0);
                action();
                return Draws(GfxStream.Recorded.ToArray());
            }

            static void Invoke(string method, params object[] args) => typeof(FrameBuilder)
                .GetMethod(method, BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, args);

            static FieldInfo Field(Type type, string name) => type.GetField(name,
                BindingFlags.Static | BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
                ?? throw new InvalidOperationException($"Missing fixture field {type.Name}.{name}");

            void Save(object owner, params string[] names) => Save(owner, names, true);
            void Save(object owner, string name, bool clear) => Save(owner, new[] { name }, clear);
            void Save(object owner, string a, string b, string c, string d, bool clear) => Save(owner, new[] { a, b, c, d }, clear);
            void Save(object owner, string[] names, bool clear)
            {
                var type = owner as Type ?? owner.GetType();
                var target = owner is Type ? null : owner;
                foreach (var name in names)
                {
                    var field = Field(type, name);
                    var value = field.GetValue(target);
                    if (value is Array array)
                    {
                        var copy = (Array)array.Clone();
                        restore.Add(() => { Array.Copy(copy, array, copy.Length); field.SetValue(target, array); });
                    }
                    else if (value is IDictionary dictionary)
                    {
                        var entries = dictionary.Keys.Cast<object>()
                            .Select(key => new DictionaryEntry(key, dictionary[key])).ToArray();
                        // Engine registration values are lists; preserve their identities and contents.
                        var lists = entries.Select(e => e.Value).OfType<IList>()
                            .Select(l => (List: l, Items: l.Cast<object>().ToArray())).ToArray();
                        restore.Add(() =>
                        {
                            foreach (var list in lists) { list.List.Clear(); foreach (var entry in list.Items) list.List.Add(entry); }
                            dictionary.Clear();
                            foreach (var entry in entries) dictionary.Add(entry.Key, entry.Value);
                        });
                        if (clear) dictionary.Clear();
                    }
                    else if (value is IList list)
                    {
                        var entries = list.Cast<object>().ToArray();
                        restore.Add(() => { list.Clear(); foreach (var entry in entries) list.Add(entry); });
                        if (clear) list.Clear();
                    }
                    else restore.Add(() => field.SetValue(target, value));
                }
            }

            public void Dispose()
            {
                // Restores original pending bytes too. No Flush, Destroy, or fake native releases.
                for (int i = restore.Count - 1; i >= 0; i--) restore[i]();
            }
        }

        readonly record struct Stencil(RenderStateMask Mask, bool Enabled, int Reference, byte ReadMask, byte WriteMask,
            CompareFunction Comparison, StencilOp Pass, CompareFunction DepthComparison);
        readonly record struct Draw(ulong Mesh, Stencil Stencil);

        // Decode the existing stream protocol, including variable-size properties. Check the state
        // at each actual Draw, rather than testing a duplicate of the eligibility predicate.
        static List<Draw> Draws(byte[] bytes)
        {
            using var input = new BinaryReader(new MemoryStream(bytes));
            var draws = new List<Draw>();
            Stencil stencil = default;
            void Skip(int count) => input.ReadBytes(count);
            void Text() => Skip(input.ReadInt32());
            while (input.BaseStream.Position < input.BaseStream.Length)
            {
                switch (input.ReadByte())
                {
                    case 18: // StateOverride
                        if (input.ReadByte() == 0) { stencil = default; break; }
                        var mask = (RenderStateMask)input.ReadInt32();
                        Skip(7 + 1 + 4 + 4 + 1);
                        var depth = (CompareFunction)input.ReadByte();
                        bool enabled = input.ReadByte() != 0;
                        int reference = input.ReadByte();
                        byte read = input.ReadByte();
                        byte write = input.ReadByte();
                        var comparison = (CompareFunction)input.ReadByte();
                        var pass = (StencilOp)input.ReadByte();
                        Skip(6);
                        stencil = new Stencil(mask, enabled, reference, read, write, comparison, pass, depth);
                        break;
                    case 9: // Draw
                        ulong mesh = input.ReadUInt64();
                        Skip(4 + 8 + 4 + 64 + 16 + 8 + 1 + 16);
                        int properties = input.ReadInt32();
                        for (int i = 0; i < properties; i++)
                        {
                            Text();
                            Assert.Equal(GfxStream.FloatKind, input.ReadByte()); // mask cutoff
                            Skip(4);
                        }
                        draws.Add(new Draw(mesh, stencil));
                        break;
                    case 16: // PassTags
                        int count = input.ReadInt32();
                        for (int i = 0; i < count; i++) Text();
                        break;
                    default: throw new InvalidOperationException($"Unexpected command at {input.BaseStream.Position - 1}");
                }
            }
            return draws;
        }
    }
}
