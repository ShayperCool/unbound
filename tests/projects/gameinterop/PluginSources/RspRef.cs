// A plugin with Auto Reference off: scripts see it only through the response file's -r option.
[assembly: System.Reflection.AssemblyVersion("1.0.0.0")]

namespace RspRef
{
    public static class RspRefInfo
    {
        public static int Value() => 77;
    }
}
