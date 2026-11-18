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

namespace ShapeCore
{
    /// <summary>Sides now takes a scale and Edges is new: a figure built against version 1
    /// implements neither.</summary>
    public abstract class Figure
    {
        public virtual int Id() => 1;
        public abstract int Sides(int scale);
        public abstract int Edges();
    }

    public interface IMeasured
    {
        int Measure(int scale);
    }

    /// <summary>Apply now takes a time as well.</summary>
    public abstract class Plug<T>
    {
        public abstract T Apply(T value, float time);
    }
}
