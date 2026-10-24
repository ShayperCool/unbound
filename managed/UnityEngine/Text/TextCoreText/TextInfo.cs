using System;
using System.Text;
using UnityEngine.TextCore.LowLevel;

// What TextCore's generator produces (UI Toolkit reads it): per character element positions and
// vertices, per line metrics, and one vertex buffer per material (atlas). Coordinates: the
// element and line values are TextCore's, y up from the first line's baseline shifted by the
// alignment into the screen rect (whose bottom is y = 0); the mesh vertices are in the screen
// rect's space with y down (y' = rect height - y), as UI Toolkit draws them.

namespace UnityEngine.TextCore.Text
{
    internal partial struct TextCoreVertex
    {
        public Vector3 position;
        public Color32 color;
        public Vector2 uv0;
        public Vector2 uv2;
    }

    internal partial struct TextVertex
    {
        public Vector3 position;
        public Vector4 uv;
        public Vector2 uv2;
        public Color32 color;
    }

    internal partial struct Extents
    {
        public Vector2 min;
        public Vector2 max;

        public Extents(Vector2 min, Vector2 max)
        {
            this.min = min;
            this.max = max;
        }

        public override string ToString() =>
            "Min (" + min.x.ToString("f2") + ", " + min.y.ToString("f2") + ")   Max (" + max.x.ToString("f2") + ", " + max.y.ToString("f2") + ")";
    }

    internal partial struct Offset
    {
        float m_Left;
        float m_Right;
        float m_Top;
        float m_Bottom;

        public Offset(float left, float right, float top, float bottom)
        {
            m_Left = left;
            m_Right = right;
            m_Top = top;
            m_Bottom = bottom;
        }

        public float left { get => m_Left; set => m_Left = value; }
        public float right { get => m_Right; set => m_Right = value; }
        public float top { get => m_Top; set => m_Top = value; }
        public float bottom { get => m_Bottom; set => m_Bottom = value; }

        public static Offset zero => default;

        public static bool operator ==(Offset lhs, Offset rhs) =>
            lhs.m_Left == rhs.m_Left && lhs.m_Right == rhs.m_Right && lhs.m_Top == rhs.m_Top && lhs.m_Bottom == rhs.m_Bottom;

        public static bool operator !=(Offset lhs, Offset rhs) => !(lhs == rhs);

        public static Offset operator *(Offset a, float b) => new Offset(a.m_Left * b, a.m_Right * b, a.m_Top * b, a.m_Bottom * b);

        public override int GetHashCode() => HashCode.Combine(m_Left, m_Right, m_Top, m_Bottom);
        public override bool Equals(object obj) => obj is Offset other && Equals(other);
        public bool Equals(Offset other) => this == other;
    }

    internal partial struct HighlightState
    {
        public Color32 color;
        public Offset padding;

        public HighlightState(Color32 color, Offset padding)
        {
            this.color = color;
            this.padding = padding;
        }

        public static bool operator ==(HighlightState lhs, HighlightState rhs) =>
            lhs.color.r == rhs.color.r && lhs.color.g == rhs.color.g && lhs.color.b == rhs.color.b && lhs.color.a == rhs.color.a && lhs.padding == rhs.padding;

        public static bool operator !=(HighlightState lhs, HighlightState rhs) => !(lhs == rhs);

        public override int GetHashCode() => HashCode.Combine(color.r, color.g, color.b, color.a, padding);
        public override bool Equals(object obj) => obj is HighlightState other && Equals(other);
        public bool Equals(HighlightState other) => this == other;
    }

    internal partial struct TextElementInfo
    {
        public uint character;
        public int index;
        public TextElementType elementType;
        public int stringLength;
        public TextElement textElement;
        public Glyph alternativeGlyph;
        public FontAsset fontAsset;
        public SpriteAsset spriteAsset;
        public Material material;
        public int materialReferenceIndex;
        public bool isUsingAlternateTypeface;
        public float pointSize;
        public int lineNumber;
        public int vertexIndex;
        public TextVertex vertexTopLeft;
        public TextVertex vertexBottomLeft;
        public TextVertex vertexTopRight;
        public TextVertex vertexBottomRight;
        public Vector3 topLeft;
        public Vector3 bottomLeft;
        public Vector3 topRight;
        public Vector3 bottomRight;
        public float origin;
        public float ascender;
        public float baseLine;
        public float descender;
        internal float adjustedAscender;
        internal float adjustedDescender;
        internal float adjustedHorizontalAdvance;
        public float xAdvance;
        public float aspectRatio;
        public float scale;
        public Color32 color;
        public Color32 underlineColor;
        public int underlineVertexIndex;
        public Color32 strikethroughColor;
        public int strikethroughVertexIndex;
        public Color32 highlightColor;
        public HighlightState highlightState;
        public FontStyles style;
        public bool isVisible;

