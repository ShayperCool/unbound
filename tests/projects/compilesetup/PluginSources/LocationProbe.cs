using System.Reflection;
[assembly: AssemblyVersion("1.0.0.0")]
namespace LocationProbe
{
    /// <summary>A plugin that finds its own file, as editor tooling finds its folder.</summary>
    public static class Marker
    {
        public static string Location() => typeof(Marker).Assembly.Location;
#pragma warning disable SYSLIB0012
        public static string CodeBase() => typeof(Marker).Assembly.CodeBase;
#pragma warning restore SYSLIB0012
    }
}
