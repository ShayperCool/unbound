using System;
using System.Collections.Generic;
using UnityEngine.Events;

namespace UnityEngine
{
    /// <summary>The place of an <see cref="Application.onBeforeRender"/> callback: lower orders run
    /// first; callbacks without the attribute have order 0.</summary>
    [AttributeUsage(AttributeTargets.Method)]
    public partial class BeforeRenderOrderAttribute : Attribute
    {
        public BeforeRenderOrderAttribute(int order) => this.order = order;

        public int order { get; private set; }
    }

    /// <summary>
    /// The callbacks of <see cref="Application.onBeforeRender"/>, in blocks by their method's
    /// <see cref="BeforeRenderOrderAttribute"/> order (ascending), a block's callbacks in the order
    /// they were added. A run walks the blocks by index, so a callback that adds one of a lower order
    /// moves the running block along and runs it again, and one of a higher order runs in the same
    /// run; an exception ends the run. Matches the Unity 6000.0.71f1 Editor
    /// (tests/projects/coreremainder).
    /// </summary>
    internal static partial class BeforeRenderHelper
    {
        struct OrderBlock
        {
            internal int order;
            internal UnityAction callback;
        }

        static readonly List<OrderBlock> s_OrderBlocks = new List<OrderBlock>();

        static int GetUpdateOrder(UnityAction callback)
        {
            // A null callback is a call through null, as in Unity.
            if (callback == null) throw Unbound.Runtime.MonoExceptions.NullReference();
            return callback.Method.GetCustomAttributes(typeof(BeforeRenderOrderAttribute), true) is { Length: > 0 } attributes
                && attributes[0] is BeforeRenderOrderAttribute attribute ? attribute.order : 0;
        }

        public static void RegisterCallback(UnityAction callback)
        {
            int order = GetUpdateOrder(callback);
            lock (s_OrderBlocks)
            {
                int i = 0;
                for (; i < s_OrderBlocks.Count && order >= s_OrderBlocks[i].order; i++)
                {
                    if (s_OrderBlocks[i].order != order) continue;
                    var block = s_OrderBlocks[i];
                    block.callback += callback;
                    s_OrderBlocks[i] = block;
                    return;
                }
                s_OrderBlocks.Insert(i, new OrderBlock { order = order, callback = callback });
            }
        }

        public static void UnregisterCallback(UnityAction callback)
        {
            int order = GetUpdateOrder(callback);
            lock (s_OrderBlocks)
            {
                for (int i = 0; i < s_OrderBlocks.Count && order >= s_OrderBlocks[i].order; i++)
                {
                    if (s_OrderBlocks[i].order != order) continue;
                    var block = s_OrderBlocks[i];
                    block.callback -= callback;
                    s_OrderBlocks[i] = block;
                    if (block.callback == null) s_OrderBlocks.RemoveAt(i);
                    return;
                }
            }
        }

        public static void Invoke()
        {
            lock (s_OrderBlocks)
            {
                for (int i = 0; i < s_OrderBlocks.Count; i++)
                    s_OrderBlocks[i].callback?.Invoke();
            }
        }
    }
}
