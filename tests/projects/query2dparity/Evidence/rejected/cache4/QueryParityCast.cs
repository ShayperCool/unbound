using System;
using System.Diagnostics;
using System.Numerics;
using Unbound.Box2D.Common;
using Unbound.Box2D.Collision;
using Unbound.Box2D.Collision.Shapes;

namespace Unbound.Runtime
{
    internal static class QueryParityCast
    {
        internal static bool Cast(out ShapeCastOutput output, in ShapeCastInput input, out SimplexCache cache)
        {
            var a = input.TransformA;
            var b = input.TransformB;
            float angleA = a.Rotation.Angle, angleB = b.Rotation.Angle;
            var toi = new ToiInput
            {
                ProxyA = input.ProxyA,
                ProxyB = input.ProxyB,
                SweepA = new Sweep { C0 = a.Position, C = a.Position, A0 = angleA, A = angleA },
                SweepB = new Sweep { C0 = b.Position, C = b.Position + input.TranslationB, A0 = angleB, A = angleB },
                Tmax = 1f,
            };
            ComputeTimeOfImpact(out var result, toi, out cache);
            output = new ShapeCastOutput { Lambda = result.Time };
            return result.State == ToiOutput.ToiState.Touching;
        }
        public static void ComputeTimeOfImpact(out ToiOutput output, in ToiInput input, out SimplexCache cache)
        {
            ToiProfile toiProfile = null;
            GJkProfile gjkProfile = null;
            var beginTime = 0L;
            output = new ToiOutput();

            if (toiProfile != null)
            {
                ++toiProfile.ToiCalls;
            }

            output.State = ToiOutput.ToiState.Unknown;
            output.Time = input.Tmax;

            ref readonly var proxyA = ref input.ProxyA;
            ref readonly var proxyB = ref input.ProxyB;

            var sweepA = input.SweepA;
            var sweepB = input.SweepB;

            // Large rotations can make the root finder fail, so we normalize the
            // sweep angles.
            sweepA.Normalize();
            sweepB.Normalize();

            var tMax = input.Tmax;

            var totalRadius = proxyA.Radius + proxyB.Radius;
            var target = Math.Max(Settings.LinearSlop, totalRadius - 3.0f * Settings.LinearSlop);
            var tolerance = 0.25f * Settings.LinearSlop;
            Debug.Assert(target > tolerance);

            var t1 = 0.0f;
            const int maxIterations = 20; // TODO_ERIN b2Settings
            var iter = 0;

            // Prepare input for distance query.
            cache = new SimplexCache();
            var distanceInput = new DistanceInput
            {
                ProxyA = input.ProxyA,
                ProxyB = input.ProxyB,
                UseRadii = false
            };

            // The outer loop progressively attempts to compute new separating axes.
            // This loop terminates when an axis is repeated (no progress is made).
            for (;;)
            {
                sweepA.GetTransform(out var xfA, t1);
                sweepB.GetTransform(out var xfB, t1);

                // Get the distance between shapes. We can also use the results
                // to get a separating axis.
                distanceInput.TransformA = xfA;
                distanceInput.TransformB = xfB;

                DistanceAlgorithm.Distance(out var distanceOutput, ref cache, distanceInput, gjkProfile);

                // If the shapes are overlapped, we give up on continuous collision.
                if (distanceOutput.Distance <= 0.0f)
                {
                    // Failure!
                    output.State = ToiOutput.ToiState.Overlapped;
                    output.Time = 0.0f;
                    break;
                }

                if (distanceOutput.Distance < target + tolerance)
                {
                    // Victory!
                    output.State = ToiOutput.ToiState.Touching;
                    output.Time = t1;
                    break;
                }

                // Initialize the separating axis.
                var fcn = new SeparationFunction();
                fcn.Initialize(ref cache, proxyA, sweepA, proxyB, sweepB, t1);

                // Compute the TOI on the separating axis. We do this by successively
                // resolving the deepest point. This loop is bounded by the number of vertices.
                var done = false;
                var t2 = tMax;
                var pushBackIter = 0;
                for (;;)
                {
                    // Find the deepest point at t2. Store the witness point indices.

                    var s2 = fcn.FindMinSeparation(out var indexA, out var indexB, t2);

                    // Is the final configuration separated?
                    if (s2 > target + tolerance)
                    {
                        // Victory!
                        output.State = ToiOutput.ToiState.Separated;
                        output.Time = tMax;
                        done = true;
                        break;
                    }

                    // Has the separation reached tolerance?
                    if (s2 > target - tolerance)
                    {
                        // Advance the sweeps
                        t1 = t2;
                        break;
                    }

                    // Compute the initial separation of the witness points.
                    var s1 = fcn.Evaluate(indexA, indexB, t1);

                    // Check for initial overlap. This might happen if the root finder
                    // runs out of iterations.
                    if (s1 < target - tolerance)
                    {
                        output.State = ToiOutput.ToiState.Failed;
                        output.Time = t1;
                        done = true;
                        break;
                    }

                    // Check for touching
                    if (s1 <= target + tolerance)
                    {
                        // Victory! t1 should hold the TOI (could be 0.0).
                        output.State = ToiOutput.ToiState.Touching;
                        output.Time = t1;
                        done = true;
                        break;
                    }

                    // Compute 1D root of: f(x) - target = 0
                    var rootIterCount = 0;
                    float a1 = t1, a2 = t2;
                    for (;;)
                    {
                        // Use a mix of the secant rule and bisection.
                        float t;
                        if ((rootIterCount & 1) != 0)
                        {
                            // Secant rule to improve convergence.
                            t = a1 + (target - s1) * (a2 - a1) / (s2 - s1);
                        }
                        else
                        {
                            // Bisection to guarantee progress.
                            t = 0.5f * (a1 + a2);
                        }

                        ++rootIterCount;
                        if (toiProfile != null)
                        {
                            ++toiProfile.ToiRootIters;
                        }

                        var s = fcn.Evaluate(indexA, indexB, t);

                        if (Math.Abs(s - target) < tolerance)
                        {
                            // t2 holds a tentative value for t1
                            t2 = t;
                            break;
                        }

                        // Ensure we continue to bracket the root.
                        if (s > target)
                        {
                            a1 = t;
                            s1 = s;
                        }
                        else
                        {
                            a2 = t;
                            s2 = s;
                        }

                        if (rootIterCount == 50)
                        {
                            break;
                        }
                    }

                    if (toiProfile != null)
                    {
                        toiProfile.ToiMaxRootIters = Math.Max(toiProfile.ToiMaxRootIters, rootIterCount);
                    }

                    ++pushBackIter;

                    if (pushBackIter == Settings.MaxPolygonVertices)
                    {
                        break;
                    }
                }

                ++iter;
                if (toiProfile != null)
                {
                    ++toiProfile.ToiIters;
                }

                if (done)
                {
                    break;
                }

                if (iter == maxIterations)
                {
                    // Root finder got stuck. Semi-victory.
                    output.State = ToiOutput.ToiState.Failed;
                    output.Time = t1;
                    break;
                }
            }

            if (toiProfile == null)
            {
                return;
            }

            var endTime = Stopwatch.GetTimestamp();
            var time = (endTime - beginTime) / 10000f;
            toiProfile.ToiMaxIters = Math.Max(toiProfile.ToiMaxIters, iter);
            toiProfile.ToiMaxTime = Math.Max(toiProfile.ToiMaxTime, time);
            toiProfile.ToiTime += time;
        }
    }
}
