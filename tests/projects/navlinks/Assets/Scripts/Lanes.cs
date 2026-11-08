using UnityEngine;

/// <summary>
/// The probe's lanes: each lies on its own 40 m cell (far enough apart that agents of different
/// lanes never meet) and is two 10 m platforms, A (x from -11 to -1) and B (x from 1 to 11),
/// with a 2 m gap between them that only an off-mesh link crosses.
/// </summary>
public static class Lanes
{
    public enum Kind
    {
        /// <summary>A and B level (tops at y = 0), 6 m deep.</summary>
        Gap,
        /// <summary>B 1.5 m lower than A.</summary>
        Drop,
        /// <summary>A and B level, 10 m deep, a 2 m bridge between them 4 m in front (+z).</summary>
        Bridge,
        /// <summary>A and B level, 10 m deep.</summary>
        Wide,
    }

    public static readonly (string Name, Kind Kind)[] All =
    {
        ("serialized", Kind.Gap),
        ("auto", Kind.Gap),
        ("oneway", Kind.Gap),
        ("fast", Kind.Gap),
        ("slow", Kind.Gap),
        ("baseoffset", Kind.Gap),
        ("norotation", Kind.Gap),
        ("noposition", Kind.Gap),
        ("drop", Kind.Drop),
        ("climb", Kind.Drop),
        ("reverse", Kind.Gap),
        ("reverseoneway", Kind.Gap),
        ("startonlink", Kind.Gap),
        ("endatlink", Kind.Gap),
        ("shortafter", Kind.Gap),
        ("nobraking", Kind.Gap),
        ("snap", Kind.Gap),
        ("wide", Kind.Wide),
        ("two", Kind.Gap),
        ("legacy", Kind.Gap),
        ("costlow", Kind.Bridge),
        ("costhigh", Kind.Bridge),
        ("areajump", Kind.Bridge),
        ("areamask", Kind.Bridge),
        ("inactive", Kind.Gap),
        ("manualwait", Kind.Gap),
        ("manualmove", Kind.Gap),
        ("manualcurve", Kind.Drop),
        ("manualtoggle", Kind.Gap),
        ("warpwait", Kind.Gap),
        ("warpauto", Kind.Gap),
        ("stopmid", Kind.Gap),
        ("resetmid", Kind.Gap),
        ("redirectmid", Kind.Gap),
        ("speedmid", Kind.Gap),
        ("completeauto", Kind.Gap),
        ("deactivatemid", Kind.Gap),
        ("autooffmid", Kind.Gap),
        ("inactive130", Kind.Gap),
        ("radius1", Kind.Gap),
        ("snapnear", Kind.Gap),
        ("snapfar", Kind.Gap),
        ("high", Kind.Gap),
        ("diagonal", Kind.Gap),
        ("turn", Kind.Gap),
        ("query", Kind.Gap),
        ("disableagent", Kind.Gap),
        ("deactivatewait", Kind.Gap),
        // Destinations and paths off the NavMesh, a link across a NavMesh tile edge (x = 42.67:
        // this lane's column), turning controls, stale paths and owners, links below the NavMesh.
        ("offedge", Kind.Gap),
        ("crosstile", Kind.Gap),
        ("island", Kind.Gap),
        ("targets", Kind.Gap),
        ("pendingold", Kind.Gap),
        ("turning", Kind.Gap),
        ("staleowner", Kind.Gap),
        ("low", Kind.Gap),
        ("lowmanual", Kind.Gap),
        ("aheadoff", Kind.Gap),
        ("incall", Kind.Gap),
        // Platform A built at run time for the second agent type ("Wide", radius 1.5, height 5).
        ("widetype", Kind.Gap),
    };

    public const float Spacing = 40f;
    public const int Columns = 8;

    public static Vector3 Origin(int lane) => new Vector3((lane % Columns) * Spacing, 0f, (lane / Columns) * Spacing);

    public static int Index(string name)
    {
        for (int i = 0; i < All.Length; i++)
            if (All[i].Name == name) return i;
        return -1;
    }

    /// <summary>The height of platform B's top in a lane of this kind.</summary>
    public static float HeightB(Kind kind) => kind == Kind.Drop ? -1.5f : 0f;
}
