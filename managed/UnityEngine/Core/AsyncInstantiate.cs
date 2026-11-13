using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading;
using Unbound.Runtime;
using UnityEngine.SceneManagement;

namespace UnityEngine
{
    public partial struct InstantiateParameters
    {
        public Transform parent;
        public Scene scene;
        public bool worldSpace;
        public bool originalImmutable;
    }

    /// <summary>
    /// Object.InstantiateAsync. The copies are made when it is called (Unity copies on a background
    /// thread at once; changes to the original after the call do not reach them) and wait outside
    /// every scene until EarlyUpdate.UpdateAsyncInstantiate of a later frame integrates them: placed,
    /// woken (Awake/OnEnable), then <see cref="AsyncOperation.completed"/>. Measured:
    /// tests/projects/instantiateasync.
    /// </summary>
    public partial class AsyncInstantiateOperation : AsyncOperation
    {
        internal Object[] m_Result;
        internal readonly CancellationToken m_CancellationToken;
        internal bool m_Cancelled;
        /// <summary>Completed by its cancellation: awaiting it throws.</summary>
        internal bool m_WasCancelled;
        internal List<Instantiation.Prepared> m_Clones;
        internal Transform m_Parent;
        internal bool m_HasParent;
        internal SceneHandle m_Scene;
        internal bool m_WorldSpace;
        internal Vector3[] m_Positions;
        internal Quaternion[] m_Rotations;

        public AsyncInstantiateOperation() { }

        protected AsyncInstantiateOperation(IntPtr ptr, CancellationToken cancellationToken)
        {
            m_CancellationToken = cancellationToken;
        }

        internal static float IntegrationTimeMS { get; set; } = 2f;

        /// <summary>The instances, once done; null before and when cancelled.</summary>
        public Object[] Result => m_Result;

        /// <summary>True while the copies are ready but held by allowSceneActivation = false.</summary>
        public bool IsWaitingForSceneActivation() { RequireOperation(); return !m_IsDone && !m_Cancelled && !allowSceneActivation; }

        /// <summary>Integrates now, unless allowSceneActivation holds the operation.</summary>
        public void WaitForCompletion()
        {
            RequireOperation();
            if (!m_IsDone && allowSceneActivation) AsyncInstantiation.Integrate(this);
        }

        /// <summary>The operation completes with no result when it is next integrated.</summary>
        public void Cancel()
        {
            RequireOperation();
            if (!m_IsDone) m_Cancelled = true;
        }

        public static float GetIntegrationTimeMS() => IntegrationTimeMS;

        public static void SetIntegrationTimeMS(float integrationTimeMS)
        {
            if (integrationTimeMS <= 0f)
                throw new ArgumentOutOfRangeException(nameof(integrationTimeMS), "integrationTimeMS was out of range. Must be greater than zero.");
            // Unity keeps its budget for a value that is not a positive number (NaN).
            if (integrationTimeMS > 0f) IntegrationTimeMS = integrationTimeMS;
        }

        internal virtual Object[] CreateResultArray(int size) => new Object[size];
    }

    public partial class AsyncInstantiateOperation<T> : AsyncInstantiateOperation
    {
        internal AsyncInstantiateOperation(IntPtr ptr, CancellationToken cancellationToken) : base(ptr, cancellationToken) { }

        internal override Object[] CreateResultArray(int size) => (Object[])(object)new T[size];

        public new T[] Result => (T[])(object)m_Result;

        public Awaiter GetAwaiter() => new Awaiter(this);

        /// <summary>Awaits the operation's completion (Awaitable.FromAsyncOperation), then gives its
        /// result. As in Unity, an awaiter gives its result once (a second GetResult finds it detached),
        /// and awaiting a cancelled operation throws OperationCanceledException.</summary>
        public partial struct Awaiter : INotifyCompletion
        {
            sealed class State
            {
                internal int Consumed;
            }

            readonly AsyncInstantiateOperation<T> m_Op;
            readonly Awaitable m_Awaitable;
            readonly State m_State;

