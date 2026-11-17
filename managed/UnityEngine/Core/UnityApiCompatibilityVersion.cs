using System;

namespace UnityEngine
{
    /// <summary>The Unity API version an assembly was built against (read by the Editor's API
    /// updater; Unbound keeps the values only). Matches the Unity 6000.0.71f1 Editor
    /// (tests/projects/coreremainder).</summary>
    [AttributeUsage(AttributeTargets.Assembly, AllowMultiple = false)]
    public partial class UnityAPICompatibilityVersionAttribute : Attribute
    {
        readonly string _version;
        readonly string[] _configurationAssembliesHashes;

        [Obsolete("This overload of the attribute has been deprecated. Use the constructor that takes the version and a boolean", true)]
        public UnityAPICompatibilityVersionAttribute(string version) => _version = version;

        public UnityAPICompatibilityVersionAttribute(string version, bool checkOnlyUnityVersion)
        {
            if (!checkOnlyUnityVersion) throw new ArgumentException("You must pass 'true' to checkOnlyUnityVersion parameter.");
            _version = version;
        }

        public UnityAPICompatibilityVersionAttribute(string version, string[] configurationAssembliesHashes)
        {
            _version = version;
            _configurationAssembliesHashes = configurationAssembliesHashes;
        }

        public string version => _version;

        internal string[] configurationAssembliesHashes => _configurationAssembliesHashes;
    }
}
