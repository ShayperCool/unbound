using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Unbound.Runtime;
using UnityEngine;
using Object = UnityEngine.Object;

namespace UnityEditor.VersionControl
{
    static class VersionControlFacts
    {
        /// <summary>Why the members that do nothing or answer a constant are as the Editor's.</summary>
        internal const string NoProvider = "No provider: nothing to release, lock, cache or wait for, as the Editor without one (tests/projects/vcsqueries)";
    }

    // UnityEditor.VersionControl in a project without a version control provider, as the Unity
    // 6000.0.71f1 Editor answers (tests/projects/vcsqueries): the provider is disabled, inactive and
    // offline with no plug-in; checking out or getting the latest fails with an error naming the
    // files (logged), a status fails without a word; assets are answered from their paths. Unbound
    // has no version control integration: a project whose settings choose a provider is said.

    /// <summary>A file or folder as version control sees it.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public partial class Asset
    {
        readonly string m_Path;
        readonly States m_State;

        public Asset(string clientPath) : this(clientPath, null) { }

        /// <summary>An asset of <paramref name="clientPath"/>: a folder's path ends in '/'; its state
        /// is <paramref name="state"/> (a new asset's: none, a .meta's MetaFile, vcsqueries new.*).</summary>
        internal Asset(string clientPath, States? state)
        {
            var path = (clientPath ?? "").Replace('\\', '/');
            if (path.Length > 0 && !path.EndsWith("/", StringComparison.Ordinal) && Directory.Exists(AssetDatabase.FileOf(path))) path += "/";
            m_Path = path;
            m_State = state ?? (path.EndsWith(".meta", StringComparison.OrdinalIgnoreCase) ? States.MetaFile : States.None);
        }

        internal static bool IsState(States isThisState, States partOfThisState) => (isThisState & partOfThisState) != 0;

        public bool IsState(States state) => IsState(this.state, state);

        public bool IsOneOfStates(States[] states)
        {
            foreach (var s in states)
                if ((state & s) != 0) return true;
            return false;
        }

        internal bool IsUnderVersionControl => IsState(States.Synced) || IsState(States.OutOfSync) || IsState(States.AddedLocal);

        public void Edit()
        {
            var loaded = Load();
            if (loaded != null) AssetDatabase.OpenAsset(loaded);
        }

        public Object Load() => state == States.DeletedLocal || isMeta ? null : AssetDatabase.LoadAssetAtPath(path, typeof(Object));

        /// <summary>The state's main word, as the Editor's lists show it.</summary>
        internal static string StateToString(States state)
        {
            if (IsState(state, States.AddedLocal)) return "Added Local";
            if (IsState(state, States.AddedRemote)) return "Added Remote";
            if (IsState(state, States.DeletedRemote)) return "Deleted Remote";
            if (IsState(state, States.CheckedOutLocal) && !IsState(state, States.LockedLocal)) return "Checked Out Local";
            if (IsState(state, States.CheckedOutRemote) && !IsState(state, States.LockedRemote)) return "Checked Out Remote";
            if (IsState(state, States.Conflicted)) return "Conflicted";
            if (IsState(state, States.DeletedLocal)) return "Deleted Local";
            if (IsState(state, States.Local) && !(IsState(state, States.OutOfSync) || IsState(state, States.Synced))) return "Local";
            if (IsState(state, States.LockedLocal)) return "Locked Local";
            if (IsState(state, States.LockedRemote)) return "Locked Remote";
            if (IsState(state, States.OutOfSync)) return "Out Of Sync";
            if (IsState(state, States.Updating)) return "Updating Status";
            return "";
        }

        static readonly (States State, string Name)[] s_StateNames =
        {
            (States.AddedLocal, "Added Local"), (States.AddedRemote, "Added Remote"), (States.CheckedOutLocal, "Checked Out Local"),
            (States.CheckedOutRemote, "Checked Out Remote"), (States.Conflicted, "Conflicted"), (States.DeletedLocal, "Deleted Local"),
            (States.DeletedRemote, "Deleted Remote"), (States.Local, "Local"), (States.LockedLocal, "Locked Local"),
            (States.LockedRemote, "Locked Remote"), (States.OutOfSync, "Out Of Sync"), (States.Synced, "Synced"), (States.Missing, "Missing"),
            (States.ReadOnly, "ReadOnly"), (States.Unversioned, "Unversioned"), (States.Exclusive, "Exclusive"),
        };

        /// <summary>Every state's word, comma separated.</summary>
        internal static string AllStateToString(States state)
        {
            var text = new StringBuilder();
            foreach (var (s, name) in s_StateNames)
                if (IsState(state, s)) text.Append(text.Length > 0 ? ", " : "").Append(name);
            return text.ToString();
        }

        internal string StateToString() => StateToString(state);

        [Unbound.EditorFaithful(VersionControlFacts.NoProvider)]
        public void Dispose() { }

        /// <summary>Whether this asset is in the folder <paramref name="other"/> (or below it).</summary>
        public bool IsChildOf(Asset other) =>
            other != null && other.isFolder && path.Length > other.path.Length && path.StartsWith(other.path, StringComparison.OrdinalIgnoreCase);

        public string prettyPath => path;

        public States state => m_State;

        public string path => m_Path;

        public string metaPath => isMeta ? path : (isFolder ? path.TrimEnd('/') : path) + ".meta";

        /// <summary>The asset's own path: a .meta's asset's (vcsqueries get.meta).</summary>
        public string assetPath => isMeta ? path.Substring(0, path.Length - ".meta".Length) : path;

        public bool isFolder => m_Path.EndsWith("/", StringComparison.Ordinal);

        [Unbound.EditorFaithful(VersionControlFacts.NoProvider)]
        public bool readOnly => false;

        public bool isMeta => m_Path.EndsWith(".meta", StringComparison.OrdinalIgnoreCase);

        [Unbound.EditorFaithful(VersionControlFacts.NoProvider)]
        public bool locked => false;

        /// <summary>The file name without its extension; a folder's is empty (vcsqueries new.data).</summary>
        public string name => isFolder ? "" : Path.GetFileNameWithoutExtension(m_Path);

        public string fullName => isFolder ? "" : Path.GetFileName(m_Path);

        /// <summary>Whether the path is a file of the project (a folder, a missing file and an empty
        /// path are not: vcsqueries new.*).</summary>
        public bool isInCurrentProject => m_Path.Length > 0 && !isFolder && File.Exists(AssetDatabase.FileOf(m_Path));

        [Flags]
        public enum States
        {
            None = 0,
            Local = 1,
            Synced = 2,
            OutOfSync = 4,
            Missing = 8,
            CheckedOutLocal = 16,
            CheckedOutRemote = 32,
            DeletedLocal = 64,
            DeletedRemote = 128,
            AddedLocal = 256,
            AddedRemote = 512,
            Conflicted = 1024,
            LockedLocal = 2048,
            LockedRemote = 4096,
            Updating = 8192,
            ReadOnly = 16384,
            MetaFile = 32768,
            MovedLocal = 65536,
            MovedRemote = 131072,
            Unversioned = 262144,
            Exclusive = 524288,
        }
    }

