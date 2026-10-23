using System.Linq;
using UnityEditor;
using Xunit;

namespace UnityEngine.Tests
{
    public class EditorServicesTests
    {
        [Fact]
        public void TypeCacheFindsDerivedTypes()
        {
            var behaviours = TypeCache.GetTypesDerivedFrom<Behaviour>();
            Assert.Contains(typeof(Camera), behaviours);
            Assert.DoesNotContain(typeof(Behaviour), behaviours);
            // Enumerable as Unity's collection (the foreach pattern packages use).
            Assert.Equal(behaviours.Count, behaviours.Cast<System.Type>().Count());
            Assert.True(behaviours.Count > 0 && behaviours.IndexOf(typeof(Camera)) >= 0);
        }

        [Fact]
        public void SessionStateKeepsValues()
        {
            SessionState.SetInt("unbound.test", 3);
            Assert.Equal(3, SessionState.GetInt("unbound.test", 0));
            SessionState.EraseInt("unbound.test");
            Assert.Equal(7, SessionState.GetInt("unbound.test", 7));
        }
    }
}
