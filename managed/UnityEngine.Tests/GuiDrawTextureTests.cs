using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Unbound.Runtime;
using UnityEngine.TextCore;
using UnityEngine.TextCore.LowLevel;
using UnityEngine.TextCore.Text;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>
    /// GUI.DrawTexture and GUI.DrawTextureWithTexCoords through Unity's IMGUI shaders. Documented
    /// (Unity 6000.0 Scripting API): border widths are (left, top, right, bottom), corner radiuses
    /// (top-left, top-right, bottom-right, bottom-left), zero values draw the plain texture;
    /// alphaBlend is "whether to apply alpha blending". Measured (Unity 6000.0.71f1 Editor,
    /// tests/projects/uialphaborders imgui): a draw with widths or radiuses is drawn by the
    /// rounded-rect shader whatever alphaBlend says, a 40 x 20 rect with radius 30 is a capsule,
    /// and a plain draw with alphaBlend false shows the texture's colour blended by the tint's alpha
    /// (the blit shader). The pixels themselves are the probe's; these cases check what reaches the
    /// renderer: shader, order, parameters and placement.
    /// </summary>
    public class GuiDrawTextureTests
    {
        static GuiDrawTextureTests()
        {
            foreach (var type in new[] { typeof(FrameBuilder), typeof(Material) })
                RuntimeHelpers.RunClassConstructor(type.TypeHandle);
        }

        static readonly int RectId = Shader.PropertyToID("_Rect");
        static readonly int RadiusesId = Shader.PropertyToID("_CornerRadiuses");
        static readonly int WidthsId = Shader.PropertyToID("_BorderWidths");
        static readonly int SmoothId = Shader.PropertyToID("_SmoothCorners");

        [Fact]
        public void PlainDrawsTakeTheBlendedOrTheBlitShaderInCallOrder()
        {
            using var f = new Fixture();
            var texture = f.Texture(601);
            var rect = new Rect(10f, 10f, 20f, 20f);
            f.Repaint(() =>
            {
                GUI.DrawTexture(rect, texture, ScaleMode.StretchToFill, true);
                GUI.DrawTexture(rect, texture, ScaleMode.StretchToFill, false);
                GUI.DrawTexture(rect, texture, ScaleMode.StretchToFill, false);
                GUI.DrawTextureWithTexCoords(rect, texture, new Rect(0f, 0f, 1f, 1f), false);
                GUI.DrawTexture(rect, texture, ScaleMode.StretchToFill, true);
            });
            var items = f.Collect();
            // Consecutive blit draws of one texture share a batch, as blended ones do.
            Assert.Equal(new[] { f.Materials[0], f.Materials[2], f.Materials[0] }, items.Select(i => i.Material).ToArray());
            Assert.Equal(new[] { 0, 1, 2 }, items.Select(i => i.Submesh).ToArray());
            Assert.All(items, i => Assert.Equal(Matrix4x4.identity, i.Model));
            Assert.All(items, i => Assert.Null(i.Properties));
        }

        [Fact]
        public void ABorderOrARadiusDrawsWithTheRoundedShaderAndItsParameters()
        {
            using var f = new Fixture();
            var texture = f.Texture(602);
            var rect = new Rect(10f, 20f, 60f, 40f);
            f.Repaint(() => GUI.DrawTexture(rect, texture, ScaleMode.StretchToFill, true, 0f, Color.white,
                new Vector4(1f, 2f, 3f, 4f), new Vector4(5f, 6f, 7f, 8f)));
            var item = Assert.Single(f.Collect());
            Assert.Same(f.Materials[3], item.Material);
            Assert.Equal(602ul, item.MainTexture);
            Assert.Equal(new[] { 10f, 20f, 60f, 40f }, (float[])item.Properties[RectId]);
            Assert.Equal(new[] { 1f, 2f, 3f, 4f }, (float[])item.Properties[WidthsId]);
            Assert.Equal(new[] { 5f, 6f, 7f, 8f }, (float[])item.Properties[RadiusesId]);
            Assert.Equal(1f, item.Properties[SmoothId]);
        }

        [Fact]
        public void AlphaBlendFalseDoesNotChangeARoundedDraw()
        {
            using var f = new Fixture();
            var texture = f.Texture(603);
            f.Repaint(() =>
            {
                GUI.DrawTexture(new Rect(0f, 0f, 40f, 40f), texture, ScaleMode.StretchToFill, false, 0f, Color.white, 0f, 8f);
                GUI.DrawTexture(new Rect(50f, 0f, 40f, 40f), texture, ScaleMode.StretchToFill, false, 0f, Color.white, 2f, 0f);
            });
            Assert.All(f.Collect(), i => Assert.Same(f.Materials[3], i.Material));
        }

        [Fact]
        public void RadiusesStopAtHalfTheShorterSideAndZeroWidthsFillTheRect()
        {
            GUI.DrawTextureShape(new Rect(0f, 0f, 40f, 20f), Vector4.zero, new Vector4(30f, 30f, 30f, 30f), out var widths, out var radiuses);
            Assert.Equal(new Vector4(10f, 10f, 10f, 10f), radiuses);
            Assert.Equal(new Vector4(40f, 20f, 40f, 20f), widths);
            // A control below the limit, with widths given: both kept; negative and NaN values are zero.
            GUI.DrawTextureShape(new Rect(0f, 0f, 40f, 40f), new Vector4(1f, -2f, float.NaN, 4f), new Vector4(3f, -1f, float.NaN, 25f), out widths, out radiuses);
            Assert.Equal(new Vector4(1f, 0f, 0f, 4f), widths);
            Assert.Equal(new Vector4(3f, 0f, 0f, 20f), radiuses);
        }

        [Fact]
        public void ZeroOrNegativeWidthsAndRadiusesKeepThePlainDraw()
        {
            Assert.False(GUI.DrawTextureNeedsShape(Vector4.zero, Vector4.zero));
            Assert.False(GUI.DrawTextureNeedsShape(new Vector4(-1f, -2f, -3f, -4f), new Vector4(float.NaN, 0f, 0f, 0f)));
            Assert.True(GUI.DrawTextureNeedsShape(new Vector4(0f, 0f, 0f, 1f), Vector4.zero));
            Assert.True(GUI.DrawTextureNeedsShape(Vector4.zero, new Vector4(0f, 1f, 0f, 0f)));
        }

        /// <summary>The shader measures pixels against its rect in GUI coordinates: the quad stays in
        /// them and the draw's matrix places it (GUI.matrix, a group's offset, the screen's flip).</summary>
        [Fact]
        public void ARoundedQuadKeepsItsGuiCoordinatesAndItsMatrixPlacesIt()
        {
            using var f = new Fixture();
            var texture = f.Texture(604);
            var rect = new Rect(5f, 6f, 30f, 20f);
            var corners = new List<Vector2>();
            f.Repaint(() =>
            {
                GUI.matrix = Matrix4x4.TRS(new Vector3(3f, 4f, 0f), Quaternion.identity, new Vector3(2f, 2f, 1f));
                GUI.BeginGroup(new Rect(10f, 7f, 100f, 80f));
                GUI.DrawTexture(rect, texture, ScaleMode.StretchToFill, true, 0f, Color.white, 0f, 4f);
                foreach (var p in new[] { rect.min, rect.max }) corners.Add(GUIUtility.GUIToScreenPoint(p));
                GUI.EndGroup();
            });
            var item = Assert.Single(f.Collect());
            var positions = f.Positions();
            Assert.Equal(new Vector3(5f, 6f, 0f), positions[0]);
            Assert.Equal(new Vector3(35f, 26f, 0f), positions[2]);
            var min = item.Model.MultiplyPoint(new Vector3(rect.xMin, rect.yMin, 0f));
            var max = item.Model.MultiplyPoint(new Vector3(rect.xMax, rect.yMax, 0f));
            Assert.Equal(corners[0].x, min.x, 4);
            Assert.Equal(Fixture.Height - corners[0].y, min.y, 4);
            Assert.Equal(corners[1].x, max.x, 4);
            Assert.Equal(Fixture.Height - corners[1].y, max.y, 4);
        }

        [Fact]
        public void EveryRoundedDrawHasItsOwnParametersReusedInTheNextFrame()
        {
            using var f = new Fixture();
            var texture = f.Texture(605);
            f.Repaint(() =>
            {
                GUI.DrawTexture(new Rect(0f, 0f, 40f, 40f), texture, ScaleMode.StretchToFill, true, 0f, Color.white, 0f, 4f);
                GUI.DrawTexture(new Rect(50f, 0f, 40f, 40f), texture, ScaleMode.StretchToFill, true, 0f, Color.white, 0f, 9f);
            });
            var first = f.Collect();
            Assert.Equal(2, first.Count);
            Assert.NotSame(first[0].Properties, first[1].Properties);
            Assert.Equal(4f, ((float[])first[0].Properties[RadiusesId])[0]);
            Assert.Equal(9f, ((float[])first[1].Properties[RadiusesId])[0]);
            var reused = first[0].Properties;
            GuiRender.Clear();
            f.Repaint(() => GUI.DrawTexture(new Rect(0f, 0f, 40f, 40f), texture, ScaleMode.StretchToFill, true, 0f, Color.white, 0f, 6f));
            var second = Assert.Single(f.Collect());
            Assert.Same(reused, second.Properties);
            Assert.Equal(6f, ((float[])second.Properties[RadiusesId])[0]);
        }

        /// <summary>Text and textures are separate draws with their own shaders, stored
        /// unconverted in a linear project, so IMGUI blends in gamma space there
        /// (tests/projects/urpoverlayui imgui). A bitmap glyph atlas draws with the IMGUI text
        /// shader, a distance field atlas with its font asset's own material; glyph quads keep
        /// their place in the rect the text was generated for (y down), their colours, and the
        /// second texture coordinates that mark bold glyphs.</summary>
        [Fact]
        public void TextAndTexturesAreSeparateDrawsStoredUnconvertedInLinear()
        {
            using var f = new Fixture();
            var texture = f.Texture(606);
            var bitmapMaterial = Fixture.Material(3000, 901);
            bitmapMaterial.mainTexture = f.Texture(607, TextureFormat.Alpha8);
            var fieldMaterial = Fixture.Material(3000, 902);
            fieldMaterial.mainTexture = f.Texture(608, TextureFormat.Alpha8);
            var colorMaterial = Fixture.Material(3000, 903);
            colorMaterial.mainTexture = f.Texture(609, TextureFormat.RGBA32);
            var text = new TextInfo
            {
                meshInfo = new[]
                {
                    GlyphQuad(bitmapMaterial, GlyphRenderMode.SMOOTH, new Color32(255, 0, 0, 255), 1f),
                    GlyphQuad(fieldMaterial, GlyphRenderMode.SDFAA, new Color32(0, 255, 0, 128), -1f),
                    GlyphQuad(colorMaterial, GlyphRenderMode.COLOR, new Color32(0, 0, 255, 255), 1f),
                    // A mesh without vertices or without an atlas draws nothing.
                    new MeshInfo { material = fieldMaterial, vertexData = new TextCoreVertex[4], vertexCount = 0, glyphRenderMode = GlyphRenderMode.SDFAA },
                    new MeshInfo { material = null, vertexData = new TextCoreVertex[4], vertexCount = 4, glyphRenderMode = GlyphRenderMode.SDFAA },
                },
            };
            f.Repaint(() =>
            {
                GuiRender.DrawTexture(new Rect(10f, 10f, 20f, 20f), texture, new Rect(0f, 0f, 1f, 1f), Color.white);
                GuiRender.DrawText(new Vector2(10f, 30f), text);
            });
            var items = f.Collect();
            Assert.Equal(new[] { f.Materials[0], f.Materials[1], fieldMaterial, f.Materials[0] }, items.Select(i => i.Material).ToArray());
            bool linear = QualitySettings.activeColorSpace == ColorSpace.Linear;
            Assert.All(items, i => Assert.Equal(linear, i.GammaImgui));

            // The first glyph quad follows the texture's four vertices: top left, top right,
            // bottom right, bottom left, at the origin plus the vertex positions.
            var positions = f.Positions();
            Assert.Equal(new Vector3(11f, 32f), positions[4]);
            Assert.Equal(new Vector3(15f, 32f), positions[5]);
            Assert.Equal(new Vector3(15f, 38f), positions[6]);
            Assert.Equal(new Vector3(11f, 38f), positions[7]);
            var colors = f.List<Color32>("s_Colors");
            Assert.Equal(new Color32(255, 0, 0, 255), colors[4]);
            Assert.Equal(new Color32(0, 255, 0, 128), colors[8]);
            var uvs2 = f.List<Vector2>("s_Uvs2");
            Assert.Equal(positions.Count, uvs2.Count);
            Assert.True(uvs2[0].y > 0f);
            Assert.True(uvs2[4].y > 0f);
            Assert.True(uvs2[8].y < 0f);
        }

        /// <summary>One glyph quad as TextCore fills it: bottom left, top left, top right, bottom right.</summary>
        static MeshInfo GlyphQuad(Material material, GlyphRenderMode mode, Color32 color, float uv2y)
        {
            TextCoreVertex Vertex(float x, float y, float u, float v) =>
                new TextCoreVertex { position = new Vector3(x, y, 0f), uv0 = new Vector2(u, v), uv2 = new Vector2(0f, uv2y), color = color };
            return new MeshInfo
            {
                material = material,
                glyphRenderMode = mode,
                vertexCount = 4,
                vertexData = new[] { Vertex(1f, 8f, 0f, 0f), Vertex(1f, 2f, 0f, 1f), Vertex(5f, 2f, 1f, 1f), Vertex(5f, 8f, 1f, 0f) },
            };
        }

        /// <summary>Isolates GuiRender's batches, GUI state and the IMGUI materials (stand-ins, one
        /// per shader kind) and puts them back afterwards.</summary>
        internal sealed class Fixture : IDisposable
        {
            internal const float Height = 100f;
            readonly List<Action> restore = new List<Action>();
            internal readonly Material[] Materials = new Material[4];

            internal Fixture()
            {
                try
                {
                    Assert.False(Native.IsReady);
                    Save(typeof(ObjectRegistry), "s_Objects", "s_NextRuntimeId", "s_NextAssetId");
                    Save(typeof(ShaderPassInfo), "s_Cache");
                    Save(typeof(FrameBuilder), "s_Sequence");
                    Save(typeof(GUIClip), "s_Stack", "s_Matrix", "s_Inverse");
                    Save(typeof(GUI), "s_Color", "s_Enabled");
                    Save(typeof(Event), "s_Current");
                    Save(typeof(GuiRender), "s_Materials", "s_ToGamma");
                    GuiRender.Clear();
                    restore.Add(GuiRender.Clear);
                    var materials = (Material[])Field(typeof(GuiRender), "s_Materials").GetValue(null);
                    for (int i = 0; i < Materials.Length; i++) materials[i] = Materials[i] = Material(3000, 801 + (ulong)i);
                }
                catch
                {
                    Dispose();
                    throw;
                }
            }

            internal Texture2D Texture(ulong handle, TextureFormat format = TextureFormat.RGBA32)
            {
                var texture = (Texture2D)Activator.CreateInstance(typeof(Texture2D), true);
                texture.m_Width = texture.m_Height = 4;
                texture.m_NativeHandle = handle;
                Field(typeof(Texture2D), "m_Format").SetValue(texture, format);
                return texture;
            }

            internal List<T> List<T>(string name) => (List<T>)Field(typeof(GuiRender), name).GetValue(null);

            /// <summary>Runs <paramref name="draw"/> as a Repaint over a 200 x 100 screen.</summary>
            internal void Repaint(Action draw)
            {
                Event.current = new Event { type = EventType.Repaint };
                GUIClip.Begin(new Rect(0f, 0f, 200f, Height));
                GUI.color = Color.white;
                GUI.enabled = true;
                GUI.matrix = Matrix4x4.identity;
                draw();
            }

            internal List<FrameBuilder.DrawItem> Collect()
            {
                var items = new List<FrameBuilder.DrawItem>();
                GuiRender.CollectItems(1001, Height, items);
                return items;
            }

            internal List<Vector3> Positions() => (List<Vector3>)Field(typeof(GuiRender), "s_Positions").GetValue(null);

            internal static Material Material(int queue, ulong handle)
            {
                var shader = Shader.Create("IMGUI fixture", queue);
                var info = new ShaderPassInfo { Found = true, DrawnShader = shader.name };
                info.Passes.Add(("", new Dictionary<string, string>()));
                ((IDictionary)Field(typeof(ShaderPassInfo), "s_Cache").GetValue(null))[(shader.DrawnName, Shader.globalRenderPipeline)] = info;
                return new Material(shader) { m_Handle = handle, m_Dirty = false };
            }

            static FieldInfo Field(Type type, string name) => type.GetField(name,
                BindingFlags.Static | BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
                ?? throw new InvalidOperationException($"Missing fixture field {type.Name}.{name}");

            void Save(Type type, params string[] names)
            {
                foreach (var name in names)
                {
                    var field = Field(type, name);
                    var value = field.GetValue(null);
                    if (value is Array array)
                    {
                        var copy = (Array)array.Clone();
                        restore.Add(() => Array.Copy(copy, array, copy.Length));
                    }
                    else if (value is IDictionary dictionary)
                    {
                        var entries = dictionary.Keys.Cast<object>().Select(key => new DictionaryEntry(key, dictionary[key])).ToArray();
                        restore.Add(() =>
                        {
                            dictionary.Clear();
                            foreach (var entry in entries) dictionary.Add(entry.Key, entry.Value);
                        });
                    }
                    else if (value is IList list)
                    {
                        var entries = list.Cast<object>().ToArray();
                        restore.Add(() =>
                        {
                            list.Clear();
                            foreach (var entry in entries) list.Add(entry);
                        });
                    }
                    else restore.Add(() => field.SetValue(null, value));
                }
            }

            public void Dispose()
            {
                for (int i = restore.Count - 1; i >= 0; i--) restore[i]();
            }
        }
    }
}
