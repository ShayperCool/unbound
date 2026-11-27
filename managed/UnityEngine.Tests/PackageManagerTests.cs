using System;
using System.Collections.Generic;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using UnityEngine;
using Xunit;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace UnityEngine.Tests
{
    /// <summary>
    /// PackageManager Error, VersionsInfo, PackageInfo.versions/errors, Request.Error,
    /// PackageCollection.error, and Client.Search/Add/Remove/AddAndRemove/Embed validation
    /// and failure paths (Unity 6000.0.71f1, tests/projects/packagemanager).
    /// </summary>
    [Collection("Frames")]
    public sealed class PackageManagerTests : IDisposable
    {
        readonly List<PackageInfo> m_SavedPackages = new List<PackageInfo>(PackageInfo.Registered);

        public PackageManagerTests()
        {
            PackageInfo.Register(new[]
            {
                PackageInfo.Create("com.probe.pm", "0.1.0", PackageSource.Embedded, "/tmp/pm", "Probe PM", null, null, null, null, true),
                PackageInfo.Create("com.probe.local", "0.2.0", PackageSource.Local, "/tmp/local", "Probe Local", null, null, null, null, true),
                PackageInfo.Create("com.unity.modules.audio", "1.0.0", PackageSource.BuiltIn, "/tmp/audio", "Audio", null, null, null, null, true),
                PackageInfo.Create("com.probe.registry", "2.1.0", PackageSource.Registry, "/tmp/reg", "Probe Registry", null, null, null, null, true),
            });
        }

        public void Dispose()
        {
            PackageInfo.Register(m_SavedPackages);
        }

        sealed class CaptureLogHandler : ILogHandler
        {
            public readonly List<string> Messages = new List<string>();
            public void LogFormat(LogType logType, Object context, string format, params object[] args) =>
                Messages.Add(string.Format(format, args));
            public void LogException(Exception exception, Object context) =>
                Messages.Add(exception.ToString());
        }

        static List<string> CaptureLogs(Action action, params string[] forgetKeys)
        {
            var prev = Debug.unityLogger.logHandler;
            var capture = new CaptureLogHandler();
            Debug.unityLogger.logHandler = capture;
            try
            {
                foreach (var key in forgetKeys) Unbound.Runtime.Stubs.Forget(key);
                action();
            }
            finally
            {
                Debug.unityLogger.logHandler = prev;
            }
            return capture.Messages;
        }

        [Fact]
        public void NativeEnumExtensionsConvertStatusesAndErrorCodes()
        {
            Assert.Equal(StatusCode.InProgress, NativeStatusCode.InQueue.ConvertToManaged());
            Assert.Equal(StatusCode.InProgress, NativeStatusCode.InProgress.ConvertToManaged());
            Assert.False(NativeStatusCode.InQueue.IsCompleted());
            Assert.False(NativeStatusCode.InProgress.IsCompleted());
            Assert.Equal(StatusCode.Success, NativeStatusCode.Done.ConvertToManaged());
            Assert.True(NativeStatusCode.Done.IsCompleted());
            Assert.Equal(StatusCode.Failure, NativeStatusCode.Error.ConvertToManaged());
            Assert.Equal(StatusCode.Failure, NativeStatusCode.NotFound.ConvertToManaged());
            Assert.Equal(StatusCode.Failure, NativeStatusCode.Cancelled.ConvertToManaged());
            Assert.True(NativeStatusCode.Error.IsCompleted());
            Assert.Throws<NotSupportedException>(() => ((NativeStatusCode)99).ConvertToManaged());

            Assert.Equal(ErrorCode.Unknown, NativeErrorCode.Unknown.ConvertToManaged());
            Assert.Equal(ErrorCode.Unknown, NativeErrorCode.Cancelled.ConvertToManaged());
            Assert.Equal(ErrorCode.Unknown, NativeErrorCode.Success.ConvertToManaged());
            Assert.Equal(ErrorCode.NotFound, NativeErrorCode.NotFound.ConvertToManaged());
            Assert.Equal(ErrorCode.Forbidden, NativeErrorCode.Forbidden.ConvertToManaged());
            Assert.Equal(ErrorCode.InvalidParameter, NativeErrorCode.InvalidParameter.ConvertToManaged());
            Assert.Equal(ErrorCode.Conflict, NativeErrorCode.Conflict.ConvertToManaged());
            Assert.Equal(ErrorCode.AggregateError, NativeErrorCode.AggregateError.ConvertToManaged());
            Assert.Throws<NotSupportedException>(() => ((NativeErrorCode)99).ConvertToManaged());
        }

        [Fact]
        public void VersionsInfoComputesLatestAndHandlesNullInputs()
        {
            var empty = new VersionsInfo(null, null, null, null);
            Assert.Empty(empty.all);
            Assert.Empty(empty.compatible);
            Assert.Equal("", empty.recommended);
#pragma warning disable CS0618
            Assert.Equal("", empty.verified);
#pragma warning restore CS0618
            Assert.Empty(empty.deprecated);
            Assert.Equal("", empty.latest);
            Assert.Equal("", empty.latestCompatible);

            var populated = new VersionsInfo(
                new[] { "1.0.0", "2.0.0" },
                new[] { "1.0.0", "1.5.0" },
                "1.5.0",
                new[] { "0.9.0" });
            Assert.Equal("2.0.0", populated.latest);
            Assert.Equal("1.5.0", populated.latestCompatible);
            Assert.Equal("1.5.0", populated.recommended);
#pragma warning disable CS0618
            Assert.Equal("1.5.0", populated.verified);
#pragma warning restore CS0618
            Assert.Equal(new[] { "0.9.0" }, populated.deprecated);

            var nullTail = new VersionsInfo(new[] { "1.0.0", null }, new string[] { null }, null, null);
            Assert.Equal("", nullTail.latest);
            Assert.Equal("", nullTail.latestCompatible);
        }

        [Fact]
        public void PackageInfoVersionsAndErrorsMatchMeasuredSourcesAndWarnForRegistry()
        {
            var embedded = PackageInfo.FindForPackageName("com.probe.pm");
            var local = PackageInfo.FindForPackageName("com.probe.local");
            var builtin = PackageInfo.FindForPackageName("com.unity.modules.audio");
            var registry = PackageInfo.FindForPackageName("com.probe.registry");

            var cleanLogs = CaptureLogs(() =>
            {
                Assert.Empty(embedded.versions.all);
                Assert.Equal("", embedded.versions.recommended);
                Assert.Equal("", embedded.versions.latest);
                Assert.Empty(embedded.errors);

                Assert.Empty(local.versions.all);
                Assert.Equal("", local.versions.latestCompatible);
                Assert.Empty(local.errors);

                Assert.Equal(new[] { "1.0.0" }, builtin.versions.all);
                Assert.Equal(new[] { "1.0.0" }, builtin.versions.compatible);
                Assert.Equal("1.0.0", builtin.versions.recommended);
#pragma warning disable CS0618
                Assert.Equal("1.0.0", builtin.versions.verified);
#pragma warning restore CS0618
                Assert.Equal("1.0.0", builtin.versions.latest);
                Assert.Equal("1.0.0", builtin.versions.latestCompatible);
                Assert.Empty(builtin.versions.deprecated);
                Assert.Empty(builtin.errors);
            }, "PackageInfo.versions");
            Assert.Empty(cleanLogs);

            VersionsInfo registryVersions = null;
            var registryLogs = CaptureLogs(() => registryVersions = registry.versions, "PackageInfo.versions");
            Assert.NotNull(registryVersions);
            Assert.Empty(registryVersions.all);
            Assert.Contains(registryLogs, m => m.Contains("PackageInfo.versions"));
        }

        [Fact]
        public void PackageCollectionExposesError()
        {
            var ok = new PackageCollection(Array.Empty<PackageInfo>());
            Assert.Null(ok.error);

            var err = new Error(NativeErrorCode.Conflict, "conflict");
            var withErr = new PackageCollection(Array.Empty<PackageInfo>(), err);
            Assert.Same(err, withErr.error);
            Assert.Equal(ErrorCode.Conflict, withErr.error.errorCode);
            Assert.Equal("conflict", withErr.error.message);
        }

        [Fact]
        public void SearchValidatesArgumentsAndMatchesOfflineLookup()
        {
            foreach (var bad in new[] { null, "", "   " })
            {
                var ex = Assert.Throws<ArgumentException>(() => Client.Search(bad, true));
                Assert.Equal("packageIdOrName", ex.ParamName);
            }

            var foundBuiltin = Client.Search("com.unity.modules.audio@9.9.9", offlineMode: true);
            Assert.True(foundBuiltin.IsCompleted);
            Assert.Equal(StatusCode.Success, foundBuiltin.Status);
            Assert.Null(foundBuiltin.Error);
            Assert.Single(foundBuiltin.Result);
            Assert.Equal("com.unity.modules.audio@9.9.9", foundBuiltin.PackageIdOrName);

            foreach (var missing in new[] { "com.probe.pm", "com.probe.local", "com.probe.does.not.exist" })
            {
                var req = Client.Search(missing, offlineMode: true);
                Assert.True(req.IsCompleted);
                Assert.Equal(StatusCode.Failure, req.Status);
                Assert.NotNull(req.Error);
                Assert.Equal(ErrorCode.NotFound, req.Error.errorCode);
                Assert.Null(req.Result);
            }

            SearchRequest onlineMiss = null;
            var logs = CaptureLogs(() => onlineMiss = Client.Search("com.probe.does.not.exist", offlineMode: false), "Client.Search(online)");
            Assert.Equal(StatusCode.Failure, onlineMiss.Status);
            Assert.Equal(ErrorCode.NotFound, onlineMiss.Error.errorCode);
            Assert.Contains(logs, m => m.Contains("Client.Search(online)"));

            // Online mode and unmeasured Registry source use distinct Stubs.Ignored keys so neither suppresses the other.
            SearchRequest onlineBuiltin = null;
            SearchRequest regOffline = null;
            var combinedLogs = CaptureLogs(() =>
            {
                onlineBuiltin = Client.Search("com.unity.modules.audio");
                regOffline = Client.Search("com.probe.registry", offlineMode: true);
            }, "Client.Search(online)", "Client.Search(Registry)");
            Assert.Equal(StatusCode.Success, onlineBuiltin.Status);
            Assert.Equal(StatusCode.Success, regOffline.Status);
            Assert.Single(regOffline.Result);
            Assert.Contains(combinedLogs, m => m.Contains("Client.Search(online)"));
            Assert.Contains(combinedLogs, m => m.Contains("Client.Search(Registry)"));

            SearchRequest allReq = null;
            var allLogs = CaptureLogs(() => allReq = Client.SearchAll(offlineMode: true), "Client.SearchAll");
            Assert.Equal(StatusCode.Success, allReq.Status);
            Assert.Contains(allLogs, m => m.Contains("Client.SearchAll"));
        }

        [Fact]
        public void SerializedFieldsRoundTripThroughJsonUtility()
        {
            var req = Client.Remove("com.probe.not.installed");
            var reqJson = JsonUtility.ToJson(req);
            Assert.Contains("\"m_PackageIdOrName\":\"com.probe.not.installed\"", reqJson);
            var reqRt = JsonUtility.FromJson<RemoveRequest>(reqJson);
            Assert.Equal("com.probe.not.installed", reqRt.PackageIdOrName);

            var err = new Error(NativeErrorCode.Conflict, "boom");
            var errJson = JsonUtility.ToJson(err);
            var errRt = JsonUtility.FromJson<Error>(errJson);
            Assert.Equal(ErrorCode.Conflict, errRt.errorCode);
            Assert.Equal("boom", errRt.message);

            var vi = new VersionsInfo(new[] { "1.0.0", "2.0.0" }, new[] { "1.0.0" }, "1.0.0", new[] { "0.9.0" });
            var viJson = JsonUtility.ToJson(vi);
            var viRt = JsonUtility.FromJson<VersionsInfo>(viJson);
            Assert.Equal(new[] { "1.0.0", "2.0.0" }, viRt.all);
            Assert.Equal("2.0.0", viRt.latest);
            Assert.Equal("1.0.0", viRt.recommended);
            Assert.Equal(new[] { "0.9.0" }, viRt.deprecated);

            var col = new PackageCollection(Array.Empty<PackageInfo>(), new Error(NativeErrorCode.Conflict, "col-err"));
            var colJson = JsonUtility.ToJson(col);
            Assert.Contains("\"m_Error\"", colJson);
            Assert.Contains("\"col-err\"", colJson);

            var bi = PackageInfo.FindForPackageName("com.unity.modules.audio");
            var piJson = JsonUtility.ToJson(bi);
            Assert.Contains("\"m_Versions\"", piJson);
            Assert.Contains("\"m_Errors\"", piJson);
        }

        [Fact]
        public void ClientMutationMethodsValidateAndFailLoudlyWhenUnsupported()
        {
            foreach (var bad in new[] { null, "", "   " })
            {
                Assert.Equal("identifier", Assert.Throws<ArgumentException>(() => Client.Add(bad)).ParamName);
                Assert.Equal("packageName", Assert.Throws<ArgumentException>(() => Client.Remove(bad)).ParamName);
                Assert.Equal("packageName", Assert.Throws<ArgumentException>(() => Client.Embed(bad)).ParamName);
            }

            Assert.Null(Assert.Throws<ArgumentException>(() => Client.AddAndRemove(null, null)).ParamName);
            Assert.Null(Assert.Throws<ArgumentException>(() => Client.AddAndRemove(Array.Empty<string>(), Array.Empty<string>())).ParamName);
            Assert.Equal("packagesToAdd", Assert.Throws<ArgumentException>(() => Client.AddAndRemove(new[] { " " }, null)).ParamName);
            Assert.Equal("packagesToRemove", Assert.Throws<ArgumentException>(() => Client.AddAndRemove(null, new[] { "" })).ParamName);

            var embedMissing = Assert.Throws<InvalidOperationException>(() => Client.Embed("com.probe.not.registered"));
            Assert.Equal("Cannot embed package [com.probe.not.registered] because it is not registered in the Asset Database.", embedMissing.Message);

            // Uninstalled Remove and already-embedded Embed fail cleanly without Stubs.NotImplemented.
            RemoveRequest remMissing = null;
            EmbedRequest embedAlready = null;
            var measuredFailureLogs = CaptureLogs(() =>
            {
                remMissing = Client.Remove("com.probe.not.installed");
                embedAlready = Client.Embed("com.probe.pm");
            },
            "UnityEditor.PackageManager.Client.Remove",
            "UnityEditor.PackageManager.Client.Embed");
            Assert.Empty(measuredFailureLogs);
            Assert.True(remMissing.IsCompleted);
            Assert.Equal(StatusCode.Failure, remMissing.Status);
            Assert.Equal(ErrorCode.NotFound, remMissing.Error.errorCode);
            Assert.Equal("com.probe.not.installed", remMissing.PackageIdOrName);
            Assert.True(embedAlready.IsCompleted);
            Assert.Equal(StatusCode.Failure, embedAlready.Status);
            Assert.Equal(ErrorCode.Unknown, embedAlready.Error.errorCode);
            Assert.Null(embedAlready.Result);

            // Mutating Add, Remove (installed), AddAndRemove, Embed (non-embedded) log Stubs.NotImplemented and return completed Failure.
            AddRequest addReq = null;
            RemoveRequest remInstalled = null;
            AddAndRemoveRequest arReq = null;
            EmbedRequest embedBuiltin = null;
            var mutatingLogs = CaptureLogs(() =>
            {
                addReq = Client.Add("com.example.pkg");
                remInstalled = Client.Remove("com.probe.pm");
                arReq = Client.AddAndRemove(new[] { "com.example.pkg" });
                embedBuiltin = Client.Embed("com.unity.modules.audio");
            },
            "UnityEditor.PackageManager.Client.Add",
            "UnityEditor.PackageManager.Client.Remove",
            "UnityEditor.PackageManager.Client.AddAndRemove",
            "UnityEditor.PackageManager.Client.Embed");
            Assert.Contains(mutatingLogs, m => m.Contains("Client.Add"));
            Assert.Contains(mutatingLogs, m => m.Contains("Client.Remove"));
            Assert.Contains(mutatingLogs, m => m.Contains("Client.AddAndRemove"));
            Assert.Contains(mutatingLogs, m => m.Contains("Client.Embed"));

            Assert.True(addReq.IsCompleted);
            Assert.Equal(StatusCode.Failure, addReq.Status);
            Assert.NotNull(addReq.Error);
            Assert.Null(addReq.Result);

            Assert.True(remInstalled.IsCompleted);
            Assert.Equal(StatusCode.Failure, remInstalled.Status);
            Assert.NotNull(remInstalled.Error);
            Assert.Equal("com.probe.pm", remInstalled.PackageIdOrName);

            Assert.True(arReq.IsCompleted);
            Assert.Equal(StatusCode.Failure, arReq.Status);
            Assert.NotNull(arReq.Error);
            Assert.Null(arReq.Result);

            Assert.True(embedBuiltin.IsCompleted);
            Assert.Equal(StatusCode.Failure, embedBuiltin.Status);
            Assert.NotNull(embedBuiltin.Error);
            Assert.Null(embedBuiltin.Result);
        }
    }
}
