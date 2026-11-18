// Built against ShapeCore 1.0.0.0: Circle implements the Area() of that version only.
[assembly: System.Reflection.AssemblyVersion("1.0.0.0")]

namespace ShapeExtras
{
    public sealed class Circle : ShapeCore.Shape
    {
        public float radius = 1f;
        public override float Area() => 3f * radius * radius;
    }

    public class Square
    {
        public float side = 2f;
        public float Area() => side * side;
    }

    public static class ExtrasInfo
    {
        public static int Answer() => 42;
        public static string CoreName() => typeof(ShapeCore.Shape).Assembly.GetName().Name;
    }
}
