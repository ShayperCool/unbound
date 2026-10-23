using System;
using System.Collections.Generic;
using Unbound.Runtime;

namespace UnityEngine
{
    /// <summary>
    /// The clip stack of UnityGUI: each group (BeginGroup, scroll views, windows) clips to its rect
    /// and moves the origin of the coordinates inside it. Rects here are "absolute": GUI pixels from
    /// the top left of the screen before <see cref="GUI.matrix"/>. The current event's mouse position
    /// follows the origin, as Unity's does.
    /// </summary>
    internal sealed partial class GUIClip
    {
        struct Entry
        {
            internal Rect Rect;
            internal Rect Visible;
            internal Vector2 Offset;
        }

        static readonly List<Entry> s_Stack = new List<Entry>();
        static Matrix4x4 s_Matrix = Matrix4x4.identity;
        static Matrix4x4 s_Inverse = Matrix4x4.identity;

        /// <summary>Starts a GUI pass over the whole screen.</summary>
        internal static void Begin(Rect screen)
        {
            s_Stack.Clear();
            s_Stack.Add(new Entry { Rect = screen, Visible = screen, Offset = Vector2.zero });
            s_Matrix = Matrix4x4.identity;
            s_Inverse = Matrix4x4.identity;
        }

        internal static int Depth => s_Stack.Count;

        internal static bool enabled => s_Stack.Count > 0;

        static Entry Top => s_Stack.Count > 0 ? s_Stack[s_Stack.Count - 1] : new Entry { Rect = ScreenRect, Visible = ScreenRect };

        static Rect ScreenRect => new Rect(0, 0, Screen.width, Screen.height);

        /// <summary>Clips to <paramref name="screenRect"/> (in the current coordinates); inside it the
        /// origin is its corner moved by <paramref name="scrollOffset"/>.</summary>
        internal static void Push(Rect screenRect, Vector2 scrollOffset, Vector2 renderOffset, bool resetOffset)
        {
            var parent = Top;
            var abs = resetOffset ? screenRect : new Rect(screenRect.position + parent.Offset, screenRect.size);
            var visible = Intersect(parent.Visible, abs);
            var entry = new Entry { Rect = abs, Visible = visible, Offset = abs.position + scrollOffset + renderOffset };
            s_Stack.Add(entry);
            MoveMouse(parent.Offset - entry.Offset);
        }

        internal static void Pop()
        {
            if (s_Stack.Count <= 1)
            {
                Debug.LogError("GUI Error: You are popping more GUIClips than you pushed. Make sure they are balanced.");
                return;
            }
            var top = s_Stack[s_Stack.Count - 1];
            s_Stack.RemoveAt(s_Stack.Count - 1);
            MoveMouse(top.Offset - Top.Offset);
        }

        static void MoveMouse(Vector2 by)
        {
            var e = Event.current;
            if (e != null) e.mousePosition += by;
        }

        static Rect Intersect(Rect a, Rect b)
        {
            float x0 = Math.Max(a.xMin, b.xMin), y0 = Math.Max(a.yMin, b.yMin);
            float x1 = Math.Min(a.xMax, b.xMax), y1 = Math.Min(a.yMax, b.yMax);
            return new Rect(x0, y0, Math.Max(0f, x1 - x0), Math.Max(0f, y1 - y0));
        }

        /// <summary>The visible part of the innermost group, in its coordinates.</summary>
        internal static Rect visibleRect
        {
            get
            {
                var top = Top;
                return new Rect(top.Visible.position - top.Offset, top.Visible.size);
            }
        }

        internal static Rect topmostRect => visibleRect;

        /// <summary>The innermost group's rect in absolute coordinates.</summary>
        internal static Rect GetTopRect() => Top.Rect;

        /// <summary>The visible part of the innermost group, absolute.</summary>
        internal static Rect AbsoluteVisible => Top.Visible;

        internal static Vector2 Offset => Top.Offset;

        public static Vector2 Unclip(Vector2 pos) => pos + Top.Offset;

        public static Rect Unclip(Rect rect) => new Rect(rect.position + Top.Offset, rect.size);

        public static Vector2 Clip(Vector2 absolutePos) => absolutePos - Top.Offset;

        public static Rect Clip(Rect absoluteRect) => new Rect(absoluteRect.position - Top.Offset, absoluteRect.size);

        internal static Matrix4x4 GetMatrix() => s_Matrix;

