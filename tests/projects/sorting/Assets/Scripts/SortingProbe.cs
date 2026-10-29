using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Lays out one cell per scenario, each a red world-space canvas image overlapping a green
/// renderer, and records which one Unity draws on top. With <c>SORTING_PROBE_OUT</c> set it writes
/// the cells (name and screen pixel) and, where the frame can be read back, the winner of each, then
/// quits. With <c>SORTING_PROBE_SCREENSHOT=1</c> it only writes the cells and keeps running, for the
/// runner to read the winners from a screenshot of the window (Unbound's <c>--screenshot</c>).
/// </summary>
public class SortingProbe : MonoBehaviour
{
    const int Width = 960, Height = 540, Columns = 6;
    const float Spacing = 3f;

    static readonly Color Red = new Color(1f, 0f, 0f, 1f);
    static readonly Color Green = new Color(0f, 1f, 0f, 1f);

    readonly List<(string name, Vector3 world)> m_Cells = new List<(string, Vector3)>();
    Camera m_Camera;
    Sprite m_Sprite;
    Material m_SpriteMaterial;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Run() => new GameObject("SortingProbe").AddComponent<SortingProbe>();

    void Awake()
    {
        var texture = new Texture2D(4, 4);
        var white = new Color[16];
        for (int i = 0; i < white.Length; i++) white[i] = Color.white;
        texture.SetPixels(white);
        texture.Apply();
        m_Sprite = Sprite.Create(texture, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f), 4f);
        m_SpriteMaterial = new Material(Shader.Find("Sprites/Default"));

        var cameraObject = new GameObject("Camera");
        m_Camera = cameraObject.AddComponent<Camera>();
        m_Camera.orthographic = true;
        m_Camera.orthographicSize = 5f;
        m_Camera.clearFlags = CameraClearFlags.SolidColor;
        m_Camera.backgroundColor = Color.black;
        m_Camera.nearClipPlane = 0.1f;
        m_Camera.farClipPlane = 100f;

        // Distance only: the same layer and order, the canvas or the renderer nearer the camera.
        Cell("canvasNearer", cell => { ImageCanvas(cell, -1f, 0f); SpriteAt(cell, 0f); });
        Cell("spriteNearer", cell => { ImageCanvas(cell, 0f, 0f); SpriteAt(cell, -1f); });
        Cell("canvasFartherFirst", cell => { ImageCanvas(cell, 1f, 0f); SpriteAt(cell, 0f); });
        Cell("spriteFartherFirst", cell => { SpriteAt(cell, 1f); ImageCanvas(cell, 0f, 0f); });
        // Equal distance: creation order.
        Cell("equalCanvasFirst", cell => { ImageCanvas(cell, 0f, 0f); SpriteAt(cell, 0f); });
        Cell("equalSpriteFirst", cell => { SpriteAt(cell, 0f); ImageCanvas(cell, 0f, 0f); });

        // Which point stands for the canvas: its root, or the image.
        Cell("rootFartherImageNearer", cell => { ImageCanvas(cell, 1f, -2f); SpriteAt(cell, 0f); });
        Cell("rootNearerImageFarther", cell => { ImageCanvas(cell, -1f, 2f); SpriteAt(cell, 0f); });
        // A second image far nearer the camera, beside the sprite: the canvas's bounds centre moves.
        Cell("boundsNearer", cell =>
        {
            var canvas = ImageCanvas(cell, 0.5f, 0.5f);
            Image(canvas, cell + new Vector3(1.2f, 0f, -6f), new Vector2(0.4f, 0.4f), Red);
            SpriteAt(cell, 0f);
        });
        Cell("boundsFarther", cell =>
        {
            var canvas = ImageCanvas(cell, -0.5f, -0.5f);
            Image(canvas, cell + new Vector3(1.2f, 0f, 6f), new Vector2(0.4f, 0.4f), Red);
            SpriteAt(cell, 0f);
        });
        // The far image in a batch of its own (another material): one distance per batch, or per
        // canvas?
        Cell("boundsNearerOtherBatch", cell =>
        {
            var canvas = ImageCanvas(cell, 0.5f, 0.5f);
            Image(canvas, cell + new Vector3(1.2f, 0f, -6f), new Vector2(0.4f, 0.4f), Red)
                .material = new Material(Shader.Find("UI/Default"));
            SpriteAt(cell, 0f);
        });
        Cell("boundsFartherOtherBatch", cell =>
        {
            var canvas = ImageCanvas(cell, -0.5f, -0.5f);
            Image(canvas, cell + new Vector3(1.2f, 0f, 6f), new Vector2(0.4f, 0.4f), Red)
                .material = new Material(Shader.Find("UI/Default"));
            SpriteAt(cell, 0f);
        });
        // An image the canvas culls (fully transparent, cullTransparentMesh) still counts?
        Cell("boundsFartherCulled", cell =>
        {
            var canvas = ImageCanvas(cell, -0.5f, -0.5f);
            var hidden = Image(canvas, cell + new Vector3(1.2f, 0f, 6f), new Vector2(0.4f, 0.4f), new Color(1f, 0f, 0f, 0f));
            hidden.canvasRenderer.cullTransparentMesh = true;
            SpriteAt(cell, 0f);
        });
        // A nested canvas (not sorting on its own) nearer the camera than its root.
        Cell("nestedNearer", cell =>
        {
            var root = ImageCanvas(cell, 1f, 1f);
            var nested = new GameObject("Nested", typeof(RectTransform), typeof(Canvas));
            nested.transform.SetParent(root.transform, false);
            nested.transform.position = cell + new Vector3(0f, 0f, -2f);
            Image(nested.GetComponent<Canvas>(), cell + new Vector3(0f, 0f, -2f), new Vector2(1f, 1f), Red);
            SpriteAt(cell, 0f);
        });

