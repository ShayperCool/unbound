using System;
using System.Collections.Generic;
using UnityEngine;

namespace Unbound.Runtime
{
    internal static partial class FrameBuilder
    {
        sealed class CameraTemporaries : IDisposable
        {
            readonly List<(RenderTexture Texture, long Lease)> m_Owned = new List<(RenderTexture, long)>();

            internal RenderTexture Own(RenderTexture texture)
            {
                m_Owned.Add((texture, texture.m_PoolLease));
                return texture;
            }

            public void Dispose()
            {
                for (int i = m_Owned.Count - 1; i >= 0; i--)
                {
                    var (texture, lease) = m_Owned[i];
                    // An effect may release and reacquire the same pooled object for another
                    // owner. Only this camera's still-live acquisition belongs to its cleanup.
                    if (texture != null && !texture.m_PooledFree && texture.m_PoolLease == lease)
                        RenderTexture.ReleaseTemporary(texture);
                }
            }
        }
    }
}