        /// <summary>Sets GUI.matrix; the mouse position is carried into the new space.</summary>
        internal static void SetMatrix(Matrix4x4 m)
        {
            if (Mathf.Approximately(m.determinant, 0f))
            {
                Debug.LogError("Ignoring invalid matrix assigned to GUI.matrix - the matrix needs to be invertible. Did you scale by 0 on Z-axis?");
                return;
            }
            var e = Event.current;
            Vector2 screen = default;
            if (e != null) screen = s_Matrix.MultiplyPoint(Unclip(e.mousePosition));
            s_Matrix = m;
            s_Inverse = m.inverse;
            if (e != null) e.mousePosition = Clip((Vector2)s_Inverse.MultiplyPoint(screen));
        }

        internal static Vector2 GUIToScreen(Vector2 local) => s_Matrix.MultiplyPoint(Unclip(local));

        internal static Vector2 ScreenToGUI(Vector2 screen) => Clip((Vector2)s_Inverse.MultiplyPoint(screen));

        /// <summary>An absolute rect through the matrix: its bounds on the screen.</summary>
        internal static Rect ToScreen(Rect absolute)
        {
            if (s_Matrix.isIdentity) return absolute;
            var a = (Vector2)s_Matrix.MultiplyPoint(new Vector2(absolute.xMin, absolute.yMin));
            var b = (Vector2)s_Matrix.MultiplyPoint(new Vector2(absolute.xMax, absolute.yMin));
            var c = (Vector2)s_Matrix.MultiplyPoint(new Vector2(absolute.xMax, absolute.yMax));
            var d = (Vector2)s_Matrix.MultiplyPoint(new Vector2(absolute.xMin, absolute.yMax));
            return Rect.MinMaxRect(
                Math.Min(Math.Min(a.x, b.x), Math.Min(c.x, d.x)), Math.Min(Math.Min(a.y, b.y), Math.Min(c.y, d.y)),
                Math.Max(Math.Max(a.x, b.x), Math.Max(c.x, d.x)), Math.Max(Math.Max(a.y, b.y), Math.Max(c.y, d.y)));
        }
    }
}

namespace Unbound.Runtime
{
    using UnityEngine;

    /// <summary>Text of UnityGUI styles through the legacy text generator, cached per layout.</summary>
    internal static class GuiText
    {
        struct Key : IEquatable<Key>
        {
            internal string Text;
            internal Font Font;
            internal int Size;
            internal FontStyle Style;
            internal TextAnchor Anchor;
            internal bool Wrap;
            internal bool Rich;
            internal Vector2 Extents;

            public bool Equals(Key o) => Text == o.Text && ReferenceEquals(Font, o.Font) && Size == o.Size && Style == o.Style &&
                                         Anchor == o.Anchor && Wrap == o.Wrap && Rich == o.Rich && Extents == o.Extents;

            public override bool Equals(object obj) => obj is Key k && Equals(k);

            public override int GetHashCode() => HashCode.Combine(Text, Font?.GetInstanceID() ?? 0, Size, Style, Anchor, Wrap, Rich, Extents);
        }

        const int CacheLimit = 1024;
        static readonly Dictionary<Key, TextGenerator> s_Cache = new Dictionary<Key, TextGenerator>();

        internal static void Invalidate() => s_Cache.Clear();

        internal static float LineHeight(Font font, int size, FontStyle style)
        {
            if (font == null) return 0f;
            int px = font.PixelSize(size);
            return font.LineSpacingAt(px);
        }

        static TextGenerator Generate(string text, GUIStyle style, Vector2 extents, bool wrap, TextAnchor anchor)
        {
            var font = style.ResolvedFont;
            var key = new Key
            {
                Text = text ?? "",
                Font = font,
                Size = style.fontSize,
                Style = style.fontStyle,
                Anchor = anchor,
                Wrap = wrap,
                Rich = style.richText,
                Extents = extents,
            };
            if (s_Cache.TryGetValue(key, out var generator)) return generator;
            if (s_Cache.Count >= CacheLimit) s_Cache.Clear();
            generator = new TextGenerator(Math.Max(8, key.Text.Length));
            generator.Populate(key.Text, new TextGenerationSettings
            {
                font = font,
                color = Color.white,
                fontSize = style.fontSize,
                lineSpacing = 1f,
                richText = style.richText,
                scaleFactor = 1f,
                fontStyle = style.fontStyle,
                textAnchor = anchor,
                updateBounds = true,
                verticalOverflow = VerticalWrapMode.Overflow,
                horizontalOverflow = wrap ? HorizontalWrapMode.Wrap : HorizontalWrapMode.Overflow,
                generationExtents = extents,
                // Vertices relative to the top left corner, y up.
                pivot = new Vector2(0f, 1f),
                generateOutOfBounds = true,
            });
            s_Cache[key] = generator;
            return generator;
        }

