// Built against ShapeCore 3.0.0.0, a later version than the project has, with the same API.
[assembly: System.Reflection.AssemblyVersion("1.0.0.0")]

namespace ShapeLater
{
    public sealed class Triangle : ShapeCore.Shape
    {
        public override float Area(float scale) => 0.5f * scale * scale;
    }

    public static class LaterInfo
    {
        public static int CoreVersion() => ShapeCore.CoreInfo.Version();
    }
}
