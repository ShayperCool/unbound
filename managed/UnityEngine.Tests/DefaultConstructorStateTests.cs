using System;
using JetBrains.Annotations;
using Unbound.Runtime;
using UnityEngine.Categorization;
using Xunit;

namespace UnityEngine.Tests
{
    [RequireComponent(typeof(Behaviour))]
    public sealed class DefaultStateBehaviour : MonoBehaviour { }

    public sealed class DefaultConstructorStateTests : IDisposable
    {
        readonly string release = ProjectInfo.UnityVersion;
        public DefaultConstructorStateTests() { ProjectInfo.UnityVersion = "6000.0.71f1"; }
        public void Dispose() { ProjectInfo.UnityVersion = release; }

        [Fact]
        public void ConstructorDefaultsAndExplicitArgumentsRemainIndependent()
        {
            Assert.True(new LocalizationRequiredAttribute().Required);
            Assert.False(new LocalizationRequiredAttribute(false).Required);
            Assert.True(new MustDisposeResourceAttribute().Value);
            Assert.False(new MustDisposeResourceAttribute(false).Value);
            Assert.Equal((ImplicitUseKindFlags.Default, ImplicitUseTargetFlags.Default),
                (new MeansImplicitUseAttribute().UseKindFlags, new MeansImplicitUseAttribute().TargetFlags));
            Assert.Equal((ImplicitUseKindFlags.Default, ImplicitUseTargetFlags.Default),
                (new UsedImplicitlyAttribute().UseKindFlags, new UsedImplicitlyAttribute().TargetFlags));
            Assert.Equal(ImplicitUseTargetFlags.Default, new MeansImplicitUseAttribute((ImplicitUseKindFlags)2).TargetFlags);
            Assert.Equal(ImplicitUseKindFlags.Default, new MeansImplicitUseAttribute((ImplicitUseTargetFlags)2).UseKindFlags);
            Assert.Equal(ImplicitUseTargetFlags.Default, new UsedImplicitlyAttribute((ImplicitUseKindFlags)2).TargetFlags);
            Assert.Equal(ImplicitUseKindFlags.Default, new UsedImplicitlyAttribute((ImplicitUseTargetFlags)2).UseKindFlags);
            Assert.Equal((default(ImplicitUseKindFlags), default(ImplicitUseTargetFlags)),
                (new UsedImplicitlyAttribute(0, 0).UseKindFlags, new UsedImplicitlyAttribute(0, 0).TargetFlags));
            Assert.Equal(int.MaxValue, new CategoryInfoAttribute().Order);
            Assert.Equal(int.MaxValue, new ElementInfoAttribute().Order);
            Assert.Equal(-27, new CategoryInfoAttribute { Order = -27 }.Order);
            Assert.Equal(14, new ElementInfoAttribute { Order = 14 }.Order);
            Assert.True(new SpaceAttribute().applyToCollection);
            Assert.True(new SpaceAttribute(23).applyToCollection);
            Assert.Equal(23f, new SpaceAttribute(23).height);
        }

        [Fact]
        public void ExceptionConstructorsRetainTheirMessageAndInnerException()
        {
            foreach (var type in new[] { typeof(UnityException), typeof(MissingReferenceException),
                typeof(MissingComponentException), typeof(UnassignedReferenceException) })
            {
                var empty = (Exception)Activator.CreateInstance(type);
                var message = (Exception)Activator.CreateInstance(type, new object[] { "changed" });
                var inner = new InvalidOperationException("inner");
                var wrapped = (Exception)Activator.CreateInstance(type, new object[] { "changed", inner });
                Assert.Equal(-2147467261, empty.HResult);
                Assert.Equal(-2147467261, message.HResult);
                Assert.Equal(-2147467261, wrapped.HResult);
                Assert.Equal("changed", message.Message);
                Assert.Equal("changed", wrapped.Message);
                Assert.Same(inner, wrapped.InnerException);
            }
        }

        [Fact]
        public void RejectedBaseTypesDoNotAttachToAPopulatedInactiveObject()
        {
            var owner = new GameObject("factory control");
            try
            {
                owner.SetActive(false);
                Assert.NotNull(owner.AddComponent<DefaultStateBehaviour>());
                Assert.NotNull(owner.AddComponent<MeshRenderer>());
                foreach (var type in new[] { typeof(Component), typeof(Behaviour), typeof(MonoBehaviour), typeof(Renderer) })
                    Assert.Null(owner.AddComponent(type));
                Assert.Equal(3, owner.GetComponents<Component>().Length);
            }
            finally { Object.DestroyImmediate(owner); }
        }

