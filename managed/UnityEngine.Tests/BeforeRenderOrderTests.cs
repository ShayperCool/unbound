using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using UnityEngine.Events;
using Xunit;

// Application.onBeforeRender order and UnityString (Unity 6000.0.71f1, tests/projects/coreremainder).
// Controls outside the probe: an override takes its base method's order, an order's last callback
// removed and added again keeps the blocks sorted, a throwing callback ends the run, and UnityString
// formats with the invariant culture whatever the current one.

namespace UnityEngine.Tests
{
    [Collection("Before render")]
    public class BeforeRenderOrderTests
    {
        static readonly List<string> s_Calls = new List<string>();

        class Base
        {
            [BeforeRenderOrder(-3)] public virtual void Run() => s_Calls.Add("base");
        }

        sealed class Derived : Base
        {
            public override void Run() => s_Calls.Add("derived");
        }

        [BeforeRenderOrder(5)] static void Five() => s_Calls.Add("5");
        [BeforeRenderOrder(5)] static void FiveB() => s_Calls.Add("5b");
        [BeforeRenderOrder(9)] static void Nine() => s_Calls.Add("9");
        [BeforeRenderOrder(2)] static void Throws()
        {
            s_Calls.Add("throws");
            throw new InvalidOperationException("x");
        }
        static void Zero() => s_Calls.Add("0");

        static string Run()
        {
            s_Calls.Clear();
            try { BeforeRenderHelper.Invoke(); }
            catch (InvalidOperationException) { s_Calls.Add("!"); }
            return string.Join(" ", s_Calls);
        }

        [Fact]
        public void BlocksStaySortedThroughRemovalsAndOverridesTakeTheBaseOrder()
        {
            UnityAction derived = new Derived().Run;
            Application.onBeforeRender += Nine;
            Application.onBeforeRender += Five;
            Application.onBeforeRender += Zero;
            Application.onBeforeRender += derived;
            try
            {
                Assert.Equal("derived 0 5 9", Run());
                Application.onBeforeRender -= Five;
                Assert.Equal("derived 0 9", Run());
                Application.onBeforeRender += FiveB;
                Application.onBeforeRender += Five;
                Assert.Equal("derived 0 5b 5 9", Run());
                Application.onBeforeRender += Throws;
                Assert.Equal("derived 0 throws !", Run());
                Application.onBeforeRender -= Throws;
            }
            finally
            {
                Application.onBeforeRender -= Nine;
                Application.onBeforeRender -= Five;
                Application.onBeforeRender -= FiveB;
                Application.onBeforeRender -= Zero;
                Application.onBeforeRender -= derived;
            }
            Assert.Equal("", Run());
        }

        [Fact]
        public void UnityStringFormatsWithTheInvariantCulture()
        {
            var culture = Thread.CurrentThread.CurrentCulture;
            Thread.CurrentThread.CurrentCulture = new CultureInfo("de-DE");
            try
            {
                Assert.Equal("1.5 1,234.50 1850", UnityString.Format("{0} {1:N2} {2}", 1.5f, 1234.5, 1849.9999f));
            }
            finally
            {
                Thread.CurrentThread.CurrentCulture = culture;
            }
        }
    }
}
