using System;
using V2 = System.Numerics.Vector2;
using V3 = System.Numerics.Vector3;
using V4 = System.Numerics.Vector4;

namespace UnityEngine.VFX
{
    /// <summary>
    /// The noise a graph's expressions compute on the CPU: value, gradient (Perlin) and cellular
    /// noise over one, two and three coordinates with their derivatives, and the curl of each. The lattice hashes and the formulas are those of the Visual Effect
    /// Graph package's shader library (Shaders/VFXNoise.hlsl), so a value computed here and one
    /// computed in a kernel agree. A sample is (value, derivative per coordinate).
    /// </summary>
    internal static class VFXNoise
    {
        internal enum Kind { Value, Perlin, Cellular }

        // The hashes' constants (the package's shader library).
        const float Domain2 = 71f, Domain3 = 69f;
        const float Offset2X = 26f, Offset3X = 50f, OffsetY = 161f;
        const float Large2A = 951.135664f, Large2B = 642.949883f;
        const float Large3A = 635.298681f, Large3B = 682.357502f, Large3C = 668.926525f;
        const float Zinc3A = 48.500388f, Zinc3B = 65.294118f, Zinc3C = 63.934599f;
        /// <summary>What a gradient's hash is centred on: just under a half, so no gradient is zero.</summary>
        const float GradientCentre = 0.49999f;
        /// <summary>How far a cell's point strays from its corner, over two and over three coordinates.</summary>
        const float Jitter2 = 0.25f, Jitter3 = 0.166666666f;
        const float Perlin1Scale = 2f, Perlin2Scale = 1.4142135623730950488f, Perlin3Scale = 1.1547005383792515290f;
        const float Cellular2Scale = 1f / 1.125f, Cellular3Scale = 9f / 12f;
        /// <summary>The offsets of the second and third planes a curl over three coordinates samples.</summary>
        const float CurlPlane1 = 100f, CurlPlane2 = 200f;

        /// <summary>The whole part below a number, through a 32-bit integer: a number outside
        /// its range, or none, gives the least integer, as the Editor's noise does (its results
        /// for infinite, absent and huge coordinates, tests/projects/vfx <c>noise</c>).</summary>
        static float Floor(float x)
        {
            float whole = x >= -2147483648f && x < 2147483648f ? (int)x : -2147483648f;
            return whole > x ? whole - 1f : whole;
        }

        static float Frac(float x) => x - Floor(x);
        static V4 Floor(V4 v) => new V4(Floor(v.X), Floor(v.Y), Floor(v.Z), Floor(v.W));
        static V4 Frac(V4 v) => new V4(Frac(v.X), Frac(v.Y), Frac(v.Z), Frac(v.W));
        static V4 Lerp(V4 a, V4 b, V4 t) => a + (b - a) * t;
        static float Lerp(float a, float b, float t) => a + (b - a) * t;
        static V4 InverseRoot(V4 v) => new V4(1f / MathF.Sqrt(v.X), 1f / MathF.Sqrt(v.Y), 1f / MathF.Sqrt(v.Z), 1f / MathF.Sqrt(v.W));
        static float Sign(float x) => x > 0f ? 1f : x < 0f ? -1f : 0f;
        static float Step(float edge, float x) => x >= edge ? 1f : 0f;

        // The quintic blend 6t^5 - 15t^4 + 10t^3 and its derivative. The noise over one and two
        // coordinates computes both in one four-term form, whose rounding differs.
        static float Blend(float x) => x * x * x * (x * (x * 6f - 15f) + 10f);
        static float BlendDerivative(float x) => x * x * (x * (x * 30f - 60f) + 30f);
        static float PairedBlend(float x) => x * x * (x * (x * (x * 6f + -15f) + 10f) + 0f);
        static float PairedBlendDerivative(float x) => x * x * (x * (x * (x * 0f + 30f) + -60f) + 30f);

        /// <summary>One number per corner of a cell over two coordinates.</summary>
        static V4 Corners2(float x, float y)
        {
            var p = new V4(x, y, x + 1f, y + 1f);
            p -= Floor(p * (1f / Domain2)) * Domain2;
            p += new V4(Offset2X, OffsetY, Offset2X, OffsetY);
            p *= p;
            return new V4(p.X, p.Z, p.X, p.Z) * new V4(p.Y, p.Y, p.W, p.W);
        }

