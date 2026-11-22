using System;
using UnityEditor;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>MonoImporter.GetExecutionOrder as the Unity 6000.0.71f1 Editor answers it
    /// (tests/projects/monoimporter, which also measures the orders of project scripts).</summary>
    public sealed class MonoImporterTests
    {
        [Fact]
        public void ANullScriptThrows() =>
            Assert.Throws<ArgumentNullException>(() => MonoImporter.GetExecutionOrder(null));
    }
}