            public Awaiter(AsyncInstantiateOperation<T> op)
            {
                m_Op = op;
                m_Awaitable = Awaitable.FromAsyncOperation(op);
                m_State = new State();
            }

            public bool IsCompleted => m_Awaitable.IsCompleted;

            public void OnCompleted(Action continuation) => m_Awaitable.GetAwaiter().OnCompleted(continuation);

            public T[] GetResult()
            {
                if (Interlocked.Exchange(ref m_State.Consumed, 1) != 0)
                    throw new InvalidOperationException("Awaitable is in detached state");
                m_Awaitable.GetAwaiter().GetResult();
                if (m_Op.m_WasCancelled) throw new OperationCanceledException("Async operation canceled");
                return m_Op.Result;
            }
        }
    }

    public partial class Object
    {
        // The overloads fill in InstantiateParameters with worldSpace set, as Unity's do.
        static InstantiateParameters AsyncParameters(Transform parent) => new InstantiateParameters { parent = parent, worldSpace = true };

        public static AsyncInstantiateOperation<T> InstantiateAsync<T>(T original) where T : Object =>
            InstantiateAsync(original, 1, ReadOnlySpan<Vector3>.Empty, ReadOnlySpan<Quaternion>.Empty, AsyncParameters(null));

        public static AsyncInstantiateOperation<T> InstantiateAsync<T>(T original, Transform parent) where T : Object =>
            InstantiateAsync(original, 1, ReadOnlySpan<Vector3>.Empty, ReadOnlySpan<Quaternion>.Empty, AsyncParameters(parent));

        public static AsyncInstantiateOperation<T> InstantiateAsync<T>(T original, Vector3 position, Quaternion rotation) where T : Object =>
            InstantiateAsync(original, 1, new[] { position }, new[] { rotation }, AsyncParameters(null));

        public static AsyncInstantiateOperation<T> InstantiateAsync<T>(T original, Transform parent, Vector3 position, Quaternion rotation) where T : Object =>
            InstantiateAsync(original, 1, new[] { position }, new[] { rotation }, AsyncParameters(parent));

        public static AsyncInstantiateOperation<T> InstantiateAsync<T>(T original, int count) where T : Object =>
            InstantiateAsync(original, count, ReadOnlySpan<Vector3>.Empty, ReadOnlySpan<Quaternion>.Empty, AsyncParameters(null));

        public static AsyncInstantiateOperation<T> InstantiateAsync<T>(T original, int count, Transform parent) where T : Object =>
            InstantiateAsync(original, count, ReadOnlySpan<Vector3>.Empty, ReadOnlySpan<Quaternion>.Empty, AsyncParameters(parent));

        public static AsyncInstantiateOperation<T> InstantiateAsync<T>(T original, int count, Vector3 position, Quaternion rotation) where T : Object =>
            InstantiateAsync(original, count, new[] { position }, new[] { rotation }, AsyncParameters(null));

        public static AsyncInstantiateOperation<T> InstantiateAsync<T>(T original, int count, ReadOnlySpan<Vector3> positions, ReadOnlySpan<Quaternion> rotations) where T : Object =>
            InstantiateAsync(original, count, positions, rotations, AsyncParameters(null));

        public static AsyncInstantiateOperation<T> InstantiateAsync<T>(T original, int count, Transform parent, Vector3 position, Quaternion rotation) where T : Object =>
            InstantiateAsync(original, count, new[] { position }, new[] { rotation }, AsyncParameters(parent));

        public static AsyncInstantiateOperation<T> InstantiateAsync<T>(T original, int count, Transform parent, Vector3 position, Quaternion rotation, CancellationToken cancellationToken) where T : Object =>
            InstantiateAsync(original, count, new[] { position }, new[] { rotation }, AsyncParameters(parent), cancellationToken);

        public static AsyncInstantiateOperation<T> InstantiateAsync<T>(T original, int count, Transform parent, ReadOnlySpan<Vector3> positions, ReadOnlySpan<Quaternion> rotations) where T : Object =>
            InstantiateAsync(original, count, positions, rotations, AsyncParameters(parent));

