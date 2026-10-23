using System;
using System.Collections;
using System.Threading;
using Unbound.Runtime;

namespace UnityEngine
{
    public partial class MonoBehaviour : Behaviour
    {
        internal bool m_DidAwake;
        internal bool m_DidStart;
        CancellationTokenSource m_CancellationTokenSource;

        public bool useGUILayout { get; set; } = true;

        public bool runInEditMode { get; set; }

        public bool didStart => m_DidStart;

        public bool didAwake => m_DidAwake;

        /// <summary>Cancelled when the behaviour is destroyed.</summary>
        public CancellationToken destroyCancellationToken
        {
            get
            {
                if (m_Destroyed)
                    throw new MissingReferenceException($"DestroyCancellation token should be called atleast once before destroying the monobehaviour object");
                m_CancellationTokenSource ??= new CancellationTokenSource();
                return m_CancellationTokenSource.Token;
            }
        }

        internal void CancelDestroyToken()
        {
            if (m_CancellationTokenSource != null)
            {
                try { m_CancellationTokenSource.Cancel(); }
                catch (Exception e) { Debug.LogException(e); }
            }
        }

        // ------------------------------------------------------------------ coroutines

        public Coroutine StartCoroutine(IEnumerator routine)
        {
            if (routine == null)
                throw new NullReferenceException("routine is null");
            if (!isActiveAndEnabled)
            {
                Debug.LogError($"Coroutine couldn't be started because the the game object '{name}' is inactive!", this);
                return null;
            }
            return Coroutines.Start(this, routine, null);
        }

        public Coroutine StartCoroutine(string methodName, [UnityEngine.Internal.DefaultValue("null")] object value)
        {
            if (string.IsNullOrEmpty(methodName))
                throw new NullReferenceException("methodName is null or empty");
            if (!isActiveAndEnabled)
            {
                Debug.LogError($"Coroutine '{methodName}' couldn't be started because the the game object '{name}' is inactive!", this);
                return null;
            }
            var routine = Messaging.InvokeForEnumerator(this, methodName, value);
            if (routine == null)
            {
                Debug.LogError($"Coroutine '{methodName}' couldn't be started!", this);
                return null;
            }
            return Coroutines.Start(this, routine, methodName);
        }

        public Coroutine StartCoroutine(string methodName) => StartCoroutine(methodName, null);

        [Obsolete("StartCoroutine_Auto has been deprecated. Use StartCoroutine instead (UnityUpgradable) -> StartCoroutine([mscorlib] System.Collections.IEnumerator)", false)]
        public Coroutine StartCoroutine_Auto(IEnumerator routine) => StartCoroutine(routine);

        public void StopCoroutine(IEnumerator routine)
        {
            if (routine == null)
                throw new NullReferenceException("routine is null");
            Coroutines.Stop(this, routine);
        }

        public void StopCoroutine(Coroutine routine)
        {
            if (routine == null)
                throw new NullReferenceException("routine is null");
            Coroutines.Stop(this, routine);
        }

        public void StopCoroutine(string methodName) => Coroutines.StopByName(this, methodName);

        public void StopAllCoroutines() => Coroutines.StopAll(this);

        // ------------------------------------------------------------------ Invoke

        public void Invoke(string methodName, float time) => Invokes.Schedule(this, methodName, time, -1f);

        public void InvokeRepeating(string methodName, float time, float repeatRate)
        {
            if (repeatRate <= 1e-05f && repeatRate != 0f)
                throw new UnityException("Invoke repeat rate has to be larger than 0.00001F");
            Invokes.Schedule(this, methodName, time, repeatRate);
        }

        public void CancelInvoke() => Invokes.Cancel(this, null);

        public void CancelInvoke(string methodName) => Invokes.Cancel(this, methodName);

        public bool IsInvoking() => Invokes.IsInvoking(this, null);

        public bool IsInvoking(string methodName) => Invokes.IsInvoking(this, methodName);

        public static void print(object message) => Debug.Log(message);
    }
}