        [Fact]
        public void RationalRatesKeepInvalidStateAndExactOrdering()
        {
            var invalid = default(RefreshRate);
            var infinite = new RefreshRate { numerator = 1, denominator = 0 };
            var finite = new RefreshRate { numerator = 60000, denominator = 1001 };
            Assert.True(double.IsNaN(invalid.value));
            Assert.Equal("59.9400599400599", finite.ToString());
            Assert.True(double.IsPositiveInfinity(infinite.value));
            Assert.True(invalid.Equals(infinite));
            Assert.Equal(0, invalid.CompareTo(infinite));
            Assert.Equal(1, invalid.CompareTo(finite));
            Assert.Equal(-1, finite.CompareTo(invalid));
            var a = new RefreshRate { numerator = uint.MaxValue, denominator = uint.MaxValue - 1 };
            var b = new RefreshRate { numerator = uint.MaxValue - 1, denominator = uint.MaxValue - 2 };
            Assert.Equal(a.value, b.value);
            Assert.False(a.Equals(b));
            Assert.Equal(-1, a.CompareTo(b));
            Assert.True(new RefreshRate { numerator = 2, denominator = 1 }.Equals(new RefreshRate { numerator = 4, denominator = 2 }));
        }

        [Fact]
        public void IntegerRatesHandleInvalidValuesRoundingAndOverflow()
        {
            foreach (var sample in new[] { (0u, 0u, int.MinValue), (1u, 0u, int.MinValue),
                (60000u, 1001u, 60), (5u, 2u, 2), (3u, 2u, 2), (uint.MaxValue, 1u, int.MinValue), ((uint)int.MaxValue, 1u, int.MaxValue) })
            {
                var rate = new RefreshRate { numerator = sample.Item1, denominator = sample.Item2 };
                Assert.Equal(sample.Item3, new Resolution { refreshRateRatio = rate }.refreshRate);
            }
            var negative = new Resolution { refreshRate = -1 };
            Assert.Equal(uint.MaxValue, negative.refreshRateRatio.numerator);
            Assert.Equal(int.MinValue, negative.refreshRate);
        }

        [Fact]
        public void CameraAliasesReadAndWriteTheModernPropertyState()
        {
            var owner = new GameObject("camera aliases");
            try
            {
                owner.SetActive(false);
                var camera = owner.AddComponent<Camera>();
                foreach (var pair in new object[][] {
                    new object[] { "far", "farClipPlane", 73f, 115f }, new object[] { "near", "nearClipPlane", 2f, 3f },
                    new object[] { "fov", "fieldOfView", 43f, 47f }, new object[] { "hdr", "allowHDR", false, true } })
                {
                    var alias = typeof(Camera).GetProperty((string)pair[0]);
                    var modern = typeof(Camera).GetProperty((string)pair[1]);
                    alias.SetValue(camera, pair[2]);
                    Assert.Equal(pair[2], modern.GetValue(camera));
                    modern.SetValue(camera, pair[3]);
                    Assert.Equal(pair[3], alias.GetValue(camera));
                }
            }
            finally { Object.DestroyImmediate(owner); }
        }

        [Fact]
        public void RemovedUvAliasKeepsTheSecondChannelUnchanged()
        {
            var mesh = new Mesh { vertices = new[] { Vector3.zero, Vector3.one } };
            try
            {
                var channel = new[] { new Vector2(4, 5), new Vector2(6, 7) };
                mesh.uv2 = channel;
                var alias = typeof(Mesh).GetProperty("uv1");
                alias.SetValue(mesh, new[] { Vector2.zero, Vector2.one });
                Assert.Null(alias.GetValue(mesh));
                Assert.Equal(channel, mesh.uv2);
            }
            finally { Object.DestroyImmediate(mesh); }
        }

        [Theory]
        [InlineData("6000.0.71f1", true)]
        [InlineData("6000.0.72f1", false)]
        [InlineData("6000.1.0f1", false)]
        [InlineData("2022.3.0f1", false)]
        public void ProfilesSelectOnlyTheMeasuredRelease(string version, bool measured)
        {
            ProjectInfo.UnityVersion = version;
            Assert.Equal(measured, Compat.DefaultConstructorStateMeasured);
            Assert.Equal(measured, Compat.RefreshRateFractionsMeasured);
            Assert.Equal(measured, Compat.ComponentFactoryEligibilityMeasured);
            Assert.Equal(measured, Compat.NativeComponentIsAbstract(typeof(Behaviour)));
            Assert.Equal(measured, Compat.NativeComponentIsAbstract(typeof(Renderer)));
            Assert.False(Compat.NativeComponentIsAbstract(typeof(DefaultStateBehaviour)));
            Assert.False(Compat.NativeComponentIsAbstract(typeof(MeshRenderer)));
        }
    }
}