        public static AsyncInstantiateOperation<T> InstantiateAsync<T>(T original, int count, Transform parent, ReadOnlySpan<Vector3> positions, ReadOnlySpan<Quaternion> rotations, CancellationToken cancellationToken) where T : Object =>
            InstantiateAsync(original, count, positions, rotations, AsyncParameters(parent), cancellationToken);

        public static AsyncInstantiateOperation<T> InstantiateAsync<T>(T original, InstantiateParameters parameters, CancellationToken cancellationToken = default) where T : Object =>
            InstantiateAsync(original, 1, ReadOnlySpan<Vector3>.Empty, ReadOnlySpan<Quaternion>.Empty, parameters, cancellationToken);

        public static AsyncInstantiateOperation<T> InstantiateAsync<T>(T original, int count, InstantiateParameters parameters, CancellationToken cancellationToken = default) where T : Object =>
            InstantiateAsync(original, count, ReadOnlySpan<Vector3>.Empty, ReadOnlySpan<Quaternion>.Empty, parameters, cancellationToken);

        public static AsyncInstantiateOperation<T> InstantiateAsync<T>(T original, Vector3 position, Quaternion rotation, InstantiateParameters parameters, CancellationToken cancellationToken = default) where T : Object =>
            InstantiateAsync(original, 1, new[] { position }, new[] { rotation }, parameters, cancellationToken);

        public static AsyncInstantiateOperation<T> InstantiateAsync<T>(T original, int count, Vector3 position, Quaternion rotation, InstantiateParameters parameters, CancellationToken cancellationToken = default) where T : Object =>
            InstantiateAsync(original, count, new[] { position }, new[] { rotation }, parameters, cancellationToken);

        public static AsyncInstantiateOperation<T> InstantiateAsync<T>(T original, int count, ReadOnlySpan<Vector3> positions, ReadOnlySpan<Quaternion> rotations,
            InstantiateParameters parameters, CancellationToken cancellationToken = default) where T : Object
        {
            // Only a null reference fails here; a destroyed original fails below, as in Unity.
            if ((object)original == null) throw new ArgumentException("The Object you want to instantiate is null.");
            if (count <= 0) throw new ArgumentException("Cannot call instantiate multiple with count less or equal to zero");
            if (original is ScriptableObject) throw new ArgumentException("Cannot call instantiate multiple for a ScriptableObject");
            ThrowIfDestroyedArgument(original, "original");
            var op = AsyncOperation.Bind(new AsyncInstantiateOperation<T>(IntPtr.Zero, cancellationToken), AsyncOperation.OperationCapabilities.SceneActivation);
            AsyncInstantiation.Start(op, original, count, positions, rotations, parameters);
            return op;
        }

        public static T Instantiate<T>(T original, InstantiateParameters parameters) where T : Object
        {
            CheckReferenceArgument(original);
            ThrowIfDestroyedArgument(original, "data");
            return (T)Instantiation.Clone(original, parameters, null, null);
        }

        public static T Instantiate<T>(T original, Vector3 position, Quaternion rotation, InstantiateParameters parameters) where T : Object
        {
            CheckReferenceArgument(original);
            ThrowIfDestroyedArgument(original, "data");
            return (T)Instantiation.Clone(original, parameters, position, rotation);
        }

        static void CheckReferenceArgument(object original)
        {
            if (original == null) throw new ArgumentException("The Object you want to instantiate is null.");
        }

        /// <summary>A destroyed object passed where Unity's engine needs a live one.</summary>
        static void ThrowIfDestroyedArgument(Object original, string parameter)
        {
            if (original.m_Destroyed)
                throw new MissingReferenceException(
                    $"The object of type '{original.GetType().FullName}' has been destroyed but you are still trying to access it.\n" +
                    $"Your script should either check if it is null or you should not destroy the object. Parameter name: {parameter}");
        }
    }
}

namespace Unbound.Runtime
{
    using UnityEngine;
    using Object = UnityEngine.Object;

