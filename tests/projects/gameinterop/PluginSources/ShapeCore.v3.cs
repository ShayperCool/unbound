// A later version than the project's, with version 2's API: ShapeLater.dll is built against it.
[assembly: System.Reflection.AssemblyVersion("3.0.0.0")]

namespace ShapeCore
{
    public abstract class Shape
    {
        public abstract float Area(float scale);
        public virtual string Describe() => GetType().Name;
    }

    public static class CoreInfo
    {
        public static int Version() => 3;
    }
}
