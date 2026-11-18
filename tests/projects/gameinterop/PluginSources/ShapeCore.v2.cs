// The library version the project has: Shape.Area takes a scale, so a plugin type built against
// version 1 lacks an implementation of it.
[assembly: System.Reflection.AssemblyVersion("2.0.0.0")]

namespace ShapeCore
{
    public abstract class Shape
    {
        public abstract float Area(float scale);
        public virtual string Describe() => GetType().Name;
    }

    public static class CoreInfo
    {
        public static int Version() => 2;
    }
}
