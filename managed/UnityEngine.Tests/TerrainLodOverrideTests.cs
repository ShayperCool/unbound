using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>TerrainData's level-of-detail overrides, as the Unity 6000.0.71f1 Editor applies them
    /// (tests/projects/terrainoverrides lod.*).</summary>
    public sealed class TerrainLodOverrideTests
    {
        sealed class ErrorCapture : ILogHandler
        {
            internal readonly List<string> Errors = new List<string>();
            public void LogException(Exception exception, Object context) { }
            public void LogFormat(LogType logType, Object context, string format, params object[] args)
            {
                if (logType == LogType.Error) Errors.Add(string.Format(format, args));
            }
        }

        static TerrainData Data()
        {
            var data = new TerrainData { heightmapResolution = 33, size = new Vector3(100, 50, 100) };
            var heights = new float[33, 33];
            for (int z = 0; z < 33; z++)
                for (int x = 0; x < 33; x++)
                    heights[z, x] = 0.25f + 0.25f * Mathf.Sin(x * 0.3f);
            data.SetHeights(0, 0, heights);
            return data;
        }

        [Fact]
        public void AnOverrideOfAnotherLengthIsAnErrorAndChangesNothing()
        {
            var data = Data();
            var capture = new ErrorCapture();
            var previous = Debug.unityLogger.logHandler;
            Debug.unityLogger.logHandler = capture;
            try
            {
                var bounds = data.bounds;
                data.OverrideMaximumHeightError(new[] { 0.5f, 0.25f });
                data.OverrideMaximumHeightError(null);
                data.OverrideMinMaxPatchHeights(new[] { new PatchExtents { min = 0f, max = 1f } });
                Assert.Equal(new[]
                {
                    "Override maximum height error array size 2 not equal to patchCount of 5 bytes",
                    "Override maximum height error array size 0 not equal to patchCount of 5 bytes",
                    "Override patch heights array size 2 not equal to patchCount of 10 bytes",
                }, capture.Errors);
                Assert.Equal(bounds, data.bounds);
            }
            finally
            {
                Debug.unityLogger.logHandler = previous;
            }
        }

        [Fact]
        public void TheGettersKeepTheHeightmapsValuesAndTheBoundsFollowTheRootOverride()
        {
            var data = Data();
            var errors = data.GetMaximumHeightError();
            var extents = data.GetPatchMinMaxHeights();
            data.OverrideMaximumHeightError(Enumerable.Repeat(0.3f, errors.Length).ToArray());
            data.OverrideMinMaxPatchHeights(Enumerable.Range(0, extents.Length)
                .Select(i => new PatchExtents { min = 0.1f * i, max = 0.2f + 0.1f * i }).ToArray());
            Assert.Equal(errors, data.GetMaximumHeightError());
            Assert.Equal(extents.Select(e => (e.min, e.max)), data.GetPatchMinMaxHeights().Select(e => (e.min, e.max)));
            // The root, the last of 5 patches: 0.4..0.6 of 50.
            Assert.Equal(25f, data.bounds.center.y, 4);
            Assert.Equal(10f, data.bounds.size.y, 4);

            // Later heights do not undo the override.
            data.SetHeights(0, 0, new float[,] { { 0.9f } });
            Assert.Equal(10f, data.bounds.size.y, 4);
            Assert.Equal(0.9f, data.GetPatchMinMaxHeights()[^1].max, 3);

            // A new heightmap resolution drops it (Unbound's choice, unmeasured).
            data.heightmapResolution = 65;
            Assert.Equal(0f, data.bounds.size.y, 3);
        }
    }
}
