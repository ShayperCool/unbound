using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

/// <summary>
/// Records what sprite import gives a game: for every sprite of Assets/Sprites, its rect, pivot,
/// pixels per unit, bounds, texture rect and offset, mesh (vertices, uvs, triangles) and physics
/// shapes. Writes <c>SPRITE_IMPORT_PROBE_OUT</c> (one JSON line per sprite) and quits.
/// </summary>
public class SpriteImportProbe : MonoBehaviour
{
    public Sprite[] sprites;

    static string F(float f) => float.IsNaN(f) || float.IsInfinity(f)
        ? "\"" + f.ToString(CultureInfo.InvariantCulture) + "\""
        : f.ToString("R", CultureInfo.InvariantCulture);
    static string V(Vector2 v) => "[" + F(v.x) + "," + F(v.y) + "]";
    static string Arr(IEnumerable<Vector2> vs) => "[" + string.Join(",", vs.Select(V)) + "]";
    static string R(Rect r) => "[" + F(r.x) + "," + F(r.y) + "," + F(r.width) + "," + F(r.height) + "]";

    void Start()
    {
        int code = 0;
        try
        {
            Record();
        }
        catch (Exception e)
        {
            Debug.LogException(e);
            code = 1;
        }
        Application.Quit(code);
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(code);
#endif
    }

    void Record()
    {
        var b = new StringBuilder();
        foreach (var s in sprites)
        {
            b.Append("{\"texture\":\"").Append(s.texture.name).Append("\",\"name\":\"").Append(s.name).Append('"');
            b.Append(",\"rect\":").Append(R(s.rect));
            b.Append(",\"pivot\":").Append(V(s.pivot));
            b.Append(",\"ppu\":").Append(F(s.pixelsPerUnit));
            var bounds = s.bounds;
            b.Append(",\"bounds\":[").Append(F(bounds.center.x)).Append(',').Append(F(bounds.center.y)).Append(',').Append(F(bounds.center.z))
                .Append(',').Append(F(bounds.extents.x)).Append(',').Append(F(bounds.extents.y)).Append(',').Append(F(bounds.extents.z)).Append(']');
            b.Append(",\"textureRect\":").Append(R(s.textureRect));
            b.Append(",\"textureRectOffset\":").Append(V(s.textureRectOffset));
            b.Append(",\"vertices\":").Append(Arr(s.vertices));
            b.Append(",\"uv\":").Append(Arr(s.uv));
            b.Append(",\"triangles\":[").Append(string.Join(",", s.triangles)).Append(']');
            b.Append(",\"physics\":[");
            var list = new List<Vector2>();
            for (int i = 0; i < s.GetPhysicsShapeCount(); i++)
            {
                s.GetPhysicsShape(i, list);
                if (i > 0) b.Append(',');
                b.Append(Arr(list));
            }
            b.Append("]}\n");
        }
        var path = Environment.GetEnvironmentVariable("SPRITE_IMPORT_PROBE_OUT");
        if (!string.IsNullOrEmpty(path)) File.WriteAllText(path, b.ToString());
        else Debug.Log(b.ToString());
    }
}