        static V4 Hash2(float x, float y) => Frac(Corners2(x, y) * (1f / Large2A));

        static void Hash2(float x, float y, out V4 a, out V4 b)
        {
            var p = Corners2(x, y);
            a = Frac(p * (1f / Large2A));
            b = Frac(p * (1f / Large2B));
        }

        /// <summary>The products a cell's corners over three coordinates hash, with the cell's
        /// own and next plane.</summary>
        static V4 Corners3(ref float x, ref float y, ref float z, out float nextZ)
        {
            x -= Floor(x * (1f / Domain3)) * Domain3;
            y -= Floor(y * (1f / Domain3)) * Domain3;
            z -= Floor(z * (1f / Domain3)) * Domain3;
            float nextX = Step(x, Domain3 - 1.5f) * (x + 1f);
            float nextY = Step(y, Domain3 - 1.5f) * (y + 1f);
            nextZ = Step(z, Domain3 - 1.5f) * (z + 1f);
            var p = new V4(x, y, nextX, nextY) + new V4(Offset3X, OffsetY, Offset3X, OffsetY);
            p *= p;
            return new V4(p.X, p.Z, p.X, p.Z) * new V4(p.Y, p.Y, p.W, p.W);
        }

        static void Hash3(float x, float y, float z, out V4 low, out V4 high)
        {
            var p = Corners3(ref x, ref y, ref z, out float nextZ);
            low = Frac(p * (1f / (Large3A + z * Zinc3A)));
            high = Frac(p * (1f / (Large3A + nextZ * Zinc3A)));
        }

        static void Hash3(float x, float y, float z, out V4 low0, out V4 low1, out V4 low2, out V4 high0, out V4 high1, out V4 high2)
        {
            var p = Corners3(ref x, ref y, ref z, out float nextZ);
            low0 = Frac(p * (1f / (Large3A + z * Zinc3A)));
            high0 = Frac(p * (1f / (Large3A + nextZ * Zinc3A)));
            low1 = Frac(p * (1f / (Large3B + z * Zinc3B)));
            high1 = Frac(p * (1f / (Large3B + nextZ * Zinc3B)));
            low2 = Frac(p * (1f / (Large3C + z * Zinc3C)));
            high2 = Frac(p * (1f / (Large3C + nextZ * Zinc3C)));
        }

        /// <summary>A hash in 0..1 as a point's offset in -1..1, weighted towards the ends.</summary>
        static V4 CellWeights(V4 samples)
        {
            samples = samples * 2f - V4.One;
            return samples * samples * samples - new V4(Sign(samples.X), Sign(samples.Y), Sign(samples.Z), Sign(samples.W));
        }

        static V2 Value1(float c)
        {
            float i = Floor(c), f = c - i;
            // The corners of a cell over one coordinate: the two ends of a row of the lattice.
            float a = i - Floor(i * (1f / Domain2)) * Domain2 + Offset2X;
            float b = i + 1f - Floor((i + 1f) * (1f / Domain2)) * Domain2 + Offset2X;
            const float row = OffsetY * OffsetY;
            float h0 = Frac(a * a * row * (1f / Large2A)), h1 = Frac(b * b * row * (1f / Large2A));
            float delta = h1 - h0;
            return new V2(h0 + delta * PairedBlend(f), delta * PairedBlendDerivative(f));
        }

        static V3 Value2(V2 c)
        {
            float ix = Floor(c.X), iy = Floor(c.Y), fx = c.X - ix, fy = c.Y - iy;
            var hash = Hash2(ix, iy);
            float bx = PairedBlend(fx), by = PairedBlend(fy);
            var res = Lerp(new V4(hash.X, hash.Y, hash.X, hash.Z), new V4(hash.Z, hash.W, hash.Y, hash.W), new V4(by, by, bx, bx));
            float dx = res.Y - res.X, dy = res.W - res.Z;
            return new V3(res.X + dx * bx, dx * PairedBlendDerivative(fx), dy * PairedBlendDerivative(fy));
        }

