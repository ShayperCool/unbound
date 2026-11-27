using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using UnityEditor.PackageManager.Requests;
using UnityEngine;

namespace UnityEditor.PackageManager
{
    public enum ErrorCode
    {
        Unknown = 0,
        NotFound = 1,
        Forbidden = 2,
        InvalidParameter = 3,
        Conflict = 4,
        AggregateError = 5,
    }

    internal enum NativeErrorCode
    {
        Unknown = 0,
        NotFound = 1,
        Forbidden = 2,
        InvalidParameter = 3,
        Conflict = 4,
        AggregateError = 5,
        Cancelled = 6,
        Success = 7,
    }

    internal enum NativeStatusCode
    {
        InQueue = 0,
        InProgress = 1,
        Done = 2,
        Error = 3,
        NotFound = 4,
        Cancelled = 5,
    }

    internal static partial class NativeEnumExtensions
    {
        public static StatusCode ConvertToManaged(this NativeStatusCode status)
        {
            switch (status)
            {
                case NativeStatusCode.InProgress:
                case NativeStatusCode.InQueue:
                    return StatusCode.InProgress;
                case NativeStatusCode.Error:
                case NativeStatusCode.NotFound:
                case NativeStatusCode.Cancelled:
                    return StatusCode.Failure;
                case NativeStatusCode.Done:
                    return StatusCode.Success;
            }
            throw new NotSupportedException($"Unknown native status code {status}");
        }

        public static ErrorCode ConvertToManaged(this NativeErrorCode errorCode)
        {
            switch (errorCode)
            {
                case NativeErrorCode.Unknown:
                case NativeErrorCode.Cancelled:
                case NativeErrorCode.Success:
                    return ErrorCode.Unknown;
                case NativeErrorCode.NotFound:
                    return ErrorCode.NotFound;
                case NativeErrorCode.Forbidden:
                    return ErrorCode.Forbidden;
                case NativeErrorCode.InvalidParameter:
                    return ErrorCode.InvalidParameter;
                case NativeErrorCode.Conflict:
                    return ErrorCode.Conflict;
                case NativeErrorCode.AggregateError:
                    return ErrorCode.AggregateError;
            }
            throw new NotSupportedException($"Unknown native error code {errorCode}");
        }

        public static bool IsCompleted(this NativeStatusCode status) => ConvertToManaged(status) != StatusCode.InProgress;
    }

    /// <summary>An error reported by a Package Manager request or package.</summary>
    [Serializable]
    [StructLayout(LayoutKind.Sequential)]
    public partial class Error
    {
        [SerializeField] NativeErrorCode m_ErrorCode;
        [SerializeField] string m_Message;

        private Error() { }

        internal Error(NativeErrorCode errorCode, string message)
        {
            m_ErrorCode = errorCode;
            m_Message = message;
        }

        public ErrorCode errorCode => m_ErrorCode.ConvertToManaged();
        public string message => m_Message;
    }

    /// <summary>Known versions of a package (Unity 6000.0.71f1, tests/projects/packagemanager).</summary>
    [Serializable]
    [StructLayout(LayoutKind.Sequential)]
    public partial class VersionsInfo
    {
        [SerializeField] string[] m_All;
        [SerializeField] string[] m_Compatible;
        [SerializeField] string m_Recommended;
        [SerializeField] string[] m_Deprecated;

        private VersionsInfo() { }

        internal VersionsInfo(
            IEnumerable<string> all,
            IEnumerable<string> compatible,
            string recommended,
            IEnumerable<string> deprecated)
        {
            m_All = all != null ? all.ToArray() : Array.Empty<string>();
            m_Compatible = compatible != null ? compatible.ToArray() : Array.Empty<string>();
            m_Recommended = recommended ?? string.Empty;
            m_Deprecated = deprecated != null ? deprecated.ToArray() : Array.Empty<string>();
        }

        public string[] all => m_All;
        public string[] compatible => m_Compatible;
        public string recommended => m_Recommended;
        public string[] deprecated => m_Deprecated;

        [Obsolete("'verified' is obsolete; use 'recommended' instead. (UnityUpgradable) -> recommended", false)]
        public string verified => m_Recommended;

        public string latest => (m_All != null && m_All.Length > 0 ? m_All[m_All.Length - 1] : null) ?? string.Empty;
        public string latestCompatible => (m_Compatible != null && m_Compatible.Length > 0 ? m_Compatible[m_Compatible.Length - 1] : null) ?? string.Empty;
    }

