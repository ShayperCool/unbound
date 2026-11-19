namespace Probe.AssetsTests
{
    /// <summary>References the embedded package's test assembly without using it: a missing
    /// reference is a warning, so this assembly builds either way.</summary>
    public static class AssetsTestHelpers
    {
        public static string Value() => "assets test assembly";
    }
}
