using System;
using System.Collections.Generic;
using Unbound.Runtime;

namespace UnityEngine
{
    // Blend shapes: named channels of frames, each frame a full weight and the deltas it adds to
    // the vertices, normals and tangents at that weight. A SkinnedMeshRenderer adds them before
    // skinning, by its weights (measured: tests/projects/blendshapes).
    public sealed partial class Mesh
    {
        internal sealed class BlendShapeFrame
        {
            internal float Weight;
            internal Vector3[] Vertices;
            /// <summary>Null when the frame has none (zero deltas).</summary>
            internal Vector3[] Normals, Tangents;

            internal BlendShapeFrame Clone() => new BlendShapeFrame
            {
                Weight = Weight,
                Vertices = (Vector3[])Vertices.Clone(),
                Normals = (Vector3[])Normals?.Clone(),
                Tangents = (Vector3[])Tangents?.Clone(),
            };
        }

        internal sealed class BlendShapeChannel
        {
            internal string Name;
            internal readonly List<BlendShapeFrame> Frames = new List<BlendShapeFrame>();
        }

        internal readonly List<BlendShapeChannel> m_BlendShapes = new List<BlendShapeChannel>();

        public int blendShapeCount => m_BlendShapes.Count;

        BlendShapeChannel Shape(int shapeIndex)
        {
            if (shapeIndex < 0 || shapeIndex >= m_BlendShapes.Count)
                throw new ArgumentException($"Shape index {shapeIndex} out of bounds (blendShapeCount is {m_BlendShapes.Count}).", nameof(shapeIndex));
            return m_BlendShapes[shapeIndex];
        }

        BlendShapeFrame Frame(int shapeIndex, int frameIndex)
        {
            var shape = Shape(shapeIndex);
            if (frameIndex < 0 || frameIndex >= shape.Frames.Count)
                throw new ArgumentException($"Frame index {frameIndex} out of bounds (blend shape {shapeIndex} has {shape.Frames.Count} frames).", nameof(frameIndex));
            return shape.Frames[frameIndex];
        }

        public void ClearBlendShapes()
        {
            m_BlendShapes.Clear();
            Changed();
        }

        public string GetBlendShapeName(int shapeIndex) => Shape(shapeIndex).Name;

        public int GetBlendShapeIndex(string blendShapeName)
        {
            for (int i = 0; i < m_BlendShapes.Count; i++)
                if (m_BlendShapes[i].Name == blendShapeName) return i;
            return -1;
        }

        public int GetBlendShapeFrameCount(int shapeIndex) => Shape(shapeIndex).Frames.Count;

        public float GetBlendShapeFrameWeight(int shapeIndex, int frameIndex) => Frame(shapeIndex, frameIndex).Weight;

        public void GetBlendShapeFrameVertices(int shapeIndex, int frameIndex, Vector3[] deltaVertices, Vector3[] deltaNormals, Vector3[] deltaTangents)
        {
            var frame = Frame(shapeIndex, frameIndex);
            int count = m_Vertices.Length;
            if ((deltaVertices != null && deltaVertices.Length != count) || (deltaNormals != null && deltaNormals.Length != count)
                || (deltaTangents != null && deltaTangents.Length != count))
                throw new ArgumentException("GetBlendShapeFrameVertices() output arrays size must match mesh vertex count");
            void Fill(Vector3[] into, Vector3[] from)
            {
                if (into == null) return;
                if (from != null) Array.Copy(from, into, Math.Min(from.Length, into.Length));
                else Array.Clear(into);
            }
            Fill(deltaVertices, frame.Vertices);
            Fill(deltaNormals, frame.Normals);
            Fill(deltaTangents, frame.Tangents);
        }

        /// <summary>A frame of a shape (a new shape the first time its name is used); frames of a
        /// shape go in increasing weights.</summary>
        public void AddBlendShapeFrame(string shapeName, float frameWeight, Vector3[] deltaVertices, Vector3[] deltaNormals, Vector3[] deltaTangents)
        {
            int count = m_Vertices.Length;
            if (deltaVertices == null) throw new ArgumentException("deltaVertices must not be null", nameof(deltaVertices));
            if (deltaVertices.Length != count) throw new ArgumentException("deltaVertices size must match the mesh vertex count", nameof(deltaVertices));
            if (deltaNormals != null && deltaNormals.Length != count) throw new ArgumentException("deltaNormals size must match the mesh vertex count", nameof(deltaNormals));
            if (deltaTangents != null && deltaTangents.Length != count) throw new ArgumentException("deltaTangents size must match the mesh vertex count", nameof(deltaTangents));
            int index = GetBlendShapeIndex(shapeName);
            var shape = index >= 0 ? m_BlendShapes[index] : null;
            if (shape != null && frameWeight <= shape.Frames[shape.Frames.Count - 1].Weight)
                throw new ArgumentException($"Blend shape frame weight {frameWeight} must be larger than the last frame's ({shape.Frames[shape.Frames.Count - 1].Weight}) of '{shapeName}'.", nameof(frameWeight));
            if (shape == null) m_BlendShapes.Add(shape = new BlendShapeChannel { Name = shapeName });
            shape.Frames.Add(new BlendShapeFrame
            {
                Weight = frameWeight,
                Vertices = (Vector3[])deltaVertices.Clone(),
                Normals = (Vector3[])deltaNormals?.Clone(),
                Tangents = (Vector3[])deltaTangents?.Clone(),
            });
            Changed();
        }

