using System;
using System.Collections.Generic;
using UnityEngine;
using Xunit;

namespace Unbound.Tests
{
    public class MacPreferencesTests
    {
        /// <summary>Ints, floats and strings come back as written, and a key left out is removed
        /// (macOS only: the store is the user's preferences domain).</summary>
        [Fact]
        public void ValuesRoundTripThroughAPreferencesDomain()
        {
            if (!OperatingSystem.IsMacOS()) return;
            var app = $"unity.UnboundTests.Prefs{Environment.ProcessId}";
            try
            {
                MacPreferences.Write(app, new Dictionary<string, object> { ["i"] = -7, ["f"] = 0.1f, ["s"] = "héllo", ["gone"] = 1 });
                MacPreferences.Write(app, new Dictionary<string, object> { ["i"] = -7, ["f"] = 0.1f, ["s"] = "héllo" });
                var back = MacPreferences.Read(app);
                Assert.Equal(3, back.Count);
                Assert.Equal(-7, back["i"]);
                Assert.Equal(0.1f, back["f"]);
                Assert.Equal("héllo", back["s"]);
            }
            finally
            {
                MacPreferences.Write(app, new Dictionary<string, object>());
            }
        }
    }
}
