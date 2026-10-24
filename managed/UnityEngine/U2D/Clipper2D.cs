using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Unity.Collections;
using Unbound.Clipper;
using ClipperLib = Unbound.Clipper.Clipper;

namespace UnityEngine.U2D
{
    /// <summary>
    /// The engine's polygon clipper (URP's 2D shadows merge shapes with it): Clipper 6.4.2, which
    /// Unbound carries (CompositeCollider2D) and whose enums these mirror. Points are scaled by
    /// <c>inIntScale</c> to Clipper's integers (rounded or truncated) and back.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Size = 1)]
    internal partial struct Clipper2D
    {
        public enum ClipType { ctIntersection = 0, ctUnion = 1, ctDifference = 2, ctXor = 3 }
        public enum PolyType { ptSubject = 0, ptClip = 1 }
        public enum PolyFillType { pftEvenOdd = 0, pftNonZero = 1, pftPositive = 2, pftNegative = 3 }
        public enum InitOptions { ioDefault = 0, oReverseSolution = 1, ioStrictlySimple = 2, ioPreserveCollinear = 4 }

        public partial struct PathArguments
        {
            public PolyType polyType;
            public bool closed;

            public PathArguments(PolyType inPolyType = PolyType.ptSubject, bool inClosed = false)
            {
                polyType = inPolyType;
                closed = inClosed;
            }
        }

        public partial struct ExecuteArguments
        {
            public InitOptions initOption;
            public ClipType clipType;
            public PolyFillType subjFillType;
            public PolyFillType clipFillType;
            public bool reverseSolution;
            public bool strictlySimple;
            public bool preserveColinear;

            public ExecuteArguments(InitOptions inInitOption = InitOptions.ioDefault, ClipType inClipType = ClipType.ctIntersection, PolyFillType inSubjFillType = PolyFillType.pftEvenOdd,
                PolyFillType inClipFillType = PolyFillType.pftEvenOdd, bool inReverseSolution = false, bool inStrictlySimple = false, bool inPreserveColinear = false)
            {
                initOption = inInitOption;
                clipType = inClipType;
                subjFillType = inSubjFillType;
                clipFillType = inClipFillType;
                reverseSolution = inReverseSolution;
                strictlySimple = inStrictlySimple;
                preserveColinear = inPreserveColinear;
            }
        }

        /// <summary>The result paths: their points one path after another, each path's size and
        /// bounds.</summary>
        public partial struct Solution : IDisposable
        {
            public NativeArray<Vector2> points;
            public NativeArray<int> pathSizes;
            public NativeArray<Rect> boundingRect;

            public Solution(int pointsBufferSize, int pathSizesBufferSize, Allocator allocator)
            {
                points = new NativeArray<Vector2>(pointsBufferSize, allocator);
                pathSizes = new NativeArray<int>(pathSizesBufferSize, allocator);
                boundingRect = new NativeArray<Rect>(pathSizesBufferSize, allocator);
            }

            public void Dispose()
            {
                if (points.IsCreated) points.Dispose();
                if (pathSizes.IsCreated) pathSizes.Dispose();
                if (boundingRect.IsCreated) boundingRect.Dispose();
            }
        }

        public static void Execute(ref Solution solution, NativeArray<Vector2> inPoints, NativeArray<int> inPathSizes, NativeArray<PathArguments> inPathArguments,
            ExecuteArguments inExecuteArguments, Allocator inSolutionAllocator, int inIntScale = 65536, bool useRounding = false)
        {
            int options = (int)inExecuteArguments.initOption;
            var clipper = new ClipperLib(options)
            {
                ReverseSolution = inExecuteArguments.reverseSolution || (options & 1) != 0,
                StrictlySimple = inExecuteArguments.strictlySimple || (options & 2) != 0,
                PreserveCollinear = inExecuteArguments.preserveColinear || (options & 4) != 0,
            };
            bool open = false;
            int at = 0;
            for (int p = 0; p < inPathSizes.Length; p++)
            {
                var path = ClipperPaths.ToPath(inPoints, at, inPathSizes[p], inIntScale, useRounding);
                at += inPathSizes[p];
                var args = p < inPathArguments.Length ? inPathArguments[p] : new PathArguments(PolyType.ptSubject, true);
                open |= !args.closed;
                clipper.AddPath(path, (Unbound.Clipper.PolyType)args.polyType, args.closed);
            }
            List<List<IntPoint>> result;
            var clip = (Unbound.Clipper.ClipType)inExecuteArguments.clipType;
            var subj = (Unbound.Clipper.PolyFillType)inExecuteArguments.subjFillType;
            var clipFill = (Unbound.Clipper.PolyFillType)inExecuteArguments.clipFillType;
            if (open)
            {
                var tree = new PolyTree();
                clipper.Execute(clip, tree, subj, clipFill);
                result = ClipperLib.PolyTreeToPaths(tree);
            }
            else
            {
                result = new List<List<IntPoint>>();
                clipper.Execute(clip, result, subj, clipFill);
            }
            solution.Dispose();
            ClipperPaths.ToSolution(result, inIntScale, inSolutionAllocator, out solution.points, out solution.pathSizes, out solution.boundingRect);
        }
    }

