using System.Globalization;
using UnityEngine;

namespace Unbound.Runtime
{
    internal static class PhysicsMaterialValues
    {
        // Scalar limits recorded by tests/projects/assetadapters on Unity 6000.0.71f1.
        internal const float MaxFriction3D = 3.4028233e38f;
        const float MaxValue2D = 100000f;

        internal static bool Set3D(PhysicsMaterial owner, string property, float value, float maximum, ref float current)
        {
            if (Compat.PhysicsMaterialValuesMeasured)
            {
                if (value < 0f || value > maximum)
                {
                    string text = float.IsPositiveInfinity(value) ? "inf" : float.IsNegativeInfinity(value) ? "-inf"
                        : ((double)value).ToString("F6", CultureInfo.InvariantCulture);
                    Debug.LogError($"Physics material {owner.name} cannot have {property} = {text}", owner);
                    value = value < 0f ? 0f : maximum;
                }
                if (current == value) return false;
            }
            current = value;
            return true;
        }

        internal static void Set2D(PhysicsMaterial2D owner, string property, float value, ref float current)
        {
            if (Compat.PhysicsMaterialValuesMeasured)
            {
                if (float.IsNaN(value) || float.IsInfinity(value))
                {
                    Debug.LogError($"PhysicsMaterial2D.{property} assign attempt for '{owner.name}' is not valid. Input {property} is {{ {value.ToString(CultureInfo.InvariantCulture)} }}.", owner);
                    return;
                }
                value = value < 0f ? 0f : value > MaxValue2D ? MaxValue2D : value;
            }
            current = value;
        }
    }
}
