namespace UnityEngine.Rendering
{
    /// <summary>Where a render pipeline resource field is found: by project path, in the built-in
    /// resources or by shader name. Read by the pipelines' resource loaders.</summary>
    public enum SearchType
    {
        ProjectPath = 0,
        BuiltinPath = 1,
        BuiltinExtraPath = 2,
        ShaderName = 3,
    }

    [System.AttributeUsage(System.AttributeTargets.Field, Inherited = true)]
    public abstract partial class ResourcePathsBaseAttribute : System.Attribute
    {
        protected ResourcePathsBaseAttribute(string[] paths, bool isField, SearchType location)
        {
            this.paths = paths;
            this.isField = isField;
            this.location = location;
        }

        public SearchType location { get; }
        public string[] paths { get; }
        public bool isField { get; }
    }

    public sealed partial class ResourcePathAttribute : ResourcePathsBaseAttribute
    {
        public ResourcePathAttribute(string path, SearchType location = SearchType.ProjectPath)
            : base(new[] { path }, true, location) { }
    }

    public sealed partial class ResourcePathsAttribute : ResourcePathsBaseAttribute
    {
        public ResourcePathsAttribute(string[] paths, SearchType location = SearchType.ProjectPath)
            : base(paths, false, location) { }
    }

    /// <summary>Paths made from a format and an index range (<c>pathFormat</c> with
    /// <c>rangeMin</c>..<c>rangeMax</c> - 1), as Unity's attribute builds them.</summary>
    public sealed partial class ResourceFormattedPathsAttribute : ResourcePathsBaseAttribute
    {
        public ResourceFormattedPathsAttribute(string pathFormat, int rangeMin, int rangeMax, SearchType location = SearchType.ProjectPath)
            : base(FormattedPaths(pathFormat, rangeMin, rangeMax), false, location) { }

        static string[] FormattedPaths(string format, int min, int max)
        {
            var paths = new string[System.Math.Max(0, max - min)];
            for (int i = min; i < max; i++)
                paths[i - min] = string.Format(format, i);
            return paths;
        }
    }
}
