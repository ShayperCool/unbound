using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Unbound.Runtime;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.UI;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>
    /// PackageManager.UI.Sample's answers for an installed package's samples, as the Unity
    /// 6000.0.71f1 Editor gives them (tests/projects/packagesamples find.*).
    /// </summary>
    [Collection("EditorState")]
    public sealed class PackageSamplesTests : IDisposable
    {
        readonly string m_Root = Path.Combine(Path.GetTempPath(), "unbound-samples-" + System.Guid.NewGuid().ToString("N"));
        readonly string m_AssetsPath = ProjectInfo.AssetsPath;
        readonly List<PackageInfo> m_Packages = new List<PackageInfo>(PackageInfo.Registered);

        static string Probe([System.Runtime.CompilerServices.CallerFilePath] string here = "") =>
            Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here)!, "..", "..", "tests", "projects", "packagesamples"));

        public PackageSamplesTests()
        {
            var package = Path.Combine(m_Root, "Packages", "com.probe.samples");
            Directory.CreateDirectory(package);
            File.Copy(Path.Combine(Probe(), "Packages", "com.probe.samples", "package.json"), Path.Combine(package, "package.json"));
            Directory.CreateDirectory(Path.Combine(m_Root, "Assets"));
            ProjectInfo.AssetsPath = Path.Combine(m_Root, "Assets");
            PackageInfo.Register(new[]
            {
                PackageInfo.Create("com.probe.samples", "1.2.3", PackageSource.Embedded, package, "Probe: Samples", null, null, null, null, true),
                PackageInfo.Create("com.probe.plain", "0.1.0", PackageSource.Embedded, Path.Combine(m_Root, "Packages", "com.probe.plain"), "Probe Plain", null, null, null, null, true),
            });
        }

        public void Dispose()
        {
            PackageInfo.Register(m_Packages);
            ProjectInfo.AssetsPath = m_AssetsPath;
            try { Directory.Delete(m_Root, true); } catch (IOException) { }
        }

        string D(Sample s) => ("displayName \"" + s.displayName + "\" description \"" + s.description + "\" resolvedPath \"" + s.resolvedPath
            + "\" importPath \"" + s.importPath + "\" interactiveImport " + s.interactiveImport + " isImported " + s.isImported)
            .Replace(Path.Combine(m_Root, "Packages", "com.probe.samples"), "<package>").Replace(m_Root, "<project>");

        string Find(string name, string version)
        {
            var list = Sample.FindByPackage(name, version).ToList();
            return "count " + list.Count + string.Concat(list.Select(s => "\n" + D(s)));
        }

        static string Reference(string key)
        {
            foreach (var line in File.ReadLines(Path.Combine(Probe(), "Reference", "unity-6000.0.71f1", "values.jsonl")))
                if (line.StartsWith("{\"k\":\"" + key + "\",", StringComparison.Ordinal))
                    return System.Text.Json.JsonDocument.Parse(line).RootElement.GetProperty("v").GetString();
            throw new InvalidOperationException("no reference row " + key);
        }

        [Theory]
        [InlineData("version", "com.probe.samples", "1.2.3")]
        [InlineData("empty", "com.probe.samples", "")]
        [InlineData("null", "com.probe.samples", null)]
        [InlineData("other", "com.probe.samples", "1.0.0")]
        [InlineData("plain", "com.probe.plain", "")]
        [InlineData("missing", "com.probe.missing", "")]
        [InlineData("nullName", null, "")]
        public void APackagesSamplesAreFoundAsTheEditorFindsThem(string key, string name, string version) =>
            Assert.Equal(Reference("find/" + key), Find(name, version));

        /// <summary>A sample's path is joined as the Package Manager joins paths: a trailing slash,
        /// "." and doubled separators left out, backslashes read as slashes.</summary>
        [Theory]
        [InlineData("Samples~/Basic/")]
        [InlineData("./Samples~/Basic")]
        [InlineData("Samples~//Basic")]
        [InlineData("Samples~\\Basic")]
        [InlineData("Samples~/Other/../Basic")]
        public void ASamplesPathIsJoinedWellFormed(string path)
        {
            var json = Path.Combine(m_Root, "Packages", "com.probe.samples", "package.json");
            File.WriteAllText(json, "{\"name\":\"com.probe.samples\",\"version\":\"1.2.3\",\"displayName\":\"Probe: Samples\",\"samples\":[3,{\"displayName\":\"Basic Sample\",\"path\":"
                + System.Text.Json.JsonSerializer.Serialize(path) + "}]}");
            var sample = Assert.Single(Sample.FindByPackage("com.probe.samples", ""));
            Assert.Equal(Path.Combine(m_Root, "Packages", "com.probe.samples") + "/Samples~/Basic", sample.resolvedPath);
        }

        [Fact]
        public void ASampleIsImportedOnceItsImportFolderExists()
        {
            var basic = Sample.FindByPackage("com.probe.samples", "").First();
            Assert.False(basic.isImported);
            Directory.CreateDirectory(basic.importPath);
            Assert.True(basic.isImported);
        }
    }
}
