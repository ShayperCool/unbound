using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Runtime.Loader;
using Xunit;
using Xunit.Abstractions;
using Xunit.Sdk;

namespace UnityEngine.Tests
{
    /// <summary>Collectible load contexts in the tests. When an unloaded context is collected,
    /// CoreCLR flushes its process-wide type-cast cache: the next cast that goes through the cache
    /// allocates a new table (3120 B at its initial size), and later casts grow it back to its
    /// earlier size (98352 B at its largest), each inside whatever allocation window is open
    /// (<see cref="CollectibleContextTests"/>). An unload that only calls Unload completes at some
    /// later collection, in a later test. A test that unloads a context therefore runs in this
    /// collection, which runs after all others (<see cref="CollectibleContextsLast"/>), and unloads
    /// it with <see cref="Use"/>, which returns only once the context is collected and the cache
    /// flushed.</summary>
    [CollectionDefinition(Name, DisableParallelization = true)]
    public sealed class CollectibleContexts
    {
        internal const string Name = "Collectible contexts";
        // Two collections with their finalizers collect an unloaded context (measured: always 2).
        const int MaxCollections = 20;

        /// <summary>Runs <paramref name="use"/> on a new collectible context, unloads the context and
        /// waits until it is collected; then rethrows what <paramref name="use"/> threw. use must not
        /// keep anything from the context (an assembly, a type, an instance) past its return.</summary>
        internal static void Use(string name, Action<AssemblyLoadContext> use)
        {
            var (context, failure) = Unloaded(name, use);
            for (int i = 0; context.IsAlive && i < MaxCollections; i++)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
            }
            // A context left alive would finish its unload, and flush the cache, in a later test.
            if (context.IsAlive)
                throw new InvalidOperationException($"the load context '{name}' is alive after {MaxCollections} collections"
                    + (failure != null ? " (use threw; see the inner exception)" : ""), failure?.SourceException);
            failure?.Throw();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        static (WeakReference, ExceptionDispatchInfo) Unloaded(string name, Action<AssemblyLoadContext> use)
        {
            var context = new AssemblyLoadContext(name, isCollectible: true);
            ExceptionDispatchInfo failure = null;
            try { use(context); }
            catch (Exception e) { failure = ExceptionDispatchInfo.Capture(e); }
            context.Unload();
            return (new WeakReference(context), failure);
        }
    }

    /// <summary>xunit's default order of the test collections, with <see cref="CollectibleContexts"/>
    /// moved to the end: no allocation window runs after its flushes.</summary>
    public sealed class CollectibleContextsLast : ITestCollectionOrderer
    {
        readonly DefaultTestCollectionOrderer m_Default = new DefaultTestCollectionOrderer();

        public IEnumerable<ITestCollection> OrderTestCollections(IEnumerable<ITestCollection> testCollections)
        {
            var ordered = m_Default.OrderTestCollections(testCollections).ToList();
            return ordered.Where(c => !Unloads(c)).Concat(ordered.Where(Unloads));
        }

        internal static bool Unloads(ITestCollection collection) => collection.DisplayName == CollectibleContexts.Name;
    }

    [Collection(CollectibleContexts.Name)]
    public sealed class CollectibleContextTests
    {
        static readonly object[] s_Subjects = { new List<int>(), new int[3], "x", new Dictionary<int, int>() };

        /// <summary>Casts through the cache; the bytes this thread allocated.</summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        static long CastWindow()
        {
            AllocationMeasurement.EmptyAllocationContext();
            long before = GC.GetAllocatedBytesForCurrentThread();
            int found = 0;
            for (int i = 0; i < 10; i++)
                foreach (var subject in s_Subjects)
                {
                    if (subject is IReadOnlyList<int>) found++;
                    if (subject is IEnumerable<int>) found++;
                    if (subject is ICollection<KeyValuePair<int, int>>) found++;
                }
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            GC.KeepAlive(found);
            return allocated;
        }

        static void LoadThisAssembly(AssemblyLoadContext context)
        {
            using var image = System.IO.File.OpenRead(typeof(CollectibleContextTests).Assembly.Location);
            context.LoadFromStream(image);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        static WeakReference UnloadedOnly()
        {
            var context = new AssemblyLoadContext("unloaded only", isCollectible: true);
            LoadThisAssembly(context);
            context.Unload();
            return new WeakReference(context);
        }

        /// <summary>The control: an unload alone leaves the context alive, and once a later
        /// collection takes it, the next cast allocates (the flushed cache's new table).</summary>
        [Fact]
        public void ACollectedContextMakesTheNextCastAllocate()
        {
            CastWindow();
            Assert.Equal(0, CastWindow());
            bool allocated = false;
            // Another thread may cast first after the flush and allocate the table itself.
            for (int attempt = 0; attempt < 3 && !allocated; attempt++)
            {
                var context = UnloadedOnly();
                Assert.True(context.IsAlive, "an unload alone does not collect the context");
                while (context.IsAlive)
                {
                    GC.Collect();
                    GC.WaitForPendingFinalizers();
                }
                allocated = CastWindow() > 0;
            }
            Assert.True(allocated, "no cast allocated after three collected contexts");
        }

        /// <summary>Casts until two windows in a row allocate nothing (the cache regrown).</summary>
        static void WarmCasts()
        {
            for (int i = 0, quiet = 0; quiet < 2; i++)
            {
                quiet = CastWindow() == 0 ? quiet + 1 : 0;
                Assert.True(i < 50, "the casts keep allocating");
            }
        }

        /// <summary>Use returns with the context collected and the cache flushed: once warm again,
        /// casts allocate nothing however many collections follow (no flush is left pending, as
        /// after an unload alone). An exception from use reaches the caller.</summary>
        [Fact]
        public void UseFinishesTheUnloadBeforeItReturns()
        {
            WeakReference seen = null;
            CollectibleContexts.Use("use", context =>
            {
                seen = new WeakReference(context);
                LoadThisAssembly(context);
            });
            Assert.False(seen.IsAlive);
            WarmCasts();
            for (int i = 0; i < 3; i++)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                Assert.Equal(0, CastWindow());
            }
            Assert.Throws<System.IO.FileNotFoundException>(() => CollectibleContexts.Use("missing",
                context => context.LoadFromAssemblyPath("/nonexistent/unbound-test.dll")));
        }

        /// <summary>The collection that unloads runs after every other collection.</summary>
        [Fact]
        public void UnloadingCollectionsRunLast()
        {
            var assembly = new TestAssembly(new ReflectionAssemblyInfo(typeof(CollectibleContextTests).Assembly));
            ITestCollection Named(string name) => new TestCollection(assembly, null, name);
            var order = new CollectibleContextsLast()
                .OrderTestCollections(new[] { Named("b"), Named(CollectibleContexts.Name), Named("a"), Named("c") })
                .Select(c => c.DisplayName).ToList();
            Assert.Equal(4, order.Count);
            Assert.Equal(CollectibleContexts.Name, order[^1]);
        }
    }
}
