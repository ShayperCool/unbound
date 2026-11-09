/*
Copyright (c) 2009-2010 Mikko Mononen memon@inside.org
recast4j copyright (c) 2015-2019 Piotr Piastucki piotr@jtilia.org
DotRecast Copyright (c) 2023-2024 Choi Ikpil ikpil@naver.com

This software is provided 'as-is', without any express or implied
warranty.  In no event will the authors be held liable for any damages
arising from the use of this software.
Permission is granted to anyone to use this software for any purpose,
including commercial applications, and to alter it and redistribute it
freely, subject to the following restrictions:
1. The origin of this software must not be misrepresented; you must not
 claim that you wrote the original software. If you use this software
 in a product, an acknowledgment in the product documentation would be
 appreciated but is not required.
2. Altered source versions must be plainly marked as such, and must not be
 misrepresented as being the original software.
3. This notice may not be removed or altered from any source distribution.
*/

using DotRecast.Core.Numerics;

namespace DotRecast.Detour
{
    /// Defines an navigation mesh off-mesh connection within a dtMeshTile object.
    /// An off-mesh connection is a user defined traversable connection made up to two vertices.
    internal class DtOffMeshConnection
    {
        /// The endpoints of the connection. [(ax, ay, az, bx, by, bz)]
        public RcVec3f[] pos = new RcVec3f[2];

        /// The radius of the endpoints. [Limit: >= 0]
        public float rad;

        /// The polygon reference of the connection within the tile.
        public int poly;

        /// Link flags. 
        /// @note These are not the connection's user defined flags. Those are assigned via the 
        /// connection's dtPoly definition. These are link flags used for internal purposes.
        public int flags;

        /// End point side.
        public int side;

        /// The id of the offmesh connection. (User assigned when the navigation mesh is built.)
        public int userId;

        /// Unbound: the cost per metre of crossing the connection in place of its area's cost; negative:
        /// the area's cost (NavMeshLinkData.costModifier, Unity's).
        public float costModifier = -1f;

        /// Unbound: half the width of a wide connection (NavMeshLinkData.width), across it; zero for a
        /// connection between two points. Each end of a wide connection is a segment: the start from
        /// pos[0] - halfWidth to pos[0] + halfWidth, the end from pos[1] + halfWidth to pos[1] - halfWidth,
        /// so the same parameter t on the start and 1 - t on the end lie straight across.
        public RcVec3f halfWidth;

        /// Unbound: whether the connection's ends are segments.
        public bool IsWide => halfWidth.X != 0f || halfWidth.Y != 0f || halfWidth.Z != 0f;

        /// Unbound: the segment of end <paramref name="end"/> (0 the start, 1 the end).
        public void Edge(int end, out RcVec3f a, out RcVec3f b)
        {
            var c = pos[end];
            var h = end == 0 ? halfWidth : RcVec3f.Zero - halfWidth;
            a = c - h;
            b = c + h;
        }
    }
}