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