    /// <summary>The engine's polygon offsetter (Clipper 6.4.2's ClipperOffset), as
    /// <see cref="Clipper2D"/>.</summary>
    [StructLayout(LayoutKind.Sequential, Size = 1)]
    internal partial struct ClipperOffset2D
    {
        public enum JoinType { jtSquare = 0, jtRound = 1, jtMiter = 2 }
        public enum EndType { etClosedPolygon = 0, etClosedLine = 1, etOpenButt = 2, etOpenSquare = 3, etOpenRound = 4 }

        public partial struct PathArguments
        {
            public JoinType joinType;
            public EndType endType;

            public PathArguments(JoinType inJoinType = JoinType.jtSquare, EndType inEndType = EndType.etClosedPolygon)
            {
                joinType = inJoinType;
                endType = inEndType;
            }
        }

        public partial struct Solution
        {
            public NativeArray<Vector2> points;
            public NativeArray<int> pathSizes;

            public Solution(int pointsBufferSize, int pathSizesBufferSize, Allocator allocator)
            {
                points = new NativeArray<Vector2>(pointsBufferSize, allocator);
                pathSizes = new NativeArray<int>(pathSizesBufferSize, allocator);
            }

            public void Dispose()
            {
                if (points.IsCreated) points.Dispose();
                if (pathSizes.IsCreated) pathSizes.Dispose();
            }
        }

        /// <summary>The paths grown by <paramref name="inDelta"/> (in points, scaled as the clipper's).
        /// <paramref name="inRoundPrecision"/> is taken as the arc tolerance when none is given.</summary>
        public static void Execute(ref Solution solution, NativeArray<Vector2> inPoints, NativeArray<int> inPathSizes, NativeArray<PathArguments> inPathArguments,
            Allocator inSolutionAllocator, double inDelta = 0, double inMiterLimit = 2, double inRoundPrecision = 0.25, double inArcTolerance = 0, double inIntScale = 65536, bool useRounding = false)
        {
            double tolerance = inArcTolerance > 0 ? inArcTolerance : inRoundPrecision;
            var offset = new ClipperOffset(inMiterLimit, tolerance * inIntScale);
            int at = 0;
            for (int p = 0; p < inPathSizes.Length; p++)
            {
                var path = ClipperPaths.ToPath(inPoints, at, inPathSizes[p], inIntScale, useRounding);
                at += inPathSizes[p];
                var args = p < inPathArguments.Length ? inPathArguments[p] : new PathArguments();
                offset.AddPath(path, (Unbound.Clipper.JoinType)args.joinType, (Unbound.Clipper.EndType)args.endType);
            }
            var result = new List<List<IntPoint>>();
            offset.Execute(ref result, inDelta * inIntScale);
            solution.Dispose();
            ClipperPaths.ToSolution(result, inIntScale, inSolutionAllocator, out solution.points, out solution.pathSizes, out _);
        }
    }

    static class ClipperPaths
    {
        internal static List<IntPoint> ToPath(NativeArray<Vector2> points, int start, int count, double scale, bool round)
        {
            var path = new List<IntPoint>(count);
            for (int i = 0; i < count; i++)
            {
                var v = points[start + i];
                double x = v.x * scale, y = v.y * scale;
                path.Add(round ? new IntPoint((long)Math.Round(x), (long)Math.Round(y)) : new IntPoint((long)x, (long)y));
            }
            return path;
        }

        internal static void ToSolution(List<List<IntPoint>> paths, double scale, Allocator allocator,
            out NativeArray<Vector2> points, out NativeArray<int> sizes, out NativeArray<Rect> bounds)
        {
            int total = 0;
            foreach (var p in paths) total += p.Count;
            points = new NativeArray<Vector2>(total, allocator);
            sizes = new NativeArray<int>(paths.Count, allocator);
            bounds = new NativeArray<Rect>(paths.Count, allocator);
            int at = 0;
            for (int i = 0; i < paths.Count; i++)
            {
                var path = paths[i];
                sizes[i] = path.Count;
                float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
                foreach (var q in path)
                {
                    var v = new Vector2((float)(q.X / scale), (float)(q.Y / scale));
                    points[at++] = v;
                    minX = Math.Min(minX, v.x); minY = Math.Min(minY, v.y);
                    maxX = Math.Max(maxX, v.x); maxY = Math.Max(maxY, v.y);
                }
                bounds[i] = path.Count > 0 ? Rect.MinMaxRect(minX, minY, maxX, maxY) : default;
            }
        }
    }
}