        void CopyBlendShapesFrom(Mesh source)
        {
            m_BlendShapes.Clear();
            foreach (var s in source.m_BlendShapes)
            {
                var copy = new BlendShapeChannel { Name = s.Name };
                foreach (var f in s.Frames) copy.Frames.Add(f.Clone());
                m_BlendShapes.Add(copy);
            }
        }

        /// <summary>Adds the shapes at the weights (a shape's weight in percent, as the
        /// SkinnedMeshRenderer keeps them) to copies of the vertices, normals and tangents: below the
        /// first frame its deltas scaled by weight / its weight (negative weights too); between two
        /// frames the two interpolated; past the last frame the last frame's deltas scaled by where
        /// the weight lies past the frame before it (measured: tests/projects/blendshapes).</summary>
        internal void AddBlendShapes(IReadOnlyList<float> weights, Vector3[] vertices, Vector3[] normals, Vector4[] tangents)
        {
            for (int s = 0; s < m_BlendShapes.Count && s < weights.Count; s++)
            {
                float w = weights[s];
                var frames = m_BlendShapes[s].Frames;
                if (w == 0f || frames.Count == 0 || frames[0].Vertices.Length != vertices.Length) continue;
                var first = frames[0];
                if (frames.Count == 1 || w <= first.Weight)
                {
                    if (first.Weight != 0f) Add(first, w / first.Weight, null, 0f);
                    continue;
                }
                var last = frames[frames.Count - 1];
                if (w >= last.Weight)
                {
                    float before = frames[frames.Count - 2].Weight;
                    Add(last, (w - before) / (last.Weight - before), null, 0f);
                    continue;
                }
                int j = 0;
                while (frames[j + 1].Weight < w) j++;
                float t = (w - frames[j].Weight) / (frames[j + 1].Weight - frames[j].Weight);
                Add(frames[j], 1f - t, frames[j + 1], t);
            }

            void Add(BlendShapeFrame a, float wa, BlendShapeFrame b, float wb)
            {
                for (int i = 0; i < vertices.Length; i++)
                {
                    vertices[i] += a.Vertices[i] * wa + (b != null ? b.Vertices[i] * wb : Vector3.zero);
                    if (normals != null)
                        normals[i] += (a.Normals != null ? a.Normals[i] * wa : Vector3.zero) + (b?.Normals != null ? b.Normals[i] * wb : Vector3.zero);
                    if (tangents != null)
                    {
                        var d = (a.Tangents != null ? a.Tangents[i] * wa : Vector3.zero) + (b?.Tangents != null ? b.Tangents[i] * wb : Vector3.zero);
                        tangents[i] = new Vector4(tangents[i].x + d.x, tangents[i].y + d.y, tangents[i].z + d.z, tangents[i].w);
                    }
                }
            }
        }

        /// <summary>Unity's serialized shapes (m_Shapes): the frames' deltas, vertex by vertex with
        /// their indices, each frame a range of them; the channels name runs of frames, with their
        /// weights in fullWeights.</summary>
        void ReadBlendShapes(YNode shapes, int count)
        {
            m_BlendShapes.Clear();
            if (shapes == null) return;
            var deltas = new List<YNode>(shapes.Get("vertices")?.Seq() ?? Array.Empty<YNode>());
            var frames = new List<YNode>(shapes.Get("shapes")?.Seq() ?? Array.Empty<YNode>());
            var weights = new List<YNode>(shapes.Get("fullWeights")?.Seq() ?? Array.Empty<YNode>());
            foreach (var channel in shapes.Get("channels")?.Seq() ?? Array.Empty<YNode>())
            {
                var shape = new BlendShapeChannel { Name = channel.Str("name") };
                int start = channel.Int("frameIndex"), frameCount = channel.Int("frameCount");
                for (int f = start; f < start + frameCount && f < frames.Count; f++)
                {
                    var range = frames[f];
                    var frame = new BlendShapeFrame
                    {
                        Weight = f < weights.Count ? weights[f].AsFloat() : 100f,
                        Vertices = new Vector3[count],
                        Normals = range.Bool("hasNormals") ? new Vector3[count] : null,
                        Tangents = range.Bool("hasTangents") ? new Vector3[count] : null,
                    };
                    int first = range.Int("firstVertex"), n = range.Int("vertexCount");
                    for (int k = first; k < first + n && k < deltas.Count; k++)
                    {
                        var d = deltas[k];
                        int index = d.Int("index");
                        if (index < 0 || index >= count) continue;
                        frame.Vertices[index] = Vec3(d.Get("vertex"));
                        if (frame.Normals != null) frame.Normals[index] = Vec3(d.Get("normal"));
                        if (frame.Tangents != null) frame.Tangents[index] = Vec3(d.Get("tangent"));
                    }
                    shape.Frames.Add(frame);
                }
                m_BlendShapes.Add(shape);
            }
        }
    }
}