        /// <summary>The size of the text, wrapped at <paramref name="wrapWidth"/> when positive.</summary>
        internal static Vector2 Size(string text, GUIStyle style, float wrapWidth)
        {
            bool wrap = wrapWidth > 0f && style.wordWrap;
            var g = Generate(text, style, new Vector2(wrap ? wrapWidth : 0f, 0f), wrap, TextAnchor.UpperLeft);
            float height = Math.Max(g.rectExtents.height, style.lineHeight);
            return new Vector2(g.rectExtents.width, height);
        }

        /// <summary>The text laid out in a rect as the style draws it.</summary>
        internal static TextGenerator Layout(Rect rect, string text, GUIStyle style) =>
            Generate(text, style, new Vector2(Math.Max(0f, rect.width), Math.Max(0f, rect.height)), style.wordWrap, style.alignment);

        internal static void Draw(Rect rect, string text, GUIStyle style, Color color)
        {
            var layout = Layout(rect, text, style);
            var font = style.ResolvedFont;
            var texture = font?.material != null ? font.material.mainTexture : null;
            if (texture == null) return;
            GuiRender.DrawGlyphs(rect.position, layout.verts, texture, color);
        }

        /// <summary>The top left of the cursor before character <paramref name="index"/>.</summary>
        internal static Vector2 CursorAt(Rect rect, TextGenerator layout, int index)
        {
            var chars = layout.characters;
            if (chars.Count == 0) return rect.position;
            var c = chars[Mathf.Clamp(index, 0, chars.Count - 1)];
            return new Vector2(rect.x + c.cursorPos.x, rect.y - c.cursorPos.y);
        }

        internal static float CharWidth(TextGenerator layout, int index)
        {
            var chars = layout.characters;
            return index >= 0 && index < chars.Count ? chars[index].charWidth : 0f;
        }

        /// <summary>The cursor index nearest a point: the line under it, then the nearest gap.</summary>
        internal static int IndexAt(Rect rect, TextGenerator layout, int length, Vector2 point, float lineHeight)
        {
            var chars = layout.characters;
            int count = Math.Min(chars.Count, length + 1);
            if (count == 0) return 0;
            // The line: the last whose top is above the point.
            float lineTop = rect.y - chars[0].cursorPos.y;
            for (int i = 0; i < count; i++)
            {
                float top = rect.y - chars[i].cursorPos.y;
                if (top <= point.y + 0.01f) lineTop = Math.Max(lineTop, top);
            }
            int best = 0;
            float bestDistance = float.MaxValue;
            for (int i = 0; i < count; i++)
            {
                float top = rect.y - chars[i].cursorPos.y;
                if (!Mathf.Approximately(top, lineTop)) continue;
                float d = Math.Abs(rect.x + chars[i].cursorPos.x - point.x);
                if (d < bestDistance)
                {
                    bestDistance = d;
                    best = i;
                }
            }
            return best;
        }
    }

    /// <summary>
    /// What UnityGUI draws in a frame: textured, tinted quads in screen pixels, each with the clip
    /// rect in force, batched by texture and clip. They become one mesh drawn over everything else
    /// (after the screen-space overlay canvases) with the UI shader.
    /// </summary>
    internal static class GuiRender
    {
        struct Batch
        {
            internal Texture Texture;
            internal Rect Clip;
            internal List<int> Indices;
        }

        static readonly List<Vector3> s_Positions = new List<Vector3>();
        static readonly List<Color32> s_Colors = new List<Color32>();
        static readonly List<Vector2> s_Uvs = new List<Vector2>();
        static readonly List<Batch> s_Batches = new List<Batch>();
        static readonly Stack<List<int>> s_Spare = new Stack<List<int>>();
        static readonly List<Rect> s_ExtraClips = new List<Rect>();
        static Mesh s_Mesh;

        internal static int QuadCount => s_Positions.Count / 4;

        internal static void Clear()
        {
            s_Positions.Clear();
            s_Colors.Clear();
            s_Uvs.Clear();
            foreach (var b in s_Batches)
            {
                b.Indices.Clear();
                s_Spare.Push(b.Indices);
            }
            s_Batches.Clear();
            s_ExtraClips.Clear();
        }

