using System;
using UnityEditor;
using Xunit;

namespace UnityEngine.Tests
{
    public class ImporterMetaOrdered : MonoBehaviour { }

    [DefaultExecutionOrder(100)]
    public class ImporterAttributeOrdered : MonoBehaviour { }

    /// <summary>MonoImporter.GetExecutionOrder as the Unity 6000.0.71f1 Editor answers it
    /// (tests/projects/monoimporter): the .meta order only, not the attribute.</summary>
    public sealed class MonoImporterTests
    {
        [Fact]
        public void TheOrderIsTheMetaFilesAndNotTheAttributes()
        {
            var meta = MonoScript.ForPluginClass(typeof(ImporterMetaOrdered), "0123456789abcdef0123456789abcdef", 1);
            var attribute = MonoScript.ForPluginClass(typeof(ImporterAttributeOrdered), "0123456789abcdef0123456789abcdee", 2);
            Unbound.Runtime.ScriptMessages.ExecutionOrders[typeof(ImporterMetaOrdered)] = 50;
            try
            {
                Assert.Equal(50, MonoImporter.GetExecutionOrder(meta));
                Assert.Equal(0, MonoImporter.GetExecutionOrder(attribute));
                Assert.Throws<ArgumentNullException>(() => MonoImporter.GetExecutionOrder(null));
            }
            finally { Unbound.Runtime.ScriptMessages.ExecutionOrders.TryRemove(typeof(ImporterMetaOrdered), out _); }
        }
    }
}