        static V4 Value3(V3 c)
        {
            float ix = Floor(c.X), iy = Floor(c.Y), iz = Floor(c.Z);
            float fx = c.X - ix, fy = c.Y - iy, fz = c.Z - iz;
            Hash3(ix, iy, iz, out var low, out var high);
            float bx = Blend(fx), by = Blend(fy), bz = Blend(fz);
            var res0 = Lerp(low, high, new V4(bz));
            var res1 = Lerp(new V4(res0.X, res0.Y, res0.X, res0.Z), new V4(res0.Z, res0.W, res0.Y, res0.W), new V4(by, by, bx, bx));
            var res3 = Lerp(new V4(low.X, low.Y, high.X, high.Y), new V4(low.Z, low.W, high.Z, high.W), new V4(by));
            float r4x = Lerp(res3.X, res3.Y, bx), r4y = Lerp(res3.Z, res3.W, bx);
            float dx = res1.Y - res1.X, dy = res1.W - res1.Z, dz = r4y - r4x;
            return new V4(res1.X + dx * bx, dx * BlendDerivative(fx), dy * BlendDerivative(fy), dz * BlendDerivative(fz));
        }

        /// <summary>Gradient noise over two coordinates: (value, d/dx, d/dy) before its scale.</summary>
        static V3 Gradient2(float cx, float cy)
        {
            float ix = Floor(cx), iy = Floor(cy);
            float fx = cx - ix, fy = cy - iy, gx = cx - (ix + 1f), gy = cy - (iy + 1f);
            Hash2(ix, iy, out var hashX, out var hashY);
            var gradX = hashX - new V4(GradientCentre);
            var gradY = hashY - new V4(GradientCentre);
            var norm = InverseRoot(gradX * gradX + gradY * gradY);
            gradX *= norm;
            gradY *= norm;
            var dot = gradX * new V4(fx, gx, fx, gx) + gradY * new V4(fy, fy, gy, gy);
            var p0 = new V3(dot.X, gradX.X, gradY.X);
            var p1 = new V3(dot.Y, gradX.Y, gradY.Y);
            var p2 = new V3(dot.Z, gradX.Z, gradY.Z);
            var p3 = new V3(dot.W, gradX.W, gradY.W);
            var k0 = p1 - p0;
            var k1 = p2 - p0;
            var k2 = p3 - p2 - k0;
            float bx = PairedBlend(fx), by = PairedBlend(fy);
            var results = p0 + bx * k0 + by * (k1 + bx * k2);
            results.Y += PairedBlendDerivative(fx) * (k0.X + by * k2.X);
            results.Z += PairedBlendDerivative(fy) * (k1.X + bx * k2.X);
            return results;
        }

        static V2 Perlin1(float c)
        {
            var r = Gradient2(c, 0f);
            return new V2(r.X * Perlin1Scale, r.Y * Perlin1Scale);
        }

        static V3 Perlin2(V2 c) => Gradient2(c.X, c.Y) * Perlin2Scale;

