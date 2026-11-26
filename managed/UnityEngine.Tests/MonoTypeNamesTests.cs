using System;
using System.IO;
using System.Reflection;
using System.Runtime.Loader;
using Unbound;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>Mono's type names game code reads (tests/projects/typenames); these need the
    /// release's Mono class library from the Hub installation and pass trivially without it.</summary>
    public class MonoTypeNamesTests : IDisposable
    {
        readonly string m_FrameworkDir = Unbound.Runtime.GameDomain.FrameworkDir;

        public void Dispose()
        {
            Unbound.Runtime.GameDomain.FrameworkDir = m_FrameworkDir;
            MonoTypeNames.Reset();
        }

        static bool Installed()
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "Unity/Hub/Editor/6000.0.71f1/Editor/Data/MonoBleedingEdge/lib/mono/4.5");
            if (!File.Exists(Path.Combine(dir, "mscorlib.dll"))) return false;
            Unbound.Runtime.GameDomain.FrameworkDir = dir;
            MonoTypeNames.Reset();
            return true;
        }

        const string Mono = ", Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089";

        /// <summary>The class library's types take Mono's assembly names (typenames *.aqn).</summary>
        [Fact]
        public void ClassLibraryTypesTakeMonosNames()
        {
            if (!Installed()) return;
            Assert.Equal("System.Uri, System" + Mono, MonoTypeNames.get_AssemblyQualifiedName(typeof(Uri)));
            Assert.Equal("System.Collections.Generic.List`1[[System.Int32, mscorlib" + Mono + "]], mscorlib" + Mono,
                MonoTypeNames.get_AssemblyQualifiedName(typeof(System.Collections.Generic.List<int>)));
            Assert.Equal("mscorlib" + Mono, MonoTypeNames.get_FullName(typeof(object).Assembly));
            Assert.Equal("b77a5c561934e089", Convert.ToHexString(MonoTypeNames.GetName(typeof(object).Assembly).GetPublicKeyToken()).ToLowerInvariant());
        }

        /// <summary>A game plugin of a framework name (a project's System.Private.Uri.dll, here a copy
        /// of .NET's loaded into a context of its own) keeps its own names, though Mono's library
        /// defines types of the same names.</summary>
        [Fact]
        public void PluginsOfFrameworkNamesKeepTheirNames()
        {
            if (!Installed()) return;
            var dir = Directory.CreateTempSubdirectory("unbound-plugin-");
            try
            {
                var copy = Path.Combine(dir.FullName, Path.GetFileName(typeof(Uri).Assembly.Location));
                File.Copy(typeof(Uri).Assembly.Location, copy);
                var context = new AssemblyLoadContext("plugin", isCollectible: true);
                var uri = context.LoadFromAssemblyPath(copy).GetType("System.Uri", true);
                Assert.NotSame(typeof(Uri), uri);
                Assert.Equal(uri.AssemblyQualifiedName, MonoTypeNames.get_AssemblyQualifiedName(uri));
                Assert.Equal(uri.Assembly.FullName, MonoTypeNames.get_FullName(uri.Assembly));
                context.Unload();
            }
            finally
            {
                try { dir.Delete(true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            }
        }

        [Fact]
        public void NullReceiversThrowAsTheMembersDo()
        {
            Assert.Throws<NullReferenceException>(() => MonoTypeNames.get_AssemblyQualifiedName(null));
            Assert.Throws<NullReferenceException>(() => MonoTypeNames.get_FullName((Type)null));
            Assert.Throws<NullReferenceException>(() => MonoTypeNames.get_FullName((Assembly)null));
            Assert.Throws<NullReferenceException>(() => MonoTypeNames.GetName(null));
        }
    }
}
