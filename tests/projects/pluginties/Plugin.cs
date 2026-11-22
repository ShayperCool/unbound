using System.Reflection;

#if VERSION_TWO
[assembly: AssemblyVersion("2.0.0.0")]
#else
[assembly: AssemblyVersion("1.0.0.0")]
#endif

#if ARCHIVE_SECOND
namespace ProbeTieArchiveSecond
#else
namespace ProbeTie
#endif
{
    public static class Marker
    {
#if PACKAGE_API
        public static int PackageOnly() => 2;
#else
        public static int AssetsOnly() => 1;
#endif
    }
}