        public override string ToString() =>
            $"character: {character}\nindex: {index}\nlineNumber: {lineNumber}\norigin: {origin}\nxAdvance: {xAdvance}\nascender: {ascender}\nbaseLine: {baseLine}\ndescender: {descender}\nbottomLeft: {bottomLeft}\ntopRight: {topRight}\nisVisible: {isVisible}";

        internal string ToStringTest() =>
            "topLeft.x: " + topLeft.x.ToString("F4") + "\n topLeft.y: " + topLeft.y.ToString("F4") + "\n topRight.x: " + topRight.x.ToString("F4") +
            "\n topRight.y: " + topRight.y.ToString("F4") + "\n  bottomLeft.x: " + bottomLeft.x.ToString("F4") + "\n bottomLeft.y: " + bottomLeft.y.ToString("F4") +
            "\n  bottomRight.x: " + bottomRight.x.ToString("F4") + "\n bottomRight.y: " + bottomRight.y.ToString("F4") + "\norigin: " + origin.ToString("F4") +
            "\nxAdvance: " + xAdvance.ToString("F4") + "\n";
    }

    internal partial struct LineInfo
    {
        internal int controlCharacterCount;
        public int characterCount;
        public int visibleCharacterCount;
        public int visibleSpaceCount;
        public int spaceCount;
        public int wordCount;
        public int firstCharacterIndex;
        public int firstVisibleCharacterIndex;
        public int lastCharacterIndex;
        public int lastVisibleCharacterIndex;
        public float length;
        public float lineHeight;
        public float ascender;
        public float baseline;
        public float descender;
        public float maxAdvance;
        public float width;
        public float marginLeft;
        public float marginRight;
        public TextAlignment alignment;
        public Extents lineExtents;
    }

    internal partial struct WordInfo
    {
        public int firstCharacterIndex;
        public int lastCharacterIndex;
        public int characterCount;
    }

    internal partial struct LinkInfo
    {
        public int hashCode;
        public int linkIdFirstCharacterIndex;
        public int linkIdLength;
        public int linkTextfirstCharacterIndex;
        public int linkTextLength;
        internal char[] linkId;
        string m_LinkIdString;
        string m_LinkTextString;

        internal void SetLinkId(char[] text, int startIndex, int length)
        {
            if (linkId == null || linkId.Length < length) linkId = new char[length];
            Array.Copy(text, startIndex, linkId, 0, length);
            linkIdLength = length;
            m_LinkIdString = null;
            m_LinkTextString = null;
        }

        public string GetLinkText(TextInfo textInfo)
        {
            if (string.IsNullOrEmpty(m_LinkTextString))
            {
                var text = new StringBuilder(Math.Max(0, linkTextLength));
                for (int i = linkTextfirstCharacterIndex; i < linkTextfirstCharacterIndex + linkTextLength; i++)
                    text.Append((char)textInfo.textElementInfo[i].character);
                m_LinkTextString = text.ToString();
            }
            return m_LinkTextString;
        }

        public string GetLinkId()
        {
            if (string.IsNullOrEmpty(m_LinkIdString)) m_LinkIdString = linkId == null ? "" : new string(linkId, 0, linkIdLength);
            return m_LinkIdString;
        }
    }

    /// <summary>The vertices of one material (a font asset's atlas texture): four per quad, in
    /// the order bottom left, top left, top right, bottom right.</summary>
    internal partial struct MeshInfo
    {
        public int vertexCount;
        public TextCoreVertex[] vertexData;
        public Material material;
        public int vertexBufferSize;
        public bool applySDF;
        internal GlyphRenderMode glyphRenderMode;

        /// <summary>The quads IMGUI can index with 16-bit indices.</summary>
        const int k_MaxImguiQuads = 16383;

        public MeshInfo(int size, bool isIMGUI)
        {
            if (isIMGUI) size = Mathf.Min(size, k_MaxImguiQuads);
            vertexCount = 0;
            vertexBufferSize = size * 4;
            vertexData = new TextCoreVertex[vertexBufferSize];
            material = null;
            applySDF = true;
            glyphRenderMode = GlyphRenderMode.DEFAULT;
        }

        internal void ResizeMeshInfo(int size, bool isIMGUI)
        {
            if (isIMGUI) size = Mathf.Min(size, k_MaxImguiQuads);
            vertexBufferSize = size * 4;
            Array.Resize(ref vertexData, vertexBufferSize);
        }