    /// <summary>A package of the project, as resolved from its manifest and lock.</summary>
    public sealed partial class PackageInfo
    {
        static readonly List<PackageInfo> s_Registered = new List<PackageInfo>();
        static readonly Dictionary<string, PackageInfo> s_ByName = new Dictionary<string, PackageInfo>(StringComparer.Ordinal);

        [SerializeField] readonly VersionsInfo m_Versions;
        [SerializeField] readonly Error[] m_Errors = Array.Empty<Error>();

        internal PackageInfo()
        {
            m_Versions = new VersionsInfo(null, null, null, null);
        }

        internal static PackageInfo Create(string name, string version, PackageSource source, string resolvedPath,
            string displayName, string description, string category, string type, string[] keywords, bool isDirectDependency) =>
            new PackageInfo(name, version, source, resolvedPath, displayName, description, category, type, keywords, isDirectDependency);

        PackageInfo(string name, string version, PackageSource source, string resolvedPath,
            string displayName, string description, string category, string type, string[] keywords, bool isDirectDependency)
        {
            this.name = name;
            this.version = version;
            this.source = source;
            this.resolvedPath = resolvedPath;
            this.displayName = string.IsNullOrEmpty(displayName) ? name : displayName;
            this.description = description;
            this.category = category;
            this.type = type;
            this.keywords = keywords ?? Array.Empty<string>();
            this.isDirectDependency = isDirectDependency;
            if (source == PackageSource.BuiltIn && !string.IsNullOrEmpty(version))
            {
                var single = new[] { version };
                m_Versions = new VersionsInfo(single, single, version, null);
            }
            else
            {
                m_Versions = new VersionsInfo(null, null, null, null);
            }
        }

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
        public bool isDirectDependency { get; }
        public string version { get; }
        public PackageSource source { get; }
        public string resolvedPath { get; }
        public string assetPath => "Packages/" + name;
        public string name { get; }
        public string displayName { get; }
        public string category { get; }
        public string type { get; }
        public string description { get; }
        public string[] keywords { get; }
        public Error[] errors => m_Errors;

        public VersionsInfo versions
        {
            get
            {
                if (source != PackageSource.BuiltIn && source != PackageSource.Embedded && source != PackageSource.Local)
                    Unbound.Runtime.Stubs.Ignored("PackageInfo.versions", "is not known for remote/archive packages (Unbound reads no registry metadata)");
                return m_Versions;
            }
        }

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
        [SerializeField] readonly Error m_Error;

        private PackageCollection()
        {
            m_Packages = new List<PackageInfo>();
        }

        internal PackageCollection(IEnumerable<PackageInfo> packages) : this(packages, null) { }

        internal PackageCollection(IEnumerable<PackageInfo> packages, Error error)
        {
            m_Packages = packages != null ? new List<PackageInfo>(packages) : new List<PackageInfo>();
            m_Error = error;
        }

        public Error error => m_Error;

        IEnumerator<PackageInfo> IEnumerable<PackageInfo>.GetEnumerator() => m_Packages.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => m_Packages.GetEnumerator();
    }

    /// <summary>The Package Manager's requests, answered from the resolved project offline: Unbound
    /// validates arguments as the Unity 6000.0.71f1 Editor does (tests/projects/packagemanager) and
    /// does not mutate project packages at runtime.</summary>
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

        public static SearchRequest Search(string packageIdOrName) => Search(packageIdOrName, false);

        public static SearchRequest Search(string packageIdOrName, bool offlineMode)
        {
            if (string.IsNullOrWhiteSpace(packageIdOrName))
                throw new ArgumentException("Package id or name cannot be null, empty or whitespace", nameof(packageIdOrName));
            if (!offlineMode)
                Unbound.Runtime.Stubs.Ignored("Client.Search", "only searches registered packages offline (no registry network access)");
            var name = packageIdOrName;
            int at = name.IndexOf('@');
            if (at >= 0) name = name.Substring(0, at);
            var found = PackageInfo.FindForPackageName(name);
            if (found != null)
            {
                if (found.source == PackageSource.BuiltIn)
                    return new SearchRequest(new[] { found }, packageIdOrName);
                if (found.source == PackageSource.Embedded || found.source == PackageSource.Local)
                    return new SearchRequest(new Error(NativeErrorCode.NotFound, $"Package '{packageIdOrName}' was not found."), packageIdOrName);
                Unbound.Runtime.Stubs.Ignored("Client.Search", $"search for {found.source} packages is not measured (Unbound reads no registry cache)");
                return new SearchRequest(new[] { found }, packageIdOrName);
            }
            return new SearchRequest(new Error(NativeErrorCode.NotFound, $"Package '{packageIdOrName}' was not found."), packageIdOrName);
        }