        /// <summary>Clips further drawing to a rect (in the current coordinates) as well.</summary>
        internal static void PushClip(Rect local)
        {
            var abs = GUIClip.Unclip(local);
            s_ExtraClips.Add(GUIClip.ToScreen(abs));
        }

        internal static void ClearClips() => s_ExtraClips.Clear();

        internal static void PopClip()
        {
            if (s_ExtraClips.Count > 0) s_ExtraClips.RemoveAt(s_ExtraClips.Count - 1);
        }

        /// <summary>The clip rect on the screen (top-left pixels).</summary>
        static Rect CurrentClip()
        {
            var clip = GUIClip.ToScreen(GUIClip.AbsoluteVisible);
            foreach (var extra in s_ExtraClips)
            {
                float x0 = Math.Max(clip.xMin, extra.xMin), y0 = Math.Max(clip.yMin, extra.yMin);
                float x1 = Math.Min(clip.xMax, extra.xMax), y1 = Math.Min(clip.yMax, extra.yMax);
                clip = new Rect(x0, y0, Math.Max(0f, x1 - x0), Math.Max(0f, y1 - y0));
            }
            return clip;
        }

        static List<int> BatchFor(Texture texture)
        {
            var clip = CurrentClip();
            if (s_Batches.Count > 0)
            {
                var last = s_Batches[s_Batches.Count - 1];
                if (last.Texture == texture && last.Clip == clip) return last.Indices;
            }
            var indices = s_Spare.Count > 0 ? s_Spare.Pop() : new List<int>();
            s_Batches.Add(new Batch { Texture = texture, Clip = clip, Indices = indices });
            return indices;
        }

        /// <summary>A quad from local corners (top left, top right, bottom right, bottom left) and
        /// the matching texture coordinates.</summary>
        static void Quad(Texture texture, Vector2 tl, Vector2 tr, Vector2 br, Vector2 bl,
            Vector2 uvTl, Vector2 uvTr, Vector2 uvBr, Vector2 uvBl, Color32 color)
        {
            if (CurrentClip().width <= 0f) return;
            var indices = BatchFor(texture);
            int start = s_Positions.Count;
            s_Positions.Add(GUIClip.GUIToScreen(tl));
            s_Positions.Add(GUIClip.GUIToScreen(tr));
            s_Positions.Add(GUIClip.GUIToScreen(br));
            s_Positions.Add(GUIClip.GUIToScreen(bl));
            s_Uvs.Add(uvTl);
            s_Uvs.Add(uvTr);
            s_Uvs.Add(uvBr);
            s_Uvs.Add(uvBl);
            for (int i = 0; i < 4; i++) s_Colors.Add(color);
            indices.Add(start);
            indices.Add(start + 1);
            indices.Add(start + 2);
            indices.Add(start + 2);
            indices.Add(start + 3);
            indices.Add(start);
        }

        /// <summary>A texture stretched over a rect; <paramref name="uv"/> is in texture space (v up).</summary>
        internal static void DrawTexture(Rect rect, Texture texture, Rect uv, Color color)
        {
            if (texture == null || rect.width == 0f || rect.height == 0f) return;
            Quad(texture,
                new Vector2(rect.xMin, rect.yMin), new Vector2(rect.xMax, rect.yMin), new Vector2(rect.xMax, rect.yMax), new Vector2(rect.xMin, rect.yMax),
                new Vector2(uv.xMin, uv.yMax), new Vector2(uv.xMax, uv.yMax), new Vector2(uv.xMax, uv.yMin), new Vector2(uv.xMin, uv.yMin),
                color);
        }

        internal static void FillRect(Rect rect, Color color) => DrawTexture(rect, Texture2D.whiteTexture, new Rect(0, 0, 1, 1), color);

