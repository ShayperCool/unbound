using System.Reflection;
[assembly: AssemblyVersion("1.0.0.0")]
namespace ProbeAlias
{
    public static class Marker
    {
        public const string Built = "v1";
        public static string Runtime() => "v1";
    }
}