    [Flags]
    public enum CheckoutMode
    {
        Asset = 1,
        Meta = 2,
        Both = 3,
        Exact = 4,
    }

    [Flags]
    public enum OnlineState
    {
        Updating = 0,
        Online = 1,
        Offline = 2,
    }

    // Declared here for the members above; the rest of them are generated.
    [StructLayout(LayoutKind.Sequential)]
    public partial class Plugin
    {
        internal Plugin() { }
    }

    [StructLayout(LayoutKind.Sequential)]
    public partial class ChangeSet { }

    public partial class AssetList : List<Asset>
    {
        public AssetList() { }

        /// <summary>A new list; like the Editor's, it does not copy <paramref name="src"/>.</summary>
        public AssetList(AssetList src) { }

        public AssetList Filter(bool includeFolder, params Asset.States[] states)
        {
            var filter = new AssetList();
            if (!includeFolder && (states == null || states.Length == 0)) return filter;
            foreach (var asset in this)
            {
                if (asset.isFolder)
                {
                    if (includeFolder) filter.Add(asset);
                }
                else if (asset.IsOneOfStates(states)) filter.Add(asset);
            }
            return filter;
        }

        public int FilterCount(bool includeFolder, params Asset.States[] states)
        {
            if (!includeFolder && states == null) return Count;
            int count = 0;
            foreach (var asset in this)
                if (asset.isFolder || asset.IsOneOfStates(states)) count++;
            return count;
        }

