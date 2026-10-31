using System;
using Unbound.Box2D.Collision;
using Unbound.Box2D.Collision.Shapes;

namespace Unbound.Runtime
{
    internal static class QueryParityCast
    {
        internal static bool Cast(out ShapeCastOutput output, in ShapeCastInput input)
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
            TimeOfImpact.ComputeTimeOfImpact(out var result, toi);
            output = new ShapeCastOutput { Lambda = result.Time };
            return result.State == ToiOutput.ToiState.Touching;
        }
    }
}
