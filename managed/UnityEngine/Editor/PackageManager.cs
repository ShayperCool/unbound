using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor.PackageManager.Requests;

namespace UnityEditor.PackageManager
{
    /// <summary>A package of the project, as resolved from its manifest and lock.</summary>
    public sealed partial class PackageInfo
    {
        static readonly List<PackageInfo> s_Registered = new List<PackageInfo>();
        static readonly Dictionary<string, PackageInfo> s_ByName = new Dictionary<string, PackageInfo>(StringComparer.Ordinal);

        internal PackageInfo() { }

        internal static PackageInfo Create(string name, string version, PackageSource source, string resolvedPath,
            string displayName, string description, string category, string type, string[] keywords, bool isDirectDependency) =>
            new PackageInfo
            {
                name = name,
                version = version,
                source = source,
                resolvedPath = resolvedPath,
                displayName = string.IsNullOrEmpty(displayName) ? name : displayName,
                description = description,
                category = category,
                type = type,
                keywords = keywords ?? Array.Empty<string>(),
                isDirectDependency = isDirectDependency,
            };

        internal static void Register(IEnumerable<PackageInfo> packages)
        {
            s_Registered.Clear();
            s_ByName.Clear();
            foreach (var p in packages)
            {
                s_Registered.Add(p);
                s_ByName[p.name] = p;
            }
        }

        internal static IReadOnlyList<PackageInfo> Registered => s_Registered;

        public string packageId => name + "@" + version;
        public bool isDirectDependency { get; private set; }
        public string version { get; private set; }
        public PackageSource source { get; private set; }
        public string resolvedPath { get; private set; }
        public string assetPath => "Packages/" + name;
        public string name { get; private set; }
        public string displayName { get; private set; }
        public string category { get; private set; }
        public string type { get; private set; }
        public string description { get; private set; }
        public string[] keywords { get; private set; }
        public bool isDeprecated
        {
            get
            {
                Unbound.Runtime.Stubs.Ignored("PackageInfo.isDeprecated", "is not known (Unbound reads no registry metadata)");
                return false;
            }
        }

        public string deprecationMessage
        {
            get
            {
                Unbound.Runtime.Stubs.Ignored("PackageInfo.deprecationMessage", "is not known (Unbound reads no registry metadata)");
                return "";
            }
        }

        public static PackageInfo FindForAssetPath(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath)) return null;
            var path = assetPath.Replace('\\', '/');
            if (!path.StartsWith("Packages/", StringComparison.Ordinal)) return null;
            var rest = path.Substring("Packages/".Length);
            int slash = rest.IndexOf('/');
            return FindForPackageName(slash < 0 ? rest : rest.Substring(0, slash));
        }

        public static PackageInfo FindForPackageName(string name) =>
            name != null && s_ByName.TryGetValue(name, out var p) ? p : null;

        public static PackageInfo FindForAssembly(Assembly assembly)
        {
            if (assembly == null) return null;
            var path = Unbound.Runtime.GameDomain.SourcePathOf(assembly);
            if (path == null) return null;
            foreach (var p in s_Registered)
                if (path.StartsWith(p.resolvedPath.TrimEnd('/') + "/", StringComparison.Ordinal)) return p;
            return null;
        }

        public static PackageInfo[] GetAllRegisteredPackages() => s_Registered.ToArray();

        public static bool IsPackageRegistered(string name) => FindForPackageName(name) != null;
    }

    /// <summary>The project's packages, as a List request returns them.</summary>
    public partial class PackageCollection : IEnumerable<PackageInfo>, IEnumerable
    {
        readonly List<PackageInfo> m_Packages;

        internal PackageCollection(IEnumerable<PackageInfo> packages) { m_Packages = new List<PackageInfo>(packages); }

        IEnumerator<PackageInfo> IEnumerable<PackageInfo>.GetEnumerator() => m_Packages.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => m_Packages.GetEnumerator();
    }

    /// <summary>The Package Manager's requests, answered from the resolved project offline: Unbound
    /// does not change the project's packages (add, remove and embed stay unsupported).</summary>
    public static partial class Client
    {
        public static ListRequest List() => List(false, false);

        public static ListRequest List(bool offlineMode) => List(offlineMode, false);

        public static ListRequest List(bool offlineMode, bool includeIndirectDependencies)
        {
            var list = new List<PackageInfo>();
            foreach (var p in PackageInfo.Registered)
                if (includeIndirectDependencies || p.isDirectDependency) list.Add(p);
            return new ListRequest(new PackageCollection(list));
        }

        public static SearchRequest Search(string packageIdOrName) => Search(packageIdOrName, true);

        public static SearchRequest Search(string packageIdOrName, bool offlineMode)
        {
            var name = packageIdOrName ?? "";
            int at = name.IndexOf('@');
            if (at >= 0) name = name.Substring(0, at);
            var found = PackageInfo.FindForPackageName(name);
            return new SearchRequest(found == null ? Array.Empty<PackageInfo>() : new[] { found }, packageIdOrName);
        }

        public static SearchRequest SearchAll() => SearchAll(true);

        public static SearchRequest SearchAll(bool offlineMode) => new SearchRequest(PackageInfo.GetAllRegisteredPackages(), "");

        public static void Resolve() =>
            Unbound.Runtime.Stubs.Ignored("Client.Resolve", "does not resolve the packages again (they are resolved when the session starts)");
    }
}

namespace UnityEditor.PackageManager.Requests
{
    public abstract partial class Request
    {
        internal StatusCode m_Status = StatusCode.Success;

        internal Request() { }

        public StatusCode Status => m_Status;

        public bool IsCompleted => m_Status != StatusCode.InProgress;
    }

    public abstract partial class Request<T> : Request
    {
        internal Request() { }

        protected abstract T GetResult();

        public T Result => IsCompleted ? GetResult() : default;
    }

    public sealed partial class ListRequest : Request<PackageCollection>
    {
        readonly PackageCollection m_Result;

        internal ListRequest(PackageCollection result) { m_Result = result; }

        protected override PackageCollection GetResult() => m_Result;
    }

    public sealed partial class SearchRequest : Request<PackageInfo[]>
    {
        readonly PackageInfo[] m_Result;

        internal SearchRequest(PackageInfo[] result, string packageIdOrName)
        {
            m_Result = result;
            PackageIdOrName = packageIdOrName;
        }

        public string PackageIdOrName { get; }

        protected override PackageInfo[] GetResult() => m_Result;
    }
}
