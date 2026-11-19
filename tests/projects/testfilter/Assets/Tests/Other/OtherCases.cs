using NUnit.Framework;

// A second test assembly: a fixture of the same class name as one in ProbeTests, and a category
// shared with it.
namespace Other
{
    public class Alpha
    {
        [Test] public void One() { }
    }

    public class Gamma
    {
        [Test, Category("Slow")] public void One() { }
    }
}