        /// <summary>A texture nine-sliced by its border (pixels): corners keep their size, edges and
        /// the middle stretch.</summary>
        internal static void DrawSliced(Rect rect, Texture texture, RectOffset border, Color color)
        {
            if (texture == null || rect.width <= 0f || rect.height <= 0f) return;
            if (border == null || (border.left == 0 && border.right == 0 && border.top == 0 && border.bottom == 0))
            {
                DrawTexture(rect, texture, new Rect(0, 0, 1, 1), color);
                return;
            }
            float tw = Math.Max(1, texture.width), th = Math.Max(1, texture.height);
            // Borders shrink together when the rect is smaller than they are.
            float l = border.left, r = border.right, t = border.top, b = border.bottom;
            float sx = l + r > rect.width ? rect.width / (l + r) : 1f;
            float sy = t + b > rect.height ? rect.height / (t + b) : 1f;
            float[] xs = { rect.xMin, rect.xMin + l * sx, rect.xMax - r * sx, rect.xMax };
            float[] ys = { rect.yMin, rect.yMin + t * sy, rect.yMax - b * sy, rect.yMax };
            float[] us = { 0f, l / tw, 1f - r / tw, 1f };
            // Texture rows go up: the top of the rect is v = 1.
            float[] vs = { 1f, 1f - t / th, b / th, 0f };
            for (int j = 0; j < 3; j++)
            {
                if (ys[j + 1] <= ys[j]) continue;
                for (int i = 0; i < 3; i++)
                {
                    if (xs[i + 1] <= xs[i]) continue;
                    Quad(texture,
                        new Vector2(xs[i], ys[j]), new Vector2(xs[i + 1], ys[j]), new Vector2(xs[i + 1], ys[j + 1]), new Vector2(xs[i], ys[j + 1]),
                        new Vector2(us[i], vs[j]), new Vector2(us[i + 1], vs[j]), new Vector2(us[i + 1], vs[j + 1]), new Vector2(us[i], vs[j + 1]),
                        color);
                }
            }
        }

        /// <summary>Glyph quads of the text generator (relative to <paramref name="origin"/>, y up),
        /// tinted by their own color times <paramref name="color"/>.</summary>
        internal static void DrawGlyphs(Vector2 origin, IList<UIVertex> verts, Texture fontTexture, Color color)
        {
            for (int i = 0; i + 3 < verts.Count; i += 4)
            {
                var v0 = verts[i];
                var v1 = verts[i + 1];
                var v2 = verts[i + 2];
                var v3 = verts[i + 3];
                Color tint = (Color)v0.color * color;
                Quad(fontTexture,
                    new Vector2(origin.x + v0.position.x, origin.y - v0.position.y),
                    new Vector2(origin.x + v1.position.x, origin.y - v1.position.y),
                    new Vector2(origin.x + v2.position.x, origin.y - v2.position.y),
                    new Vector2(origin.x + v3.position.x, origin.y - v3.position.y),
                    v0.uv0, v1.uv0, v2.uv0, v3.uv0, tint);
            }
        }

        /// <summary>The frame's draws, over everything (the overlay camera spans the screen in pixels
        /// from the bottom left).</summary>
        internal static void Collect(List<FrameBuilder.DrawItem> into)
        {
            if (s_Batches.Count == 0) return;
            if (s_Mesh == null)
            {
                using (ObjectRegistry.AsAssets())
                    s_Mesh = new Mesh { name = "IMGUI" };
                s_Mesh.hideFlags = HideFlags.HideAndDontSave;
                s_Mesh.IsPersistentAsset = true;
            }
            float height = Screen.height;
            var positions = new Vector3[s_Positions.Count];
            for (int i = 0; i < positions.Length; i++)
            {
                var p = s_Positions[i];
                positions[i] = new Vector3(p.x, height - p.y, 0f);
            }
            s_Mesh.Clear();
            s_Mesh.vertices = positions;
            s_Mesh.colors32 = s_Colors.ToArray();
            s_Mesh.uv = s_Uvs.ToArray();
            s_Mesh.subMeshCount = s_Batches.Count;
            for (int i = 0; i < s_Batches.Count; i++)
                s_Mesh.SetTriangles(s_Batches[i].Indices, i, false);
            ulong mesh = s_Mesh.Sync();
            var material = CanvasSystem.DefaultMaterial;
            for (int i = 0; i < s_Batches.Count; i++)
            {
                var b = s_Batches[i];
                into.Add(new FrameBuilder.DrawItem
                {
                    Mesh = mesh,
                    Submesh = i,
                    Material = material,
                    Model = Matrix4x4.identity,
                    Color = Color.white,
                    MainTexture = TextureHandles.Of(b.Texture),
                    Clip = new Rect(b.Clip.x, height - b.Clip.yMax, b.Clip.width, b.Clip.height),
                    ClipEnabled = true,
                    Queue = material.renderQueue,
                    Sequence = FrameBuilder.NextSequence(),
                });
            }
        }
    }

