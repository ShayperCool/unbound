using System;
using Xunit;
namespace UnityEngine.Tests
{
    public class TransformParentingParityTests
    {
        static System.Runtime.InteropServices.Architecture Architecture => System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture;
        static bool MacArm64 => OperatingSystem.IsMacOS() && Architecture == System.Runtime.InteropServices.Architecture.Arm64;
        static bool LinuxX64 => OperatingSystem.IsLinux() && Architecture == System.Runtime.InteropServices.Architecture.X64;

        [Fact]
        public void SetRotationsNormalizeAsTheHostsEditor()
        {
            // tests/projects/solverpairorder --transform-inputs raw/6 and circle-scale/18/rotated: the
            // Linux x86_64 and macOS arm64 Editors give different last bits; each host matches its own
            // (the two measured hosts; others are unmeasured).
            if (!MacArm64 && !LinuxX64) return;
            var o = new GameObject("rotation");
            try
            {
                o.transform.localRotation = new Quaternion(.23f, -.17f, .31f, .9f);
                Assert.Equal(MacArm64 ? new Quaternion(.231404275f, -.171037942f, .311892718f, .9054949f)
                    : new Quaternion(.231404275f, -.171037927f, .311892718f, .905494869f), o.transform.localRotation, Exact);
                // Euler's half-angle sines and cosines from one sincos: on macOS separate sin and cos
                // give the Linux values here (y, z, w one bit off the macOS Editor's).
                o.transform.localRotation = Quaternion.Euler(30f, 16f, 38f);
                Assert.Equal(MacArm64 ? new Quaternion(.28610307f, .0436637141f, .277356058f, .916139841f)
                    : new Quaternion(.28610307f, .0436637066f, .2773561f, .9161398f), o.transform.localRotation, Exact);
            }
            finally { Object.DestroyImmediate(o); }
        }

        static readonly System.Collections.Generic.IEqualityComparer<Quaternion> Exact = new BitsComparer();

        sealed class BitsComparer : System.Collections.Generic.IEqualityComparer<Quaternion>
        {
            public bool Equals(Quaternion a, Quaternion b) => a.x.Equals(b.x) && a.y.Equals(b.y) && a.z.Equals(b.z) && a.w.Equals(b.w);
            public int GetHashCode(Quaternion q) => 0;
        }