        public static SearchRequest SearchAll() => SearchAll(false);

        public static SearchRequest SearchAll(bool offlineMode)
        {
            Unbound.Runtime.Stubs.Ignored("Client.SearchAll", "returns only registered packages offline (no registry catalog)");
            return new SearchRequest(PackageInfo.GetAllRegisteredPackages(), "");
        }

        public static AddRequest Add(string identifier)
        {
            if (string.IsNullOrWhiteSpace(identifier))
                throw new ArgumentException("Package identifier cannot be null, empty or whitespace", nameof(identifier));
            Unbound.Runtime.Stubs.NotImplemented("UnityEditor.PackageManager.Client.Add");
            return new AddRequest(new Error(NativeErrorCode.Unknown, "Unbound does not add packages at runtime."));
        }

        public static RemoveRequest Remove(string packageName)
        {
            if (string.IsNullOrWhiteSpace(packageName))
                throw new ArgumentException("Package name cannot be null, empty or whitespace", nameof(packageName));
            if (!PackageInfo.IsPackageRegistered(packageName))
                return new RemoveRequest(new Error(NativeErrorCode.NotFound, $"Package '{packageName}' is not installed."), packageName);
            Unbound.Runtime.Stubs.NotImplemented("UnityEditor.PackageManager.Client.Remove");
            return new RemoveRequest(new Error(NativeErrorCode.Unknown, "Unbound does not remove installed packages at runtime."), packageName);
        }

        public static AddAndRemoveRequest AddAndRemove(string[] packagesToAdd = null, string[] packagesToRemove = null)
        {
            packagesToAdd ??= Array.Empty<string>();
            packagesToRemove ??= Array.Empty<string>();
            if (packagesToAdd.Length == 0 && packagesToRemove.Length == 0)
                throw new ArgumentException("No packages provided to add or remove");
            foreach (var p in packagesToAdd)
                if (string.IsNullOrWhiteSpace(p))
                    throw new ArgumentException("Packages to add cannot contain null, empty or whitespace values", nameof(packagesToAdd));
            foreach (var p in packagesToRemove)
                if (string.IsNullOrWhiteSpace(p))
                    throw new ArgumentException("Packages to remove cannot contain null, empty or whitespace values", nameof(packagesToRemove));
            Unbound.Runtime.Stubs.NotImplemented("UnityEditor.PackageManager.Client.AddAndRemove");
            return new AddAndRemoveRequest(new Error(NativeErrorCode.Unknown, "Unbound does not add or remove packages at runtime."));
        }

        public static EmbedRequest Embed(string packageName)
        {
            if (string.IsNullOrWhiteSpace(packageName))
                throw new ArgumentException("Package name cannot be null, empty or whitespace", nameof(packageName));
            var info = PackageInfo.FindForPackageName(packageName);
            if (info == null)
                throw new InvalidOperationException($"Cannot embed package [{packageName}] because it is not registered in the Asset Database.");
            if (info.source == PackageSource.Embedded)
                return new EmbedRequest(new Error(NativeErrorCode.Unknown, $"Cannot embed package [{packageName}] because it is already embedded."));
            Unbound.Runtime.Stubs.NotImplemented("UnityEditor.PackageManager.Client.Embed");
            return new EmbedRequest(new Error(NativeErrorCode.Unknown, "Unbound does not embed packages at runtime."));
        }

        public static void Resolve() =>
            Unbound.Runtime.Stubs.Ignored("Client.Resolve", "does not resolve the packages again (they are resolved when the session starts)");
    }
}

namespace UnityEditor.PackageManager.Requests
{
    public abstract partial class Request
    {
        internal const string ShimPackageType = "shim";

        internal NativeStatusCode m_Status = NativeStatusCode.NotFound;
        [SerializeField] internal long m_Id;
        [SerializeField] internal Error m_Error;

        internal Request() { }

