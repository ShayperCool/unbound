// The library a plugin was built against: ShapeExtras.dll and ShapeLater.dll reference this
// version's assembly identity. The project carries ShapeCore.v2.cs (Assets/Plugins/ShapeCore.dll).
[assembly: System.Reflection.AssemblyVersion("1.0.0.0")]

namespace ShapeCore
{
    public abstract class Shape
    {
        public abstract float Area();
        public virtual string Describe() => GetType().Name;
    }
}

namespace ShapeCore
{
    /// <summary>A base with a virtual member before its abstract one.</summary>
    public abstract class Figure
    {
        public virtual int Id() => 1;
        public abstract int Sides();
    }

    public interface IMeasured
    {
        int Measure();
    }

    public abstract class Plug<T>
    {
        public abstract T Apply(T value);
    }
}