        // A disabled nested canvas: its image (nearer) is not drawn.
        Cell("nestedDisabled", cell =>
        {
            var nested = NestedCanvas(ImageCanvas(cell, 1f, 1f).transform, cell);
            Image(nested, cell + new Vector3(0f, 0f, -2f), new Vector2(1f, 1f), Red);
            nested.transform.parent.GetChild(0).gameObject.SetActive(false);
            nested.enabled = false;
            SpriteAt(cell, 0f);
        });
        // An enabled canvas under a disabled nested one.
        Cell("enabledUnderDisabled", cell =>
        {
            var root = ImageCanvas(cell, 1f, 1f);
            root.transform.GetChild(0).gameObject.SetActive(false);
            var disabled = NestedCanvas(root.transform, cell);
            disabled.enabled = false;
            var inner = NestedCanvas(disabled.transform, cell);
            Image(inner, cell + new Vector3(0f, 0f, -2f), new Vector2(1f, 1f), Red);
            SpriteAt(cell, 0f);
        });

        // Which point stands for a mesh renderer: its pivot, or its bounds centre.
        Cell("meshPivotFartherBoundsNearer", cell => { ImageCanvas(cell, 0f, 0f); MeshAt(cell, 1f, -2f); });
        Cell("meshPivotNearerBoundsFarther", cell => { ImageCanvas(cell, 0f, 0f); MeshAt(cell, -1f, 2f); });
        // A sprite renderer's point: the sprite's pivot is at its centre; the sort point setting.
        Cell("spriteSortPivot", cell =>
        {
            ImageCanvas(cell, 0f, 0f);
            var sprite = SpriteAt(cell, 0.5f);
            sprite.spriteSortPoint = SpriteSortPoint.Pivot;
        });

        // Order and layer still decide before distance.
        Cell("orderBeatsDistance", cell =>
        {
            var canvas = ImageCanvas(cell, 5f, 5f);
            canvas.sortingOrder = 1;
            SpriteAt(cell, -5f);
        });
        Cell("negativeOrderSprite", cell =>
        {
            ImageCanvas(cell, 5f, 5f);
            SpriteAt(cell, -5f).sortingOrder = -1;
        });
        // A canvas whose root is far from the image in x and y (only depth counts for an
        // orthographic camera).
        Cell("rootOffsetSideways", cell =>
        {
            var canvas = ImageCanvas(cell, 1f, -1f);
            canvas.transform.position = cell + new Vector3(-30f, 20f, 1f);
            canvas.transform.GetChild(0).position = cell + new Vector3(0f, 0f, -1f);
            SpriteAt(cell, 0f);
        });

