using System.Reflection;
[assembly: AssemblyVersion("1.0.0.0")]
namespace ProbeOrder
{
    public static class Marker
    {
        public const string Built = "Order2";
        public static string Runtime() => "Order2";
    }
}
