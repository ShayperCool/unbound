using Xunit;

namespace UnityEngine.Tests
{
    public class BuiltinFields : MonoBehaviour
    {
        public AnimationCurve curve;
        public Gradient gradient;
        public RectOffset offset;
    }

    public class SerializedCopyBuiltinTests
    {
        /// <summary>A new component's null curve stays null; its Instantiate copy has an empty one
        /// (6000.0.71f1, tests/projects/particlecurves fields/fresh and fields/freshclone), as after
        /// loading. Gradient and RectOffset are not measured here; they follow the same rule as
        /// when they are loaded.</summary>
        [Fact]
        public void InstantiateGivesNullBuiltinFieldsEmptyValues()
        {
            var go = new GameObject("builtins");
            GameObject copy = null;
            try
            {
                var fields = go.AddComponent<BuiltinFields>();
                Assert.Null(fields.curve);
                copy = Object.Instantiate(go);
                var cloned = copy.GetComponent<BuiltinFields>();
                Assert.NotNull(cloned.curve);
                Assert.Equal(0, cloned.curve.length);
                Assert.NotNull(cloned.gradient);
                Assert.NotNull(cloned.offset);
                Assert.Null(fields.curve);
            }
            finally
            {
                Object.DestroyImmediate(go);
                if (copy != null) Object.DestroyImmediate(copy);
            }
        }
    }
}
