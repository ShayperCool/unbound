using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text.Json;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>An imported Mesh takes its bounds from the importer, which includes every blend shape
    /// frame's positions (measured, Unity 6000.0.71f1: tests/projects/modelaxis Skinned set,
    /// BlenderBlob). An answer without bounds keeps the bounds of its vertices.</summary>
    public class ImportedMeshBoundsTests
    {
        static string Floats(params float[] values) => Convert.ToBase64String(MemoryMarshal.AsBytes(values.AsSpan()).ToArray());

        static string Ints(params int[] values) => Convert.ToBase64String(MemoryMarshal.AsBytes(values.AsSpan()).ToArray());

        static Mesh Imported(bool withBounds)
        {
            var answer = new Dictionary<string, object>
            {
                ["name"] = "Tri",
                ["vertexCount"] = 3,
                ["positions"] = Floats(0, 0, 0, 0, 1, 0, -1, 0, 0),
                ["submeshes"] = new[] { Ints(0, 1, 2) },
                ["blendShapes"] = new[]
                {
                    new Dictionary<string, object>
                    {
                        ["name"] = "Up",
                        ["frames"] = new[]
                        {
                            new Dictionary<string, object>
                            {
                                ["weight"] = 100f,
                                ["vertices"] = Floats(0, 0.5f, 0, 0, 0.5f, 0, 0, 0.5f, 0),
                            },
                        },
                    },
                },
            };
            if (withBounds)
            {
                // The base vertices and the frame's shaped ones.
                answer["boundsMin"] = new[] { -1f, 0f, 0f };
                answer["boundsMax"] = new[] { 0f, 1.5f, 0f };
            }
            var mesh = new Mesh { name = "Tri" };
            mesh.FillFromModel(JsonDocument.Parse(JsonSerializer.Serialize(answer)).RootElement);
            return mesh;
        }

        [Fact]
        public void ImportedBoundsHoldTheBlendShapeFrames()
        {
            var bounds = Imported(true).bounds;
            Assert.Equal(new Vector3(-1, 0, 0), bounds.min);
            Assert.Equal(new Vector3(0, 1.5f, 0), bounds.max);
        }

        [Fact]
        public void AnswersWithoutBoundsUseTheVertices()
        {
            var bounds = Imported(false).bounds;
            Assert.Equal(new Vector3(-1, 0, 0), bounds.min);
            Assert.Equal(new Vector3(0, 1, 0), bounds.max);
        }
    }
}