        static V4 Perlin3(V3 c)
        {
            float ix = Floor(c.X), iy = Floor(c.Y), iz = Floor(c.Z);
            float fx = c.X - ix, fy = c.Y - iy, fz = c.Z - iz;
            float gx = fx - 1f, gy = fy - 1f, gz = fz - 1f;
            Hash3(ix, iy, iz, out var x0, out var y0, out var z0, out var x1, out var y1, out var z1);
            var centre = new V4(GradientCentre);
            x0 -= centre; y0 -= centre; z0 -= centre;
            x1 -= centre; y1 -= centre; z1 -= centre;
            var norm0 = InverseRoot(x0 * x0 + y0 * y0 + z0 * z0);
            var norm1 = InverseRoot(x1 * x1 + y1 * y1 + z1 * z1);
            x0 *= norm0; y0 *= norm0; z0 *= norm0;
            x1 *= norm1; y1 *= norm1; z1 *= norm1;
            var alongX = new V4(fx, gx, fx, gx);
            var alongY = new V4(fy, fy, gy, gy);
            var dot0 = alongX * x0 + alongY * y0 + new V4(fz) * z0;
            var dot1 = alongX * x1 + alongY * y1 + new V4(gz) * z1;
            var p0 = new V4(dot0.X, x0.X, y0.X, z0.X);
            var p1 = new V4(dot0.Y, x0.Y, y0.Y, z0.Y);
            var p2 = new V4(dot0.Z, x0.Z, y0.Z, z0.Z);
            var p3 = new V4(dot0.W, x0.W, y0.W, z0.W);
            var p4 = new V4(dot1.X, x1.X, y1.X, z1.X);
            var p5 = new V4(dot1.Y, x1.Y, y1.Y, z1.Y);
            var p6 = new V4(dot1.Z, x1.Z, y1.Z, z1.Z);
            var p7 = new V4(dot1.W, x1.W, y1.W, z1.W);
            var k0 = p1 - p0;
            var k1 = p2 - p0;
            var k2 = p4 - p0;
            var k3 = p3 - p2 - k0;
            var k4 = p5 - p4 - k0;
            var k5 = p6 - p4 - k1;
            var k6 = (p7 - p6) - (p5 - p4) - k3;
            float u = Blend(fx), v = Blend(fy), w = Blend(fz);
            float du = BlendDerivative(fx), dv = BlendDerivative(fy), dw = BlendDerivative(fz);
            var result = p0 + u * (k0 + v * k3) + v * (k1 + w * k5) + w * (k2 + u * (k4 + v * k6));
            result.Y += V4.Dot(new V4(k0.X, k3.X * v, k4.X * w, k6.X * v * w), new V4(du));
            result.Z += V4.Dot(new V4(k1.X, k3.X * u, k5.X * w, k6.X * u * w), new V4(dv));
            result.W += V4.Dot(new V4(k2.X, k4.X * u, k5.X * v, k6.X * u * v), new V4(dw));
            return result * Perlin3Scale;
        }

        /// <summary>The nearest of a cell's four points over two coordinates: (squared distance,
        /// offset along x, offset along y).</summary>
        static V3 Nearest2(float cx, float cy)
        {
            float ix = Floor(cx), iy = Floor(cy), fx = cx - ix, fy = cy - iy;
            Hash2(ix, iy, out var hashX, out var hashY);
            hashX = CellWeights(hashX) * Jitter2 + new V4(0f, 1f, 0f, 1f);
            hashY = CellWeights(hashY) * Jitter2 + new V4(0f, 0f, 1f, 1f);
            var dx = new V4(fx) - hashX;
            var dy = new V4(fy) - hashY;
            var d = dx * dx + dy * dy;
            var t1 = d.X < d.Y ? new V3(d.X, dx.X, dy.X) : new V3(d.Y, dx.Y, dy.Y);
            var t2 = d.Z < d.W ? new V3(d.Z, dx.Z, dy.Z) : new V3(d.W, dx.W, dy.W);
            return t1.X < t2.X ? t1 : t2;
        }

        static V2 Cellular1(float c)
        {
            var t = Nearest2(c, 0f);
            return new V2(t.X * Cellular2Scale, t.Y * 2f * Cellular2Scale);
        }

        static V3 Cellular2(V2 c) => Nearest2(c.X, c.Y) * new V3(1f, 2f, 2f) * Cellular2Scale;