    /// <summary>
    /// Runs OnGUI, as Unity does after the scene renders: a Repaint (preceded by Layout) for every
    /// behaviour with OnGUI, from the highest GUI.depth to the lowest, then each of the frame's
    /// input events (each preceded by Layout) from the lowest depth up; an event a behaviour uses
    /// reaches the rest as <c>Used</c>. Its draws go on top of the frame.
    /// </summary>
    internal sealed class GuiDispatch : FrameBuilder.IDrawSource
    {
        static readonly GuiDispatch s_Instance = new GuiDispatch();
        static readonly Dictionary<MonoBehaviour, int> s_Depth = new Dictionary<MonoBehaviour, int>();
        static readonly List<MonoBehaviour> s_Order = new List<MonoBehaviour>();
        static bool s_AtlasRebuilt;
        static bool s_Registered;

        /// <summary>Whether the pointer is over the game view (hover states need it).</summary>
        internal static bool MouseInView => true;

        internal static void EnsureRegistered()
        {
            if (s_Registered) return;
            s_Registered = true;
            FrameBuilder.Sources.Add(s_Instance);
            Font.textureRebuilt += _ => s_AtlasRebuilt = true;
        }

        public void CollectCameraDraws(FrameBuilder.CameraContext camera, List<FrameBuilder.DrawItem> into) { }

        public void CollectOverlayDraws(List<FrameBuilder.DrawItem> into)
        {
            RunFrame();
            GuiRender.Collect(into);
        }

        static void RunFrame()
        {
            GuiRender.Clear();
            var all = Lifecycle.GuiBehaviours;
            if (all.Count == 0) return;
            s_Order.Clear();
            foreach (var mb in all)
                if (mb != null && mb.isActiveAndEnabled) s_Order.Add(mb);
            if (s_Order.Count == 0) return;

            // Repaint, back to front. A glyph atlas that grows meanwhile moves earlier glyphs: paint again.
            for (int attempt = 0; attempt < 2; attempt++)
            {
                s_AtlasRebuilt = false;
                GuiRender.Clear();
                var repaint = new Event { type = EventType.Repaint, mousePosition = MousePosition() };
                foreach (var mb in Sorted(descending: true))
                    Call(mb, repaint);
                if (!s_AtlasRebuilt) break;
            }

            var events = GuiEvents.Frame;
            for (int i = 0; i < events.Count; i++)
            {
                var e = new Event(events[i]);
                var type = e.type;
                foreach (var mb in Sorted(descending: false))
                {
                    if (mb == null || !mb.isActiveAndEnabled) continue;
                    Call(mb, e);
                }
                if (type == EventType.MouseDown && e.type != EventType.Used)
                    GUIUtility.keyboardControl = 0;
                // A release nobody took ends whatever held the mouse.
                if (type == EventType.MouseUp && e.type != EventType.Used)
                    GUIUtility.hotControl = 0;
            }
        }

        static Vector2 MousePosition() => new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y);

        static List<MonoBehaviour> Sorted(bool descending)
        {
            var list = new List<MonoBehaviour>(s_Order);
            var index = new Dictionary<MonoBehaviour, int>();
            for (int i = 0; i < list.Count; i++) index[list[i]] = i;
            list.Sort((a, b) =>
            {
                s_Depth.TryGetValue(a, out int da);
                s_Depth.TryGetValue(b, out int db);
                int c = descending ? db.CompareTo(da) : da.CompareTo(db);
                return c != 0 ? c : index[a].CompareTo(index[b]);
            });
            return list;
        }

        /// <summary>One OnGUI call for an event, with the Layout pass before it when the behaviour
        /// uses GUILayout.</summary>
        static void Call(MonoBehaviour mb, Event e)
        {
            var onGui = ScriptMessages.For(mb.GetType()).OnGUI;
            if (onGui == null) return;
            int id = mb.GetInstanceID();
            if (mb.useGUILayout)
            {
                var layout = new Event(e) { type = EventType.Layout };
                Invoke(mb, onGui, id, layout, useLayout: true);
            }
            Invoke(mb, onGui, id, e, mb.useGUILayout);
            s_Depth[mb] = GUI.depth;
        }

        static void Invoke(MonoBehaviour mb, Action<MonoBehaviour> onGui, int id, Event e, bool useLayout)
        {
            var previous = Event.current;
            Event.current = e;
            GUIUtility.BeginGUI(0, id, useLayout ? 1 : 0);
            try
            {
                GUI.BeginWindows(id);
                onGui(mb);
                GUI.EndWindows(id);
                GUIUtility.EndGUI(useLayout ? 1 : 0);
            }
            catch (Exception ex)
            {
                if (!GUIUtility.IsExitGUIException(ex))
                    Debug.LogException(ex, mb);
                GUIUtility.EndGUIAfterException();
            }
            finally
            {
                Event.current = previous;
            }
        }
    }
}
