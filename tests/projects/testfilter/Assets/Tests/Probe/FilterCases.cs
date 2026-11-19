using NUnit.Framework;

// The tests the filter cases of run.py select from. Every test passes; what a case records is
// which of them ran.
namespace Probe
{
    public class Alpha
    {
        [Test] public void One() { }
        [Test] public void Two() { }
    }

    public class AlphaExtra
    {
        [Test] public void One() { }
    }

    public class Beta
    {
        [Test, Category("Slow")] public void One() { }
        [Test] public void Two() { }
        [TestCase(1)]
        [TestCase(2)]
        public void Sum(int n) { Assert.That(n, Is.GreaterThan(0)); }
        [Test, Explicit] public void Hidden() { }
    }
}
