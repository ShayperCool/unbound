using System;
using System.Collections.Generic;

namespace Unbound
{
    /// <summary>Delegates game code handed to native code: kept alive, as Mono keeps their thunks
    /// (see Unbound.Host.NativeCallbacks, which calls this from rewritten P/Invokes).</summary>
    public static class NativeCallbackRoots
    {
        static readonly HashSet<Delegate> s_Roots = new HashSet<Delegate>();

        public static void Keep(Delegate callback)
        {
            if (callback == null) return;
            lock (s_Roots) s_Roots.Add(callback);
        }
    }
}
