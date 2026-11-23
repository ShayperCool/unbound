using System;
using UnityEngine;

namespace Unbound.Runtime
{
    /// <summary>
    /// A planar primitive outputs draw for each particle, as the generated vertex programs find
    /// a particle by their vertex id: its vertices, the triangles over them (the indices of one
    /// primitive), and how many primitives one draw's 16-bit indices reach, beyond which a draw
    /// is made of instances of <see cref="PerInstance"/> (the programs add the instance's place).
    /// A triangle's program reads no instance: triangles are drawn in one draw, with 32-bit
    /// indices. Matches the Unity 6000.0.71f1 Editor (tests/projects/vfx `draw-*`,
    /// tests/projects/vfxoutputs `Triangle*`, `Octagon*`).
    /// </summary>
    internal sealed class Primitive
    {
        /// <summary>Two triangles over a quad's four corners.</summary>
        internal static readonly Primitive Quad = new Primitive(4, new ushort[] { 0, 2, 1, 1, 2, 3 }, QuadsPerInstance);
        /// <summary>Six triangles over an octagon's eight points, in order around it.</summary>
        internal static readonly Primitive Octagon = new Primitive(8, new ushort[] { 0, 1, 7, 1, 2, 7, 2, 6, 7, 2, 3, 6, 3, 5, 6, 3, 4, 5 }, OctagonsPerInstance);
        /// <summary>One triangle: its three vertices.</summary>
        internal static readonly Primitive Triangle = new Primitive(3, new ushort[] { 0, 1, 2 }, 0);
        /// <summary>Twelve triangles over a cube's eight corners (corner i at the offsets the
        /// program gives it: x from bit 0, y from bit 1, z from bit 2), each face's two
        /// triangles facing out (<see cref="CubeFaces"/>).</summary>
        internal static readonly Primitive Hexahedron = new Primitive(8, CubeFaces(), HexahedronsPerInstance);

        /// <summary>The primitives of each instance of a draw beyond one draw's indices, as the
        /// generated vertex programs find a particle: <c>(vertexID >> 2) + instanceID * 2048</c>
        /// for quads, <c>(vertexID >> 3) + instanceID * 1024</c> for octagons and cubes.</summary>
        const uint QuadsPerInstance = 2048, OctagonsPerInstance = 1024, HexahedronsPerInstance = 1024;

        /// <summary>A cube's faces as triangles over its corners, wound as the quad's are (the
        /// face toward the camera at negative z is <c>0, 2, 1, 1, 2, 3</c>, the quad's own):
        /// each triangle's corners in the order whose cross product points out of the cube.</summary>
        static ushort[] CubeFaces()
        {
            var faces = new ushort[36];
            int at = 0;
            static Vector3 Corner(int i) => new Vector3(i & 1, (i >> 1) & 1, (i >> 2) & 1);
            // Each face: the axis it is across and its side; its corners in the quad's order.
            for (int axis = 0; axis < 3; axis++)
                for (int side = 0; side < 2; side++)
                {
                    // The two other axes, in order.
                    int u = axis == 0 ? 1 : 0, v = axis == 2 ? 1 : 2;
                    int Pick(int du, int dv) => (side << axis) | (du << u) | (dv << v);
                    var quad = new[] { Pick(0, 0), Pick(1, 0), Pick(0, 1), Pick(1, 1) };
                    var outward = new Vector3(axis == 0 ? side * 2 - 1 : 0, axis == 1 ? side * 2 - 1 : 0, axis == 2 ? side * 2 - 1 : 0);
                    foreach (var (a, b, c) in new[] { (0, 2, 1), (1, 2, 3) })
                    {
                        var (p, q, r) = (quad[a], quad[b], quad[c]);
                        if (Vector3.Dot(Vector3.Cross(Corner(q) - Corner(p), Corner(r) - Corner(p)), outward) < 0f) (q, r) = (r, q);
                        faces[at++] = (ushort)p;
                        faces[at++] = (ushort)q;
                        faces[at++] = (ushort)r;
                    }
                }
            return faces;
        }

        internal readonly int Vertices;
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
        Primitive(int vertices, ushort[] pattern, uint perInstance)
        {
            Vertices = vertices;
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