        internal Request(long operationId, NativeStatusCode initialStatus)
        {
            m_Id = operationId;
            m_Status = initialStatus;
        }

        internal Request(Error error)
        {
            m_Status = error != null ? NativeStatusCode.Error : NativeStatusCode.Done;
            m_Error = error;
        }

        internal NativeStatusCode NativeStatus => m_Status;

        internal long Id => m_Id;

        public StatusCode Status
        {
            get
            {
                FetchNativeData();
                return m_Status.ConvertToManaged();
            }
        }

        public bool IsCompleted
        {
            get
            {
                FetchNativeData();
                return m_Status.IsCompleted();
            }
        }

        public Error Error
        {
            get
            {
                FetchNativeData();
                return m_Error;
            }
        }

        [Unbound.EditorFaithful("Unbound answers offline requests synchronously on creation")]
        protected virtual void FetchNativeData() { }
    }

    public abstract partial class Request<T> : Request
    {
        internal Request() : base() { }

        internal Request(long operationId, NativeStatusCode initialStatus) : base(operationId, initialStatus) { }

        internal Request(Error error) : base(error) { }

        protected abstract T GetResult();

        protected sealed override void FetchNativeData()
        {
            base.FetchNativeData();
        }

        public T Result
        {
            get
            {
                FetchNativeData();
                return Status == StatusCode.Success ? GetResult() : default;
            }
        }
    }

    public sealed partial class ListRequest : Request<PackageCollection>
    {
        readonly PackageCollection m_Result;

        private ListRequest() { }

        internal ListRequest(long operationId, NativeStatusCode initialStatus) : base(operationId, initialStatus) { }

        internal ListRequest(PackageCollection result) : base(0, NativeStatusCode.Done) { m_Result = result; }

        protected override PackageCollection GetResult() => m_Result;
    }

    public sealed partial class SearchRequest : Request<PackageInfo[]>
    {
        readonly PackageInfo[] m_Result;

        private SearchRequest() { }

        internal SearchRequest(long operationId, NativeStatusCode initialStatus, string packageIdOrName) : base(operationId, initialStatus)
        {
            PackageIdOrName = packageIdOrName;
        }

        internal SearchRequest(PackageInfo[] result, string packageIdOrName) : base(0, NativeStatusCode.Done)
        {
            m_Result = result;
            PackageIdOrName = packageIdOrName;
        }

        internal SearchRequest(Error error, string packageIdOrName) : base(error)
        {
            PackageIdOrName = packageIdOrName;
        }

        public string PackageIdOrName { get; }

        protected override PackageInfo[] GetResult() => m_Result;
    }

    [Serializable]
    public sealed partial class AddRequest : Request<PackageInfo>
    {
        readonly PackageInfo m_Result;

        private AddRequest() { }

        internal AddRequest(long operationId, NativeStatusCode initialStatus) : base(operationId, initialStatus) { }

        internal AddRequest(Error error) : base(error) { }

        protected override PackageInfo GetResult() => m_Result;
    }

    [Serializable]
    public sealed partial class RemoveRequest : Request
    {
        [SerializeField] readonly string m_PackageIdOrName;

        private RemoveRequest() { }

        internal RemoveRequest(long operationId, NativeStatusCode initialStatus, string packageName) : base(operationId, initialStatus)
        {
            m_PackageIdOrName = packageName;
        }

        internal RemoveRequest(Error error, string packageName) : base(error)
        {
            m_PackageIdOrName = packageName;
        }

        public string PackageIdOrName => m_PackageIdOrName;
    }

    [Serializable]
    public sealed partial class AddAndRemoveRequest : Request<PackageCollection>
    {
        readonly PackageCollection m_Result;

        private AddAndRemoveRequest() { }

        internal AddAndRemoveRequest(long operationId, NativeStatusCode initialStatus) : base(operationId, initialStatus) { }

        internal AddAndRemoveRequest(Error error) : base(error) { }

        protected override PackageCollection GetResult() => m_Result;
    }

    [Serializable]
    public sealed partial class EmbedRequest : Request<PackageInfo>
    {
        readonly PackageInfo m_Result;

        private EmbedRequest() { }

        internal EmbedRequest(long operationId, NativeStatusCode initialStatus) : base(operationId, initialStatus) { }

        internal EmbedRequest(Error error) : base(error) { }

        protected override PackageInfo GetResult() => m_Result;
    }
}
