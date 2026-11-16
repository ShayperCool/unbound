using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>How a bundle's asset names are looked up (tests/projects/bundlenames): the stored
    /// names compared exactly after backslashes read as '/' and, when the bundle's m_PathFlags say
    /// its names are lower case, the query lower-cased; file names only for a query without '/'.</summary>
    public class AssetBundleNameTests
    {
        static AssetBundle Bundle(int flags, params string[] names)
        {
            var bundle = new AssetBundle { m_PathFlags = flags };
            foreach (var name in names) bundle.m_Container.Add((name, 0, 0, 0));
            return bundle;
        }

        [Fact]
        public void CaseKeepingNamesMatchOnlyAsStored()
        {
            var bundle = Bundle(0, "Assets/Mixed/RoomsData.asset");
            Assert.True(bundle.Contains("Assets/Mixed/RoomsData.asset"));
            Assert.True(bundle.Contains("Assets\\Mixed\\RoomsData.asset"));
            Assert.False(bundle.Contains("assets/mixed/roomsdata.asset"));
            Assert.False(bundle.Contains("RoomsData"));
            Assert.False(bundle.Contains("RoomsData.asset"));
        }

        [Fact]
        public void LowerCaseNamesTakeAnyCaseAndFileNames()
        {
            var bundle = Bundle(7, "assets/bundled/data.asset");
            Assert.True(bundle.Contains("ASSETS/Bundled/Data.asset"));
            Assert.True(bundle.Contains("Data"));
            Assert.True(bundle.Contains("DATA.ASSET"));
            // A partial path is no file name.
            Assert.False(bundle.Contains("bundled/data.asset"));
            Assert.False(bundle.Contains("/data.asset"));
        }

        [Fact]
        public void FileNameLookupsFollowTheirFlags()
        {
            Assert.False(Bundle(6, "assets/a/data.asset").Contains("data"));
            Assert.True(Bundle(6, "assets/a/data.asset").Contains("data.asset"));
            Assert.True(Bundle(5, "assets/a/data.asset").Contains("data"));
            Assert.False(Bundle(5, "assets/a/data.asset").Contains("data.asset"));
            // Without lower case, file names compare as stored too (a control outside the probe).
            var mixed = Bundle(3, "Assets/A/RoomsData.asset");
            Assert.True(mixed.Contains("RoomsData"));
            Assert.False(mixed.Contains("roomsdata"));
        }

        [Fact]
        public void AnExactNameWinsOverFileNames()
        {
            // "data" is both a stored name and another entry's file name: only the stored one is found.
            var bundle = Bundle(7, "data", "assets/a/data.asset");
            Assert.Single(bundle.Lookup("data"));
            Assert.Empty(bundle.Lookup("x"));
        }

        [Fact]
        public void ABundleWithoutTheFieldTakesEveryLookup()
        {
            var bundle = new AssetBundle();
            bundle.m_Container.Add(("assets/a/data.asset", 0, 0, 0));
            Assert.True(bundle.Contains("Data"));
            Assert.True(bundle.Contains("Assets/A/Data.asset"));
        }

        [Fact]
        public void OnlyAsciiLettersAreLowerCased()
        {
            // As measured for a stored "assets/bundled/Ünïcode.txt".
            Assert.True(Bundle(7, "assets/a/Über.asset").Contains("ASSETS/A/Über.asset"));
            Assert.False(Bundle(7, "assets/a/über.asset").Contains("assets/a/Über.asset"));
        }
    }
}
