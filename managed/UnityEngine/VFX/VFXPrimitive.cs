using System;
using UnityEngine;

namespace Unbound.Runtime
{
    /// <summary>
    /// A primitive outputs draw for each particle (planar, a cube, a point or a line), as the
    /// generated vertex programs find a particle by their vertex id: its vertices, the triangles
    /// (or points, or lines) over them (the indices of one primitive), and how many primitives one
    /// draw's 16-bit indices reach, beyond which a draw is made of instances of
    /// <see cref="PerInstance"/> (the programs add the instance's place). The programs of
    /// triangles, points and lines read no instance: they are drawn in one draw, with 32-bit
    /// indices. Matches the Unity 6000.0.71f1 Editor (tests/projects/vfx `draw-*`,
    /// tests/projects/vfxoutputs `Triangle*`, `Octagon*`, `Cube*`; tests/projects/vfxlit `points*`,
    /// `lines*`).
    /// </summary>
    internal sealed class Primitive
    {
        /// <summary>Two triangles over a quad's four corners.</summary>
        internal static readonly Primitive Quad = new Primitive(4, new ushort[] { 0, 2, 1, 1, 2, 3 }, QuadsPerInstance);
        /// <summary>Six triangles over an octagon's eight points, in order around it.</summary>
        internal static readonly Primitive Octagon = new Primitive(8, new ushort[] { 0, 1, 7, 1, 2, 7, 2, 6, 7, 2, 3, 6, 3, 5, 6, 3, 4, 5 }, OctagonsPerInstance);
        /// <summary>One triangle: its three vertices.</summary>
        internal static readonly Primitive Triangle = new Primitive(3, new ushort[] { 0, 1, 2 }, 0);
        /// <summary>One point (`Output Particle Point`).</summary>
        internal static readonly Primitive Point = new Primitive(1, new ushort[] { 0 }, 0, MeshTopology.Points);
        /// <summary>One line from a particle's position to its target position (`Output
        /// Particle Line` drawn as lines: the program's vertex 0 and 1).</summary>
        internal static readonly Primitive Line = new Primitive(2, new ushort[] { 0, 1 }, 0, MeshTopology.Lines);
        /// <summary>Twelve triangles over a cube's eight corners (corner i at the offsets the
        /// program gives it: x from bit 0, y from bit 1, z from bit 2), two on each face, facing
        /// out of the cube. The program gives each vertex a face number from its corner
        /// (<c>(corner & 7) % 5</c>) and lays the texture on a pixel by the number its triangle
        /// takes flat: 0 across z, 1 across y, any other across x. On OpenGL Core, which takes
        /// flat values from a triangle's last vertex, these triangles lay the texture as the
        /// Unity 6000.0.71f1 Editor's images on that device show it, on most faces across
        /// another axis than the face's own (tests/projects/vfxoutputs `CubeTextured`,
        /// `CubeTexturedTurned`: every face seen, the images pixel for pixel). With the winding,
        /// a triangle's last corner fixes its first, but the images do not tell a last corner
        /// from another that lays the texture alike (one of the same number, or 2, 3 and 4, all
        /// across x), so what a device taking flat values from the first vertex (Vulkan, Metal)
        /// shows is not measured, and cube outputs are refused there
        /// (<see cref="GraphicsDeviceProfile.EffectCubesMeasured"/>).</summary>
        internal static readonly Primitive Hexahedron = new Primitive(8, new ushort[]
        {
            0, 3, 1, 0, 2, 3, // z = 0
            5, 6, 4, 5, 7, 6, // z = 1
            1, 4, 0, 1, 5, 4, // y = 0
            6, 3, 2, 6, 7, 3, // y = 1
            2, 0, 4, 2, 4, 6, // x = 0
            1, 3, 7, 7, 5, 1, // x = 1
        }, HexahedronsPerInstance);

        /// <summary>The primitives of each instance of a draw beyond one draw's indices, as the
        /// generated vertex programs find a particle: <c>(vertexID >> 2) + instanceID * 2048</c>
        /// for quads, <c>(vertexID >> 3) + instanceID * 1024</c> for octagons and cubes.</summary>
        const uint QuadsPerInstance = 2048, OctagonsPerInstance = 1024, HexahedronsPerInstance = 1024;

