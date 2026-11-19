using System.Reflection;
[assembly: AssemblyVersion("1.0.0.0")]
namespace ProbeSame
{
    public static class Marker
    {
        public const string Built = "Package";
        public static string Runtime() => "Package";
    }
}