        internal void Clear(bool uploadChanges)
        {
            if (vertexData == null) return;
            Array.Clear(vertexData, 0, vertexData.Length);
            vertexBufferSize = vertexData.Length;
            vertexCount = 0;
        }

        internal void ClearUnusedVertices()
        {
            int unused = vertexData.Length - vertexCount;
            if (unused > 0) Array.Clear(vertexData, vertexCount, unused);
            vertexBufferSize = vertexData.Length;
        }
    }

    internal partial class TextInfo
    {
        const float k_Infinity = 32767f;

        public int characterCount;
        public int spriteCount;
        public int spaceCount;
        public int wordCount;
        public int linkCount;
        public int lineCount;
        public int materialCount;
        public TextElementInfo[] textElementInfo;
        public WordInfo[] wordInfo;
        public LinkInfo[] linkInfo;
        public LineInfo[] lineInfo;
        public MeshInfo[] meshInfo;
        public double lastTimeInCache;
        public Action removedFromCache;
        public bool hasMultipleColors;

        public TextInfo()
        {
            textElementInfo = new TextElementInfo[4];
            wordInfo = new WordInfo[1];
            lineInfo = new LineInfo[1];
            linkInfo = Array.Empty<LinkInfo>();
            meshInfo = Array.Empty<MeshInfo>();
        }

        public void RemoveFromCache()
        {
            var removed = removedFromCache;
            removedFromCache = null;
            removed?.Invoke();
        }

        internal void Clear()
        {
            characterCount = 0;
            spaceCount = 0;
            wordCount = 0;
            linkCount = 0;
            lineCount = 0;
            spriteCount = 0;
            hasMultipleColors = false;
            for (int i = 0; i < meshInfo.Length; i++) meshInfo[i].vertexCount = 0;
        }

        internal void ClearMeshInfo(bool updateMesh)
        {
            for (int i = 0; i < meshInfo.Length; i++) meshInfo[i].Clear(updateMesh);
        }

        internal void ClearLineInfo()
        {
            lineInfo ??= new LineInfo[1];
            for (int i = 0; i < lineInfo.Length; i++)
            {
                ref var line = ref lineInfo[i];
                line.characterCount = 0;
                line.spaceCount = 0;
                line.wordCount = 0;
                line.controlCharacterCount = 0;
                line.ascender = -k_Infinity;
                line.baseline = 0f;
                line.descender = k_Infinity;
                line.maxAdvance = 0f;
                line.marginLeft = 0f;
                line.marginRight = 0f;
                line.lineExtents.min = new Vector2(k_Infinity, k_Infinity);
                line.lineExtents.max = new Vector2(-k_Infinity, -k_Infinity);
                line.width = 0f;
            }
        }

        /// <summary>Grows an array to the next power of two of <paramref name="size"/> (+256 past 1024).</summary>
        internal static void Resize<T>(ref T[] array, int size) => Array.Resize(ref array, size > 1024 ? size + 256 : Mathf.NextPowerOfTwo(size));

        internal static void Resize<T>(ref T[] array, int size, bool isBlockAllocated)
        {
            if (isBlockAllocated) size = size > 1024 ? size + 256 : Mathf.NextPowerOfTwo(size);
            if (size != array.Length) Array.Resize(ref array, size);
        }

        // ---- Queries (text fields: cursor and selection) ----

        public virtual Vector2 GetCursorPositionFromStringIndexUsingCharacterHeight(int index, Rect screenRect, float lineHeight, bool inverseYAxis = true)
        {
            var position = screenRect.position;
            if (characterCount == 0) return inverseYAxis ? new Vector2(0f, lineHeight) : position;
            var element = textElementInfo[index >= characterCount ? characterCount - 1 : index];
            float x = index >= characterCount ? element.xAdvance : element.origin;
            return position + (inverseYAxis ? new Vector2(x, screenRect.height - element.descender) : new Vector2(x, element.descender));
        }

        public Vector2 GetCursorPositionFromStringIndexUsingLineHeight(int index, Rect screenRect, float lineHeight, bool useXAdvance = false, bool inverseYAxis = true)
        {
            var position = screenRect.position;
            if (characterCount == 0 || index < 0) return inverseYAxis ? new Vector2(0f, lineHeight) : position;
            if (index >= characterCount)
            {
                index = characterCount - 1;
                useXAdvance = true;
            }
            var element = textElementInfo[index];
            var line = lineInfo[element.lineNumber];
            float x = useXAdvance ? element.xAdvance : element.origin;
            return position + new Vector2(x, inverseYAxis ? screenRect.height - line.descender : line.descender);
        }

