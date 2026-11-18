// The engine's state is global (object registry, lifecycle lists, frame clock, physics worlds):
// test classes run one after another, as one game would drive it.
[assembly: Xunit.CollectionBehavior(DisableTestParallelization = true)]
// Collections that unload load contexts run after all others (UnityEngine.Tests.CollectibleContexts).
[assembly: Xunit.TestCollectionOrderer("UnityEngine.Tests.CollectibleContextsLast", "UnityEngine.Tests")]
