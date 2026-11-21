using UnityEditor.UIElements;
using Xunit;

namespace UnityEngine.Tests
{
    public class UxmlNamespacePrefixAttributeTests
    {
        // The attribute carries the namespace and the prefix it was given (Scripting API
        // UxmlNamespacePrefixAttribute: ns, prefix); creating it reports nothing.
        [Fact]
        public void TheAttributeKeepsItsNamespaceAndPrefix()
        {
            var before = Unbound.Runtime.Stubs.HitReport().Count;
            var attribute = new UxmlNamespacePrefixAttribute("Example.Controls", "ex");
            Assert.Equal("Example.Controls", attribute.ns);
            Assert.Equal("ex", attribute.prefix);
            Assert.Equal(before, Unbound.Runtime.Stubs.HitReport().Count);
        }
    }
}