        public int GetCursorIndexFromPosition(Vector2 position, Rect screenRect, bool inverseYAxis = true)
        {
            if (inverseYAxis) position.y = screenRect.height - position.y;
            int line = lineCount > 1 ? FindNearestLine(position) : 0;
            int index = FindNearestCharacterOnLine(position, line, false);
            var element = textElementInfo[index];
            float fraction = (position.x - element.bottomLeft.x) / (element.topRight.x - element.bottomLeft.x);
            return fraction < 0.5f || element.character == 10 ? index : index + 1;
        }

        public int LineDownCharacterPosition(int originalPos)
        {
            if (originalPos >= characterCount) return characterCount - 1;
            int line = textElementInfo[originalPos].lineNumber;
            if (line + 1 >= lineCount) return characterCount - 1;
            return NearestOnLine(textElementInfo[originalPos].origin, lineInfo[line + 1].firstCharacterIndex, lineInfo[line + 1].lastCharacterIndex);
        }

        public int LineUpCharacterPosition(int originalPos)
        {
            if (originalPos >= characterCount) originalPos--;
            int line = textElementInfo[originalPos].lineNumber;
            if (line - 1 < 0) return 0;
            return NearestOnLine(textElementInfo[originalPos].origin, lineInfo[line - 1].firstCharacterIndex, lineInfo[line].firstCharacterIndex - 1);
        }

        /// <summary>The cursor index on another line nearest to an x: the character under it (its
        /// near half), else the closest origin; <paramref name="end"/> when none is.</summary>
        int NearestOnLine(float x, int first, int end)
        {
            int best = -1;
            float bestDistance = float.PositiveInfinity, bestFraction = 0f;
            for (int i = first; i < end; i++)
            {
                var candidate = textElementInfo[i];
                float distance = x - candidate.origin;
                float fraction = distance / (candidate.xAdvance - candidate.origin);
                if (fraction >= 0f && fraction <= 1f) return fraction < 0.5f ? i : i + 1;
                distance = Mathf.Abs(distance);
                if (distance < bestDistance)
                {
                    best = i;
                    bestDistance = distance;
                    bestFraction = fraction;
                }
            }
            if (best == -1) return end;
            return bestFraction < 0.5f ? best : best + 1;
        }

        public int FindWordIndex(int cursorIndex)
        {
            for (int i = 0; i < wordCount; i++)
                if (wordInfo[i].firstCharacterIndex <= cursorIndex && wordInfo[i].lastCharacterIndex >= cursorIndex) return i;
            return -1;
        }

        public int FindNearestLine(Vector2 position)
        {
            float nearest = float.PositiveInfinity;
            int result = -1;
            for (int i = 0; i < lineCount; i++)
            {
                var line = lineInfo[i];
                if (line.ascender > position.y && line.descender < position.y) return i;
                float distance = Mathf.Min(Mathf.Abs(line.ascender - position.y), Mathf.Abs(line.descender - position.y));
                if (distance < nearest)
                {
                    nearest = distance;
                    result = i;
                }
            }
            return result;
        }

        public int FindNearestCharacterOnLine(Vector2 position, int line, bool visibleOnly)
        {
            if (line >= lineInfo.Length || line < 0) return 0;
            int first = lineInfo[line].firstCharacterIndex, last = lineInfo[line].lastCharacterIndex;
            float nearest = float.PositiveInfinity;
            int result = last;
            for (int i = first; i <= last; i++)
            {
                var element = textElementInfo[i];
                if ((visibleOnly && !element.isVisible) || element.character == 13 || element.character == 10) continue;
                var bl = element.bottomLeft;
                var tl = new Vector3(element.bottomLeft.x, element.topRight.y, 0f);
                var tr = element.topRight;
                var br = new Vector3(element.topRight.x, element.bottomLeft.y, 0f);
                if (PointIntersectRectangle(position, bl, tl, tr, br)) return i;
                float distance = Mathf.Min(Mathf.Min(DistanceToLine(bl, tl, position), DistanceToLine(tl, tr, position)),
                    Mathf.Min(DistanceToLine(tr, br, position), DistanceToLine(br, bl, position)));
                if (nearest > distance)
                {
                    nearest = distance;
                    result = i;
                }
            }
            return result;
        }