        internal readonly int Vertices;
        /// <summary>What the indices draw: triangles, or points or lines.</summary>
        internal readonly MeshTopology Topology;
        readonly ushort[] m_Pattern;
        /// <summary>The primitives one draw takes, and of each instance beyond; 0: no limit,
        /// no instances (32-bit indices made for as many as asked).</summary>
        internal readonly uint MaxPerDraw, PerInstance;
        GraphicsBuffer m_Indices;
        uint m_Made;
        /// <summary>Buffers outgrown while draws taken before still hold them (released by the
        /// primitive's next request for indices in a later frame; at most one frame's growths
        /// wait until then).</summary>
        readonly VFXRetiredBuffers m_Retired = new VFXRetiredBuffers();

        /// <summary>A primitive of <paramref name="vertices"/> drawn by <paramref name="pattern"/>;
        /// with instances of <paramref name="perInstance"/> beyond the most one draw's 16-bit
        /// indices reach, or (0) in one draw of 32-bit indices.</summary>
        Primitive(int vertices, ushort[] pattern, uint perInstance, MeshTopology topology = MeshTopology.Triangles)
        {
            Vertices = vertices;
            Topology = topology;
            m_Pattern = pattern;
            PerInstance = perInstance;
            // As many as one draw's 16-bit indices reach.
            MaxPerDraw = perInstance == 0 ? 0 : (uint)IndexedDraws.StandInVertices / (uint)vertices;
        }

        internal int IndicesPerPrimitive => m_Pattern.Length;

        /// <summary>The indices of the primitives a draw of <paramref name="count"/> takes: the
        /// most one draw reaches, or (triangles) at least <paramref name="count"/>, made anew
        /// only when more are asked for than there are.</summary>
        internal GraphicsBuffer Indices(uint count)
        {
            m_Retired.ReleaseOlder();
            uint needed = MaxPerDraw != 0 ? MaxPerDraw : Math.Max(count, 1u);
            if (m_Indices != null && !m_Indices.IsValid())
            {
                m_Indices = null;
                m_Made = 0;
            }
            if (m_Indices != null && m_Made >= needed) return m_Indices;
            // Triangles' indices grown by halves again, so that growing systems make few buffers.
            if (MaxPerDraw == 0) needed = Math.Max(needed, m_Made + m_Made / 2);
            var data = Data(needed);
            // Draws taken before keep the outgrown buffer until a later frame.
            m_Retired.Retire(m_Indices);
            m_Indices = new GraphicsBuffer(GraphicsBuffer.Target.Index, data.Length, MaxPerDraw == 0 ? sizeof(uint) : sizeof(ushort));
            m_Indices.SetData(data);
            m_Made = needed;
            return m_Indices;
        }

        /// <summary>The indices of <paramref name="count"/> primitives, one after another: 16-bit
        /// for primitives with a most per draw, else 32-bit.</summary>
        internal Array Data(uint count)
        {
            if (MaxPerDraw == 0)
            {
                var wide = new uint[count * m_Pattern.Length];
                for (uint primitive = 0, at = 0; primitive < count; primitive++)
                    foreach (var corner in m_Pattern) wide[at++] = primitive * (uint)Vertices + corner;
                return wide;
            }
            var indices = new ushort[count * m_Pattern.Length];
            for (uint primitive = 0, at = 0; primitive < count; primitive++)
                foreach (var corner in m_Pattern) indices[at++] = (ushort)(primitive * Vertices + corner);
            return indices;
        }

        /// <summary>The direct draw of <paramref name="count"/> primitives: all in one while the
        /// indices reach, else instances of <see cref="PerInstance"/>.</summary>
        internal DrawSetup Draw(GraphicsBuffer indices, uint count) => MaxPerDraw == 0 || count <= MaxPerDraw
            ? IndexedDraws.Direct(indices, 0, (int)count * m_Pattern.Length, 1)
            : IndexedDraws.Direct(indices, 0, (int)PerInstance * m_Pattern.Length, (int)((count + PerInstance - 1) / PerInstance));
    }
}
