using System;
using UnityEditor;
using Xunit;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;
using PackageSource = UnityEditor.PackageManager.PackageSource;

namespace UnityEngine.Tests
{
    public sealed class VirtualPlayerActivationTests : IDisposable
    {
        public VirtualPlayerActivationTests()
        {
            VirtualPlayerActivation.Configure(null);
            PackageInfo.Register(Array.Empty<PackageInfo>());
        }

        public void Dispose()
        {
            VirtualPlayerActivation.Configure(null);
            PackageInfo.Register(Array.Empty<PackageInfo>());
        }

        [Fact]
        public void NoRequestedPlayersDoesNothing()
        {
            VirtualPlayerActivation.Configure(Array.Empty<string>());
            VirtualPlayerActivation.ActivateRequested();
        }

        [Fact]
        public void MissingPackageFailsLoudly()
        {
            VirtualPlayerActivation.Configure(new[] { "2" });
            var ex = Assert.Throws<InvalidOperationException>(() => VirtualPlayerActivation.ActivateRequested());
            Assert.Contains("com.unity.multiplayer.playmode", ex.Message);
            Assert.Contains("not installed", ex.Message);
        }

        [Fact]
        public void UnknownPackageVersionFailsLoudly()
        {
            PackageInfo.Register(new[]
            {
                PackageInfo.Create(VirtualPlayerActivation.PackageName, "9.9.9", PackageSource.Registry,
                    "/tmp/fake-mppm", "MPPM", "", "", "", Array.Empty<string>(), true),
            });
            VirtualPlayerActivation.Configure(new[] { "2" });
            var ex = Assert.Throws<InvalidOperationException>(() => VirtualPlayerActivation.ActivateRequested());
            Assert.Contains("9.9.9", ex.Message);
            Assert.Contains("does not support", ex.Message);
        }

        [Fact]
        public void ResolveTargetMatchesByIndexAndNameAndRejectsMainOrInvalid()
        {
            var p1 = new object();
            var p2 = new object();
            var p3 = new object();
            var entries = new[]
            {
                new VirtualPlayerActivation.PlayerEntry(1, "Main Editor", true, p1),
                new VirtualPlayerActivation.PlayerEntry(2, "Player 2", false, p2),
                new VirtualPlayerActivation.PlayerEntry(3, "Player 3", false, p3),
            };

            Assert.Same(p2, VirtualPlayerActivation.ResolveTarget("2", entries).Instance);
            Assert.Same(p3, VirtualPlayerActivation.ResolveTarget("player 3", entries).Instance);
            Assert.Same(p2, VirtualPlayerActivation.ResolveTarget("  Player 2 ", entries).Instance);

            Assert.Throws<InvalidOperationException>(() => VirtualPlayerActivation.ResolveTarget("", entries));
            Assert.Throws<InvalidOperationException>(() => VirtualPlayerActivation.ResolveTarget("1", entries));
            Assert.Throws<InvalidOperationException>(() => VirtualPlayerActivation.ResolveTarget("Main Editor", entries));
            Assert.Throws<InvalidOperationException>(() => VirtualPlayerActivation.ResolveTarget("5", entries));
            Assert.Throws<InvalidOperationException>(() => VirtualPlayerActivation.ResolveTarget("0", entries));
            Assert.Throws<InvalidOperationException>(() => VirtualPlayerActivation.ResolveTarget("Ghost", entries));
        }
    }
}