        public int FindIntersectingLink(Vector3 position, Rect screenRect, bool inverseYAxis = true)
        {
            if (inverseYAxis) position.y = screenRect.height - position.y;
            for (int i = 0; i < linkCount; i++)
            {
                var link = linkInfo[i];
                bool open = false;
                Vector3 bottomLeft = default, topLeft = default;
                for (int j = 0; j < link.linkTextLength; j++)
                {
                    int index = link.linkTextfirstCharacterIndex + j;
                    var element = textElementInfo[index];
                    if (!open)
                    {
                        open = true;
                        bottomLeft = new Vector3(element.bottomLeft.x, element.descender, 0f);
                        topLeft = new Vector3(element.bottomLeft.x, element.ascender, 0f);
                    }
                    bool lineEnds = j == link.linkTextLength - 1 || element.lineNumber != textElementInfo[index + 1].lineNumber;
                    if (!lineEnds) continue;
                    open = false;
                    var topRight = new Vector3(element.topRight.x, element.ascender, 0f);
                    var bottomRight = new Vector3(element.topRight.x, element.descender, 0f);
                    if (PointIntersectRectangle(position, bottomLeft, topLeft, topRight, bottomRight)) return i;
                }
            }
            return -1;
        }

        public int GetCorrespondingStringIndex(int index) =>
            index <= 0 ? 0 : textElementInfo[index - 1].index + textElementInfo[index - 1].stringLength;

        public int GetCorrespondingCodePointIndex(int stringIndex)
        {
            if (stringIndex <= 0) return 0;
            for (int i = 0; i < characterCount; i++)
                if (textElementInfo[i].index + textElementInfo[i].stringLength >= stringIndex) return i + 1;
            return characterCount;
        }

        public LineInfo GetLineInfoFromCharacterIndex(int index) => lineInfo[GetLineNumber(index)];

        static bool PointIntersectRectangle(Vector3 m, Vector3 a, Vector3 b, Vector3 c, Vector3 d)
        {
            if (Vector3.Cross(b - a, d - a) == Vector3.zero) return false;
            var ab = b - a;
            var bc = c - b;
            float along = Vector3.Dot(ab, m - a);
            float across = Vector3.Dot(bc, m - b);
            return 0f <= along && along <= Vector3.Dot(ab, ab) && 0f <= across && across <= Vector3.Dot(bc, bc);
        }

        /// <summary>The squared distance from a point to a segment.</summary>
        static float DistanceToLine(Vector3 a, Vector3 b, Vector3 point)
        {
            if (a == b)
            {
                var v = point - a;
                return Vector3.Dot(v, v);
            }
            var ab = b - a;
            var pa = a - point;
            float t = Vector3.Dot(ab, pa);
            if (t > 0f) return Vector3.Dot(pa, pa);
            var pb = point - b;
            if (Vector3.Dot(ab, pb) > 0f) return Vector3.Dot(pb, pb);
            var e = pa - ab * (t / Vector3.Dot(ab, ab));
            return Vector3.Dot(e, e);
        }

        int ClampCharacter(int index) => index <= 0 ? 0 : index >= characterCount ? Mathf.Max(0, characterCount - 1) : index;

        public int GetLineNumber(int index) => textElementInfo[ClampCharacter(index)].lineNumber;

        public float GetLineHeight(int lineNumber)
        {
            if (lineNumber <= 0) lineNumber = 0;
            if (lineNumber >= lineCount) lineNumber = Mathf.Max(0, lineCount - 1);
            return lineInfo[lineNumber].lineHeight;
        }

        public float GetLineHeightFromCharacterIndex(int index) => GetLineHeight(textElementInfo[ClampCharacter(index)].lineNumber);

        public float GetCharacterHeightFromIndex(int index)
        {
            var element = textElementInfo[ClampCharacter(index)];
            return element.ascender - element.descender;
        }

        public string Substring(int startIndex, int length)
        {
            if (startIndex < 0 || startIndex + length > characterCount) throw new ArgumentOutOfRangeException();
            var text = new StringBuilder(length);
            for (int i = startIndex; i < startIndex + length; i++)
            {
                uint c = textElementInfo[i].character;
                if (c >= 0x10000 && c <= 0x10FFFF) text.Append(char.ConvertFromUtf32((int)c));
                else text.Append((char)c);
            }
            return text.ToString();
        }

        public int IndexOf(char value, int startIndex)
        {
            if (startIndex < 0 || startIndex >= characterCount) throw new ArgumentOutOfRangeException();
            for (int i = startIndex; i < characterCount; i++)
                if (textElementInfo[i].character == value) return i;
            return -1;
        }

        public int LastIndexOf(char value, int startIndex)
        {
            if (startIndex < 0 || startIndex >= characterCount) throw new ArgumentOutOfRangeException();
            for (int i = startIndex; i >= 0; i--)
                if (textElementInfo[i].character == value) return i;
            return -1;
        }
    }
}
