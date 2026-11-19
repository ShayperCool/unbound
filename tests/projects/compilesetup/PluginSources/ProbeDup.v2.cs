using System.Reflection;
[assembly: AssemblyVersion("2.0.0.0")]
namespace ProbeDup
{
    public static class Marker
    {
        public const string Built = "v2";
        public static string Runtime() => "v2";
    }
}