        int rows = (m_Cells.Count + Columns - 1) / Columns;
        m_Camera.orthographicSize = rows * Spacing / 2f + 0.5f;
        cameraObject.transform.position = new Vector3(Spacing * (Columns - 1) / 2f, -Spacing * (rows - 1) / 2f, -20f);
    }

    // Synchronous: the Editor in batch mode never reaches the end of a frame.
    void Start()
    {
        var output = Environment.GetEnvironmentVariable("SORTING_PROBE_OUT");
        Canvas.ForceUpdateCanvases();
        var texture = Capture();
        var json = new StringBuilder("{\n  \"cells\": [\n");
        for (int i = 0; i < m_Cells.Count; i++)
        {
            var (name, world) = m_Cells[i];
            var screen = m_Camera.WorldToScreenPoint(world);
            int x = Mathf.RoundToInt(screen.x), y = Mathf.RoundToInt(screen.y);
            json.Append("    {\"name\": \"").Append(name).Append("\", \"x\": ").Append(x)
                .Append(", \"y\": ").Append(y);
            if (texture != null) json.Append(", \"winner\": \"").Append(Winner(texture.GetPixel(x, y))).Append('"');
            json.Append(i + 1 < m_Cells.Count ? "},\n" : "}\n");
        }
        json.Append("  ]\n}\n");
        if (string.IsNullOrEmpty(output)) { Debug.Log(json.ToString()); return; }
        File.WriteAllText(output, json.ToString());
        if (texture != null) File.WriteAllBytes(Path.ChangeExtension(output, ".png"), texture.EncodeToPNG());
        Debug.Log("sorting probe written to " + output);
        // Read from the runner's screenshot instead: keep showing the frame.
        if (texture == null) return;
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(0);
#else
        Application.Quit();
#endif
    }

    /// <summary>The camera's frame, read back (<c>null</c> where reading back is not supported).</summary>
    Texture2D Capture()
    {
        if (Environment.GetEnvironmentVariable("SORTING_PROBE_SCREENSHOT") == "1") return null;
        var target = new RenderTexture(Width, Height, 24);
        m_Camera.targetTexture = target;
        m_Camera.Render();
        RenderTexture.active = target;
        var texture = new Texture2D(Width, Height, TextureFormat.RGBA32, false);
        texture.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
        texture.Apply();
        RenderTexture.active = null;
        // The target stays: the cells' pixels are measured in it.
        return texture;
    }

    static string Winner(Color c) =>
        c.r > 0.5f && c.g < 0.5f ? "canvas" : c.g > 0.5f && c.r < 0.5f ? "renderer" : $"none({c.r:F2},{c.g:F2},{c.b:F2})";

    void Cell(string name, Action<Vector3> build)
    {
        int i = m_Cells.Count;
        var centre = new Vector3((i % Columns) * Spacing, -(i / Columns) * Spacing, 0f);
        m_Cells.Add((name, centre));
        build(centre);
    }

    /// <summary>A world-space canvas whose root is at depth <paramref name="rootZ"/>, with a red image
    /// over the cell at depth <paramref name="imageZ"/>.</summary>
    Canvas ImageCanvas(Vector3 cell, float rootZ, float imageZ)
    {
        var go = new GameObject("Canvas", typeof(RectTransform), typeof(Canvas));
        var canvas = go.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        var rect = (RectTransform)go.transform;
        rect.sizeDelta = new Vector2(200f, 200f);
        rect.localScale = new Vector3(0.01f, 0.01f, 0.01f);
        rect.position = new Vector3(cell.x, cell.y, rootZ);
        Image(canvas, new Vector3(cell.x, cell.y, imageZ), new Vector2(1f, 1f), Red);
        return canvas;
    }

    static Canvas NestedCanvas(Transform parent, Vector3 cell)
    {
        var go = new GameObject("Nested", typeof(RectTransform), typeof(Canvas));
        go.transform.SetParent(parent, false);
        go.transform.position = cell + new Vector3(0f, 0f, -2f);
        return go.GetComponent<Canvas>();
    }

    static Image Image(Canvas canvas, Vector3 world, Vector2 size, Color color)
    {
        var go = new GameObject("Image", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        go.transform.SetParent(canvas.transform, false);
        var rect = (RectTransform)go.transform;
        rect.sizeDelta = size * 100f;
        rect.position = world;
        var image = go.GetComponent<Image>();
        image.color = color;
        return image;
    }

    /// <summary>A green sprite, shifted down-left of the cell centre so both halves stay visible.</summary>
    SpriteRenderer SpriteAt(Vector3 cell, float z)
    {
        var go = new GameObject("Sprite");
        go.transform.position = new Vector3(cell.x + 0.3f, cell.y - 0.3f, z);
        var renderer = go.AddComponent<SpriteRenderer>();
        renderer.sprite = m_Sprite;
        renderer.color = Green;
        return renderer;
    }

    /// <summary>A green quad mesh whose transform is at <paramref name="pivotZ"/> and whose vertices
    /// sit <paramref name="vertexZ"/> from it.</summary>
    void MeshAt(Vector3 cell, float pivotZ, float vertexZ)
    {
        var go = new GameObject("Mesh");
        go.transform.position = new Vector3(cell.x + 0.3f, cell.y - 0.3f, pivotZ);
        var mesh = new Mesh();
        mesh.vertices = new[]
        {
            new Vector3(-0.5f, -0.5f, vertexZ), new Vector3(-0.5f, 0.5f, vertexZ),
            new Vector3(0.5f, 0.5f, vertexZ), new Vector3(0.5f, -0.5f, vertexZ),
        };
        mesh.colors = new[] { Green, Green, Green, Green };
        mesh.uv = new[] { Vector2.zero, Vector2.up, Vector2.one, Vector2.right };
        mesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        go.AddComponent<MeshRenderer>().sharedMaterial = m_SpriteMaterial;
    }
}
