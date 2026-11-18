// Built against ShapeCore 1.0.0.0: Circle implements the Area() of that version only.
[assembly: System.Reflection.AssemblyVersion("1.0.0.0")]

namespace ShapeExtras
{
    public sealed class Circle : ShapeCore.Shape
    {
        public float radius = 1f;
        public override float Area() => 3f * radius * radius;
        public static int Count() => 1;
    }

    /// <summary>Implements version 1's Figure.Sides() only.</summary>
    public class Pentagon : ShapeCore.Figure
    {
        public override int Sides() => 5;
    }

    /// <summary>Implements version 1's IMeasured.Measure() only.</summary>
    public class Ruler : ShapeCore.IMeasured
    {
        public int Measure() => 30;
    }

    /// <summary>Implements version 1's Plug<float>.Apply(float) only.</summary>
    public class FloatPlug : ShapeCore.Plug<float>
    {
        public override float Apply(float value) => value * 2f;
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