    /// <summary>The pending InstantiateAsync operations, integrated in EarlyUpdate.UpdateAsyncInstantiate.</summary>
    internal static class AsyncInstantiation
    {
        static readonly List<AsyncInstantiateOperation> s_Pending = new List<AsyncInstantiateOperation>();

        static AsyncInstantiation()
        {
            PlayerLoopDriver.Register(typeof(UnityEngine.PlayerLoop.EarlyUpdate.UpdateAsyncInstantiate), Run);
        }

        internal static void Start(AsyncInstantiateOperation op, Object original, int count, ReadOnlySpan<Vector3> positions,
            ReadOnlySpan<Quaternion> rotations, InstantiateParameters parameters)
        {
            var sourceGo = original as GameObject ?? (original as Component)?.m_GameObject;
            if (sourceGo == null)
            {
                Debug.LogError("Cannot instantiate multiple objects that are not connected to GameObject, please use a loop over Instantiate");
                throw new NullReferenceException();
            }
            op.m_Parent = parameters.parent;
            op.m_HasParent = (object)parameters.parent != null;
            op.m_WorldSpace = parameters.worldSpace;
            op.m_Scene = Instantiation.TargetScene(parameters.parent, parameters.scene);
            op.m_Positions = positions.ToArray();
            op.m_Rotations = rotations.ToArray();
            op.m_Clones = new List<Instantiation.Prepared>(count);
            for (int i = 0; i < count; i++)
                op.m_Clones.Add(Instantiation.Prepare(original, sourceGo, op.m_Scene, " (Clone)"));
            // The copies are ready: what is left is the integration.
            op.m_Progress = 0.9f;
            op.m_OnAllowActivation = () => Integrate(op);
            s_Pending.Add(op);
        }

        /// <summary>Integrates the operations queued before this frame's integration, in order; once
        /// the budget is spent the rest wait for the next frame. An operation integrates whole.</summary>
        internal static void Run()
        {
            if (s_Pending.Count == 0) return;
            var start = Stopwatch.GetTimestamp();
            foreach (var op in s_Pending.ToArray())
            {
                if (op.m_IsDone || !op.allowSceneActivation) continue;
                Integrate(op);
                if (Stopwatch.GetElapsedTime(start).TotalMilliseconds >= AsyncInstantiateOperation.IntegrationTimeMS) break;
            }
        }

        internal static void Integrate(AsyncInstantiateOperation op)
        {
            if (op.m_IsDone || !s_Pending.Remove(op)) return;
            var clones = op.m_Clones;
            op.m_Clones = null;
            op.m_OnAllowActivation = null;
            // A parent destroyed before the integration cancels it too.
            if (op.m_Cancelled || op.m_CancellationToken.IsCancellationRequested || (op.m_HasParent && op.m_Parent == null))
            {
                foreach (var clone in clones) Instantiation.Discard(clone);
                op.m_WasCancelled = op.m_Cancelled || op.m_CancellationToken.IsCancellationRequested;
                op.Complete();
                return;
            }
            var parent = op.m_Parent;
            var scene = parent != null ? parent.m_GameObject.m_Scene : op.m_Scene;
            if (parent == null && SceneRegistry.Get(scene) == null) scene = Instantiation.TargetScene(null, default);
            var result = op.CreateResultArray(clones.Count);
            for (int i = 0; i < clones.Count; i++)
            {
                var clone = clones[i];
                Vector3? position = op.m_Positions.Length > 0 ? op.m_Positions[i % op.m_Positions.Length] : (Vector3?)null;
                Quaternion? rotation = op.m_Rotations.Length > 0 ? op.m_Rotations[i % op.m_Rotations.Length] : (Quaternion?)null;
                if (parent != null && op.m_WorldSpace)
                {
                    // World space keeps the world position and rotation but not the world scale.
                    position ??= clone.SourcePosition;
                    rotation ??= clone.SourceRotation;
                }
                Instantiation.Place(clone, parent, scene, false, !op.m_WorldSpace, position, rotation);
                result[i] = clone.Result;
            }
            Instantiation.Activate(clones);
            op.m_Result = result;
            op.Complete();
        }
    }
}