        static V4 Cellular3(V3 c)
        {
            float ix = Floor(c.X), iy = Floor(c.Y), iz = Floor(c.Z);
            float fx = c.X - ix, fy = c.Y - iy, fz = c.Z - iz;
            Hash3(ix, iy, iz, out var x0, out var y0, out var z0, out var x1, out var y1, out var z1);
            x0 = CellWeights(x0) * Jitter3 + new V4(0f, 1f, 0f, 1f);
            y0 = CellWeights(y0) * Jitter3 + new V4(0f, 0f, 1f, 1f);
            x1 = CellWeights(x1) * Jitter3 + new V4(0f, 1f, 0f, 1f);
            y1 = CellWeights(y1) * Jitter3 + new V4(0f, 0f, 1f, 1f);
            z0 = CellWeights(z0) * Jitter3 + V4.Zero;
            z1 = CellWeights(z1) * Jitter3 + V4.One;
            V4 dx1 = new V4(fx) - x0, dy1 = new V4(fy) - y0, dz1 = new V4(fz) - z0;
            V4 dx2 = new V4(fx) - x1, dy2 = new V4(fy) - y1, dz2 = new V4(fz) - z1;
            var d1 = dx1 * dx1 + dy1 * dy1 + dz1 * dz1;
            var d2 = dx2 * dx2 + dy2 * dy2 + dz2 * dz2;
            var r1 = d1.X < d1.Y ? new V4(d1.X, dx1.X, dy1.X, dz1.X) : new V4(d1.Y, dx1.Y, dy1.Y, dz1.Y);
            var r2 = d1.Z < d1.W ? new V4(d1.Z, dx1.Z, dy1.Z, dz1.Z) : new V4(d1.W, dx1.W, dy1.W, dz1.W);
            var r3 = d2.X < d2.Y ? new V4(d2.X, dx2.X, dy2.X, dz2.X) : new V4(d2.Y, dx2.Y, dy2.Y, dz2.Y);
            var r4 = d2.Z < d2.W ? new V4(d2.Z, dx2.Z, dy2.Z, dz2.Z) : new V4(d2.W, dx2.W, dy2.W, dz2.W);
            var t1 = r1.X < r2.X ? r1 : r2;
            var t2 = r3.X < r4.X ? r3 : r4;
            return (t1.X < t2.X ? t1 : t2) * new V4(1f, 2f, 2f, 2f) * Cellular3Scale;
        }

        /// <summary>The noise of an expression operation of the three noises' five shapes each
        /// (over one, two and three coordinates, the curl over two and three), into the words at
        /// <paramref name="at"/>: the coordinate's words at <paramref name="coordinate"/>, the
        /// frequency, persistence and lacunarity at <paramref name="tuning"/>.</summary>
        internal static void Evaluate(VFXExpressionOperation op, uint[] w, int at, int coordinate, int tuning, int octaves)
        {
            int index = op - VFXExpressionOperation.ValueNoise1D;
            var kind = (Kind)(index / ShapeCount);
            float x = F(w[coordinate]), frequency = F(w[tuning]), persistence = F(w[tuning + 1]), lacunarity = F(w[tuning + 2]);
            switch (index % ShapeCount)
            {
                case 0:
                {
                    var n = Noise1D(kind, x, frequency, octaves, persistence, lacunarity);
                    (w[at], w[at + 1]) = (B(n.x), B(n.y));
                    break;
                }
                case 1:
                {
                    var n = Noise2D(kind, new Vector2(x, F(w[coordinate + 1])), frequency, octaves, persistence, lacunarity);
                    (w[at], w[at + 1], w[at + 2]) = (B(n.x), B(n.y), B(n.z));
                    break;
                }
                case 2:
                {
                    var n = Noise3D(kind, new Vector3(x, F(w[coordinate + 1]), F(w[coordinate + 2])), frequency, octaves, persistence, lacunarity);
                    (w[at], w[at + 1], w[at + 2], w[at + 3]) = (B(n.x), B(n.y), B(n.z), B(n.w));
                    break;
                }
                case 3:
                {
                    var n = Curl2D(kind, new Vector2(x, F(w[coordinate + 1])), frequency, octaves, persistence, lacunarity);
                    (w[at], w[at + 1]) = (B(n.x), B(n.y));
                    break;
                }
                default:
                {
                    var n = Curl3D(kind, new Vector3(x, F(w[coordinate + 1]), F(w[coordinate + 2])), frequency, octaves, persistence, lacunarity);
                    (w[at], w[at + 1], w[at + 2]) = (B(n.x), B(n.y), B(n.z));
                    break;
                }
            }
        }

        /// <summary>The shapes each noise has, in the operations' order.</summary>
        const int ShapeCount = 5;

        static float F(uint bits) => BitConverter.UInt32BitsToSingle(bits);
        static uint B(float value) => BitConverter.SingleToUInt32Bits(value);

        static V2 Sample1(Kind kind, float c) => kind switch { Kind.Value => Value1(c), Kind.Perlin => Perlin1(c), _ => Cellular1(c) };
        static V3 Sample2(Kind kind, V2 c) => kind switch { Kind.Value => Value2(c), Kind.Perlin => Perlin2(c), _ => Cellular2(c) };
        static V4 Sample3(Kind kind, V3 c) => kind switch { Kind.Value => Value3(c), Kind.Perlin => Perlin3(c), _ => Cellular3(c) };