        /// <summary>The list without the assets in a folder it holds (vcsqueries assetlist.filter).</summary>
        public AssetList FilterChildren()
        {
            var unique = new AssetList();
            unique.AddRange(this);
            foreach (var asset in this) unique.RemoveAll(a => a.IsChildOf(asset));
            return unique;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    public partial class Message
    {
        readonly Severity m_Severity;
        readonly string m_Message;

        internal Message() : this(Severity.Info, "") { }

        internal Message(Severity severity, string message)
        {
            m_Severity = severity;
            m_Message = message ?? "";
        }

        /// <summary>Logs the message as Unity's published C# does: "Version control:", a new line and
        /// the message, as information whatever its severity.</summary>
        public void Show() => Debug.Log("Version control:\n" + m_Message);

        [Unbound.EditorFaithful(VersionControlFacts.NoProvider)]
        public void Dispose() { }

        public Severity severity => m_Severity;

        public string message => m_Message;

        public enum Severity
        {
            Data = 0,
            Verbose = 1,
            Info = 2,
            Warning = 3,
            Error = 4,
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    public partial class Task
    {
        readonly bool m_Success;
        readonly int m_ResultCode;
        readonly Message[] m_Messages;
        readonly AssetList m_Assets = new AssetList();

        /// <summary>A task that has run: done, with <paramref name="messages"/>.</summary>
        internal Task(bool success, int resultCode, params Message[] messages)
        {
            m_Success = success;
            m_ResultCode = resultCode;
            m_Messages = messages ?? Array.Empty<Message>();
        }

        /// <summary>Done already: the provider runs nothing (no provider).</summary>
        [Unbound.EditorFaithful(VersionControlFacts.NoProvider)]
        public void Wait() { }

        public bool success => m_Success;

        public int resultCode => m_ResultCode;

        [Unbound.EditorFaithful(VersionControlFacts.NoProvider)]
        public string text => "";

        [Unbound.EditorFaithful(VersionControlFacts.NoProvider)]
        public string description => "";

        public Message[] messages => m_Messages;

        public AssetList assetList => m_Assets;
    }

    public partial class Provider
    {
        public delegate bool PreCheckoutCallback(AssetList list, ref string changesetID, ref string changesetDescription);

        /// <summary>Kept, not run (<see cref="Checkout(AssetList, CheckoutMode)"/>).</summary>
        public static PreCheckoutCallback preCheckoutCallback;

        /// <summary>The result code of a task the provider refuses (vcsqueries checkout.*, status.*).</summary>
        const int FailedResult = 2;

        public static bool enabled => NoProvider();

        public static bool isActive => NoProvider();

        public static bool requiresNetwork => NoProvider();

        public static bool hasCheckoutSupport => NoProvider();

        public static bool hasLockingSupport => NoProvider();

        public static bool isVersioningFolders => NoProvider();

        public static OnlineState onlineState
        {
            get
            {
                NoProvider();
                return OnlineState.Offline;
            }
        }

        public static string offlineReason
        {
            get
            {
                NoProvider();
                return "";
            }
        }

        public static Task activeTask
        {
            get
            {
                NoProvider();
                return null;
            }
        }

        public static Plugin GetActivePlugin()
        {
            NoProvider();
            return null;
        }

        /// <summary>False: there is no provider. A project whose settings choose one is not supported
        /// (as AssetDatabase's editability queries, AssetEditability).</summary>
        static bool NoProvider()
        {
            if (VersionControlSettings.ChoosesProvider)
                throw new NotSupportedException("UnityEditor.VersionControl.Provider with an active version control provider ('" + VersionControlSettings.mode + "') is not supported.");
            return false;
        }

        /// <summary>The asset of a path that exists, its status being read (Updating, vcsqueries
        /// get.*); null for none.</summary>
        public static Asset GetAssetByPath(string unityPath)
        {
            if (string.IsNullOrEmpty(unityPath)) return null;
            var file = AssetDatabase.FileOf(unityPath.Replace('\\', '/'));
            return File.Exists(file) || Directory.Exists(file) ? new Asset(unityPath, Asset.States.Updating) : null;
        }

        public static Asset GetAssetByGUID(string guid)
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            return string.IsNullOrEmpty(path) ? null : GetAssetByPath(path);
        }

        public static bool IsOpenForEdit(Asset asset)
        {
            NoProvider();
            return true;
        }

        [Unbound.EditorFaithful(VersionControlFacts.NoProvider)]
        public static void ClearCache() { }

        // ------------------------------------------------------------------ checkout

        public static Task Checkout(AssetList assets, CheckoutMode mode)
        {
            // Unity's C# runs a pre-checkout callback before the checkout (with the list
            // consolidated, as an exact checkout): not done here, said (not measured).
            if (preCheckoutCallback != null)
                Stubs.Ignored("UnityEditor.VersionControl.Provider.preCheckoutCallback", "is not run by a checkout yet (Unbound has no provider): the checkout is refused as offline without it");
            return Refused("check out", Paths(assets, mode));
        }

        public static Task Checkout(AssetList assets, CheckoutMode mode, ChangeSet changeset) => Checkout(assets, mode);

        public static Task Checkout(string[] assets, CheckoutMode mode) => Checkout(ListOf(assets), mode);

        public static Task Checkout(string[] assets, CheckoutMode mode, ChangeSet changeset) => Checkout(assets, mode);

        public static Task Checkout(Object[] assets, CheckoutMode mode)
        {
            var list = new AssetList();
            if (assets != null)
                foreach (var o in assets)
                    if (o != null && AssetDatabase.GetAssetPath(o) is { Length: > 0 } path) list.Add(new Asset(path));
            return Checkout(list, mode);
        }

        public static Task Checkout(Object[] assets, CheckoutMode mode, ChangeSet changeset) => Checkout(assets, mode);

        public static Task Checkout(Asset asset, CheckoutMode mode) => Checkout(ListOf(asset), mode);

        public static Task Checkout(Asset asset, CheckoutMode mode, ChangeSet changeset) => Checkout(asset, mode);

        public static Task Checkout(string asset, CheckoutMode mode) => Checkout(new[] { asset }, mode);

        public static Task Checkout(string asset, CheckoutMode mode, ChangeSet changeset) => Checkout(asset, mode);

        public static Task Checkout(Object asset, CheckoutMode mode) => Checkout(new[] { asset }, mode);

        public static Task Checkout(Object asset, CheckoutMode mode, ChangeSet changeset) => Checkout(asset, mode);

        public static bool CheckoutIsValid(AssetList assets) => NoProvider();

        public static bool CheckoutIsValid(AssetList assets, CheckoutMode mode) => NoProvider();

        public static bool CheckoutIsValid(Asset asset) => NoProvider();

        public static bool CheckoutIsValid(Asset asset, CheckoutMode mode) => NoProvider();

        // ------------------------------------------------------------------ status, get latest

        public static Task Status(AssetList assets) => Silent();

        public static Task Status(Asset asset) => Silent();

        public static Task Status(AssetList assets, bool recursively) => Silent();

        public static Task Status(Asset asset, bool recursively) => Silent();

        public static Task Status(string[] assets) => Silent();

        public static Task Status(string[] assets, bool recursively) => Silent();

        public static Task Status(string asset) => Silent();

        public static Task Status(string asset, bool recursively) => Silent();

        public static bool GetLatestIsValid(AssetList assets) => NoProvider();

        public static bool GetLatestIsValid(Asset asset) => NoProvider();

        public static Task GetLatest(AssetList assets) => Refused("get latest", Paths(assets, CheckoutMode.Asset));

        public static Task GetLatest(Asset asset) => GetLatest(ListOf(asset));

        static Task Silent()
        {
            NoProvider();
            return new Task(false, FailedResult);
        }

        /// <summary>A refused task's error, logged: "Cannot &lt;verb&gt; files in offline mode" and the
        /// paths, one a line (vcsqueries checkout.*, getlatest).</summary>
        static Task Refused(string verb, List<string> paths)
        {
            NoProvider();
            var message = new Message(Message.Severity.Error, "Cannot " + verb + " files in offline mode" + (paths.Count > 0 ? "\n" + string.Join("\n", paths) : ""));
            // The Editor logs the refusal as it makes the task (vcsqueries checkout.*.logs).
            Debug.LogError("Version Control: " + message.message);
            return new Task(false, FailedResult, message);
        }

        /// <summary>The paths of the assets a task names: an asset's own path, and (CheckoutMode.Meta
        /// alone) its .meta's (the measured tasks name assets: vcsqueries checkout.*).</summary>
        static List<string> Paths(AssetList assets, CheckoutMode mode)
        {
            var paths = new List<string>();
            if (assets == null) return paths;
            foreach (var asset in assets)
                if (asset != null && asset.path.Length > 0) paths.Add(mode == CheckoutMode.Meta ? asset.metaPath : asset.assetPath);
            return paths;
        }

        static AssetList ListOf(Asset asset)
        {
            var list = new AssetList();
            if (asset != null) list.Add(asset);
            return list;
        }

        static AssetList ListOf(string[] paths)
        {
            var list = new AssetList();
            if (paths != null)
                foreach (var path in paths)
                    if (!string.IsNullOrEmpty(path)) list.Add(new Asset(path));
            return list;
        }
    }

    public static partial class VersionControlUtils
    {
        /// <summary>Whether a project-relative path is under version control: one in Assets, Packages
        /// or ProjectSettings (vcsqueries versioned.*; AssetEditability's rule); others, and absolute
        /// paths, are not.</summary>
        public static bool IsPathVersioned(string path) => !string.IsNullOrEmpty(path) && AssetDatabase.IsVersionedPath(path);
    }
}

namespace UnityEditor
{
    public sealed partial class VersionControlSettings : Object
    {
        /// <summary>A mode a script set this session (not written to the settings).</summary>
        static string s_SetMode;

        /// <summary>The project's version control mode (ProjectSettings/VersionControlSettings.asset
        /// m_Mode as the settings hold it; "Visible Meta Files" without it, vcsqueries settings.mode).</summary>
        public static string mode
        {
            get => s_SetMode ?? (SettingsAssets.ReadBody("VersionControlSettings")?.Get("m_Mode")?.Text is { Length: > 0 } mode ? mode : "Visible Meta Files");
            set
            {
                Stubs.Ignored("UnityEditor.VersionControlSettings.mode setter", "keeps the mode for the session: Unbound does not write it into ProjectSettings yet");
                s_SetMode = value ?? "";
            }
        }

        /// <summary>Whether the mode chooses a version control provider (not one of the modes without
        /// one), which Unbound does not support.</summary>
        internal static bool ChoosesProvider => mode is not ("" or "Visible Meta Files" or "Hidden Meta Files" or "Generic");
    }
}
