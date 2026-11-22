using UnityEngine;
using UnityEngine.VFX;

namespace UnityEditor.VFX
{
    /// <summary>
    /// The noise the graph compiler folds over constants: what an expression computes on the CPU
    /// (<see cref="VFXNoise"/>). Each result is (value, derivative per coordinate); a curl is a
    /// vector. Matches the Unity 6000.0.71f1 Editor bit for bit, tests/projects/vfx <c>noise</c>.
    /// </summary>
    internal partial class VFXExpressionNoise
    {
        internal static Vector2 GenerateValueNoise1D(float coordinate, float frequency, int octaveCount, float persistence, float lacunarity) => VFXNoise.Noise1D(VFXNoise.Kind.Value, coordinate, frequency, octaveCount, persistence, lacunarity);
        internal static Vector3 GenerateValueNoise2D(Vector2 coordinate, float frequency, int octaveCount, float persistence, float lacunarity) => VFXNoise.Noise2D(VFXNoise.Kind.Value, coordinate, frequency, octaveCount, persistence, lacunarity);
        internal static Vector4 GenerateValueNoise3D(Vector3 coordinate, float frequency, int octaveCount, float persistence, float lacunarity) => VFXNoise.Noise3D(VFXNoise.Kind.Value, coordinate, frequency, octaveCount, persistence, lacunarity);
        internal static Vector2 GenerateValueCurlNoise2D(Vector2 coordinate, float frequency, int octaveCount, float persistence, float lacunarity) => VFXNoise.Curl2D(VFXNoise.Kind.Value, coordinate, frequency, octaveCount, persistence, lacunarity);
        internal static Vector3 GenerateValueCurlNoise3D(Vector3 coordinate, float frequency, int octaveCount, float persistence, float lacunarity) => VFXNoise.Curl3D(VFXNoise.Kind.Value, coordinate, frequency, octaveCount, persistence, lacunarity);
        internal static Vector2 GeneratePerlinNoise1D(float coordinate, float frequency, int octaveCount, float persistence, float lacunarity) => VFXNoise.Noise1D(VFXNoise.Kind.Perlin, coordinate, frequency, octaveCount, persistence, lacunarity);
        internal static Vector3 GeneratePerlinNoise2D(Vector2 coordinate, float frequency, int octaveCount, float persistence, float lacunarity) => VFXNoise.Noise2D(VFXNoise.Kind.Perlin, coordinate, frequency, octaveCount, persistence, lacunarity);
        internal static Vector4 GeneratePerlinNoise3D(Vector3 coordinate, float frequency, int octaveCount, float persistence, float lacunarity) => VFXNoise.Noise3D(VFXNoise.Kind.Perlin, coordinate, frequency, octaveCount, persistence, lacunarity);
        internal static Vector2 GeneratePerlinCurlNoise2D(Vector2 coordinate, float frequency, int octaveCount, float persistence, float lacunarity) => VFXNoise.Curl2D(VFXNoise.Kind.Perlin, coordinate, frequency, octaveCount, persistence, lacunarity);
        internal static Vector3 GeneratePerlinCurlNoise3D(Vector3 coordinate, float frequency, int octaveCount, float persistence, float lacunarity) => VFXNoise.Curl3D(VFXNoise.Kind.Perlin, coordinate, frequency, octaveCount, persistence, lacunarity);
        internal static Vector2 GenerateCellularNoise1D(float coordinate, float frequency, int octaveCount, float persistence, float lacunarity) => VFXNoise.Noise1D(VFXNoise.Kind.Cellular, coordinate, frequency, octaveCount, persistence, lacunarity);
        internal static Vector3 GenerateCellularNoise2D(Vector2 coordinate, float frequency, int octaveCount, float persistence, float lacunarity) => VFXNoise.Noise2D(VFXNoise.Kind.Cellular, coordinate, frequency, octaveCount, persistence, lacunarity);
        internal static Vector4 GenerateCellularNoise3D(Vector3 coordinate, float frequency, int octaveCount, float persistence, float lacunarity) => VFXNoise.Noise3D(VFXNoise.Kind.Cellular, coordinate, frequency, octaveCount, persistence, lacunarity);
        internal static Vector2 GenerateCellularCurlNoise2D(Vector2 coordinate, float frequency, int octaveCount, float persistence, float lacunarity) => VFXNoise.Curl2D(VFXNoise.Kind.Cellular, coordinate, frequency, octaveCount, persistence, lacunarity);
        internal static Vector3 GenerateCellularCurlNoise3D(Vector3 coordinate, float frequency, int octaveCount, float persistence, float lacunarity) => VFXNoise.Curl3D(VFXNoise.Kind.Cellular, coordinate, frequency, octaveCount, persistence, lacunarity);
        /// <summary>No number, whatever it is given (Unity 6000.0.71f1, tests/projects/vfx
        /// <c>noise/api/GenerateVoroNoise2D</c>): the Voronoi noise is computed in kernels only,
        /// and the graph compiler refuses it on the CPU.</summary>
        internal static float GenerateVoroNoise2D(Vector2 coordinate, float frequency, float warp, float smoothness) => float.NaN;
    }
}