        /// <summary>The octaves' sum over their amplitudes' sum: (value, derivative).</summary>
        internal static Vector2 Noise1D(Kind kind, float coordinate, float frequency, int octaves, float persistence, float lacunarity)
        {
            var total = V2.Zero;
            float amplitude = 1f, totalAmplitude = 0f;
            for (int octave = 0; octave < octaves; octave++)
            {
                total += Sample1(kind, coordinate * frequency) * amplitude;
                totalAmplitude += amplitude;
                amplitude *= persistence;
                frequency *= lacunarity;
            }
            total /= totalAmplitude;
            return new Vector2(total.X, total.Y);
        }

        internal static Vector3 Noise2D(Kind kind, Vector2 coordinate, float frequency, int octaves, float persistence, float lacunarity)
        {
            var total = V3.Zero;
            var c = new V2(coordinate.x, coordinate.y);
            float amplitude = 1f, totalAmplitude = 0f;
            for (int octave = 0; octave < octaves; octave++)
            {
                total += Sample2(kind, c * frequency) * amplitude;
                totalAmplitude += amplitude;
                amplitude *= persistence;
                frequency *= lacunarity;
            }
            total /= totalAmplitude;
            return new Vector3(total.X, total.Y, total.Z);
        }

        internal static Vector4 Noise3D(Kind kind, Vector3 coordinate, float frequency, int octaves, float persistence, float lacunarity)
        {
            var total = V4.Zero;
            var c = new V3(coordinate.x, coordinate.y, coordinate.z);
            float amplitude = 1f, totalAmplitude = 0f;
            for (int octave = 0; octave < octaves; octave++)
            {
                total += Sample3(kind, c * frequency) * amplitude;
                totalAmplitude += amplitude;
                amplitude *= persistence;
                frequency *= lacunarity;
            }
            total /= totalAmplitude;
            return new Vector4(total.X, total.Y, total.Z, total.W);
        }

        /// <summary>The curl of the noise over two coordinates: its gradient turned a quarter.</summary>
        internal static Vector2 Curl2D(Kind kind, Vector2 coordinate, float frequency, int octaves, float persistence, float lacunarity)
        {
            var total = V2.Zero;
            var c = new V2(coordinate.x, coordinate.y);
            float amplitude = 1f, totalAmplitude = 0f;
            for (int octave = 0; octave < octaves; octave++)
            {
                var sample = Sample2(kind, c * frequency);
                total += new V2(sample.Y, sample.Z) * amplitude;
                totalAmplitude += amplitude;
                amplitude *= persistence;
                frequency *= lacunarity;
            }
            return new Vector2(total.Y / totalAmplitude, -total.X / totalAmplitude);
        }

        /// <summary>The curl over three coordinates, from the gradients of the noise over three
        /// planes: (z, y), (x, z) and (y, x), the second and third moved apart.</summary>
        internal static Vector3 Curl3D(Kind kind, Vector3 coordinate, float frequency, int octaves, float persistence, float lacunarity)
        {
            V2 total0 = V2.Zero, total1 = V2.Zero, total2 = V2.Zero;
            var point0 = new V2(coordinate.z, coordinate.y);
            var point1 = new V2(coordinate.x + CurlPlane1, coordinate.z + CurlPlane1);
            var point2 = new V2(coordinate.y + CurlPlane2, coordinate.x + CurlPlane2);
            float amplitude = 1f, totalAmplitude = 0f;
            for (int octave = 0; octave < octaves; octave++)
            {
                var a = Sample2(kind, point0 * frequency);
                var b = Sample2(kind, point1 * frequency);
                var c = Sample2(kind, point2 * frequency);
                total0 += new V2(a.Y, a.Z) * amplitude;
                total1 += new V2(b.Y, b.Z) * amplitude;
                total2 += new V2(c.Y, c.Z) * amplitude;
                totalAmplitude += amplitude;
                amplitude *= persistence;
                frequency *= lacunarity;
            }
            return new Vector3((total2.X - total1.Y) / totalAmplitude, (total0.X - total2.Y) / totalAmplitude, (total1.X - total0.Y) / totalAmplitude);
        }
    }
}