        [Fact]
        public void TiltedLossyScaleKeepsTheMeasuredAccumulationOrder()
        {
            var o = new GameObject("scale");
            try
            {
                o.transform.localRotation = new Quaternion(.236167312f,.2592456f,.0951197743f,.931648552f);
                o.transform.localScale = new Vector3(.5f,-.8f,1.3f);
                Assert.Equal(new Vector3(.50000006f,-.8f,1.30000007f),o.transform.lossyScale);
            }
            finally { Object.DestroyImmediate(o); }
        }
        [Fact]
        public void DetachingRetainsTheCapturedScale()
        {
            var parent = new GameObject("parent"); var child = new GameObject("child");
            try
            {
                child.transform.rotation = Quaternion.Euler(0f,0f,12f);
                child.transform.localScale = new Vector3(.5f,-.8f,1.3f);
                child.transform.SetParent(parent.transform,false);
                parent.transform.localScale = new Vector3(1.17f,.73f,-.8f);
                child.transform.SetParent(null,true);
                Assert.Equal(new Vector3(.575489998f,-.599216044f,-1.04f),child.transform.localScale);
            }
            finally { Object.DestroyImmediate(child); Object.DestroyImmediate(parent); }
        }
        [Fact]
        public void DetachingNormalizesQuaternionComponentsInMeasuredOrder()
        {
            var parent = new GameObject("parent"); var child = new GameObject("child");
            try
            {
                var q = new Quaternion(.2488692105f,.1409349144f,-.9571540356f,.0453629345f);
                // This is the measured world rotation entering pose preservation,
                // after hierarchy composition, rather than a local setter input.
                child.transform.KeepWorldPose(Vector3.zero,q,Vector3.one,null);
                var actual = child.transform.localRotation;
                Assert.Equal(q.x,actual.x); Assert.Equal(q.y,actual.y);
                Assert.Equal(q.z,actual.z); Assert.Equal(q.w,actual.w);
            }
            finally { Object.DestroyImmediate(child); Object.DestroyImmediate(parent); }
        }
        [Fact]
        public void LocalSetterUsesTheMeasuredQuaternionNormalization()
        {
            var o = new GameObject("rotation");
            try
            {
                var q = new Quaternion(.2488692105f,.1409349144f,-.9571540356f,.0453629345f);
                o.transform.localRotation = q;
                var actual = o.transform.localRotation;
                Assert.Equal(q.x,actual.x); Assert.Equal(q.y,actual.y);
                Assert.Equal(q.z,actual.z); Assert.Equal(q.w,actual.w);
            }
            finally { Object.DestroyImmediate(o); }
        }
        [Fact]
        public void QuarterTurnInverseRetainsTheMeasuredRotationBasis()
        {
            var o = new GameObject("inverse");
            try
            {
                o.transform.rotation = Quaternion.Euler(0f,0f,90f);
                o.transform.localScale = new Vector3(1f,.8f,1f);
                var inverse = o.transform.worldToLocalMatrix;
                Assert.Equal(-1.1920929e-7f,inverse.m00); Assert.Equal(1.00000012f,inverse.m01);
                Assert.Equal(-1.25000012f,inverse.m10); Assert.Equal(-1.49011612e-7f,inverse.m11);
            }
            finally { Object.DestroyImmediate(o); }
        }
        [Fact]
        public void NonuniformParentUsesTheMeasuredScaleReciprocal()
        {
            var parent = new GameObject("parent"); var child = new GameObject("child");
            try
            {
                parent.transform.localScale = new Vector3(1.3f,.7f,1.1f);
                child.transform.rotation = Quaternion.Euler(0f,0f,12f);
                child.transform.localScale = new Vector3(.5f,-.8f,1.3f);
                child.transform.SetParent(parent.transform,true);
                Assert.Equal(new Vector3(.392445117f,-1.102024913f,1.181818128f),child.transform.localScale);
            }
            finally { Object.DestroyImmediate(child); Object.DestroyImmediate(parent); }
        }
        // tests/projects/solverpairorder --transform-inputs circle-planar/10/initial: the Linux
        // x86-64 and macOS arm64 Editors round three inverse scales differently.
        [Fact]
        public void TheInverseScaleFollowsTheCpusReciprocal()
        {
            var parent = new GameObject("parent"); var child = new GameObject("child");
            try
            {
                child.transform.position = new Vector3(.3f,-.2f,2f);
                child.transform.localScale = new Vector3(-.9f,1.7f,-1.1f);
                parent.transform.position = child.transform.position;
                child.transform.SetParent(parent.transform,true);
                var inverse = child.transform.worldToLocalMatrix;
                bool arm64 = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture == System.Runtime.InteropServices.Architecture.Arm64;
                Assert.Equal(-1.11111116f,inverse.m00);
                Assert.Equal(arm64 ? .5882353f : .588235259f,inverse.m11);
                Assert.Equal(arm64 ? -.909090936f : -.9090909f,inverse.m22);
                Assert.Equal(.333333373f,inverse.m03);
                Assert.Equal(arm64 ? .117647067f : .117647052f,inverse.m13);
                Assert.Equal(arm64 ? 1.81818187f : 1.81818175f,inverse.m23);
            }
            finally { Object.DestroyImmediate(child); Object.DestroyImmediate(parent); }
        }
        [Fact]
        public void NestedPositionRetainsTheMeasuredVectorArithmetic()
        {
            var parent = new GameObject("parent"); var child = new GameObject("child");
            try
            {
                parent.transform.position = new Vector3(2.3f,-1.7f,.9f);
                parent.transform.rotation = Quaternion.Euler(17f,-29f,13f);
                parent.transform.localScale = new Vector3(-.9f,1.7f,1.1f);
                child.transform.SetParent(parent.transform,false);
                child.transform.localPosition = new Vector3(.7f,-.4f,1.2f);
                Assert.Equal(new Vector3(1.398917437f,-2.855077744f,1.574941278f),child.transform.position);
            }
            finally { Object.DestroyImmediate(child); Object.DestroyImmediate(parent); }
        }
        [Fact]
        public void CollapsedTransformRetainsAnAffineInverse()
        {
            var o = new GameObject("collapsed");
            try
            {
                o.transform.localScale = Vector3.zero;
                var inverse = o.transform.worldToLocalMatrix;
                for (int i=0;i<16;i++) Assert.Equal(i==15 ? 1f : 0f,inverse[i]);
            }
            finally { Object.DestroyImmediate(o); }
        }
        [Theory]
        [InlineData(11f,23f,.5f,.4f,.5f,.4f)]
        [InlineData(0f,90f,1f,.8f,1f,.8000001f)]
        public void ReparentingMeasuresScaleInTheOriginalRotationFrame(float parentAngle, float childAngle,
            float sx, float sy, float expectedX, float expectedY)
        {
            var parent = new GameObject("parent"); var child = new GameObject("child");
            try
            {
                parent.transform.rotation = Quaternion.Euler(0f,0f,parentAngle);
                child.transform.rotation = Quaternion.Euler(0f,0f,childAngle);
                child.transform.localScale = new Vector3(sx,sy,1f);
                child.transform.SetParent(parent.transform,true);
                Assert.Equal(new Vector3(expectedX,expectedY,1f),child.transform.localScale);
            }
            finally { Object.DestroyImmediate(child); Object.DestroyImmediate(parent); }
        }
    }
}
