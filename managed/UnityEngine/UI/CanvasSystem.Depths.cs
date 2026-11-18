using System.Collections.Generic;
using UnityEngine;

namespace Unbound.Runtime
{
    /// <summary>
    /// <c>CanvasRenderer.absoluteDepth</c> and <c>relativeDepth</c> (and so uGUI's
    /// <c>Graphic.depth</c>), as the Unity 6000.0.71f1 Editor numbers them in each frame's canvas
    /// update (tests/projects/uidepth). Scripting API 6000.0: absoluteDepth is the "Depth of the
    /// renderer relative to the root canvas", relativeDepth the depth "relative to the parent
    /// canvas".
    /// <list type="bullet">
    /// <item>Each canvas that sorts on its own (a root canvas, or a nested canvas with
    /// <c>overrideSorting</c>) numbers its renderers from 0: absoluteDepth counts the renderers of
    /// active objects in hierarchy order, its own object's first, through the nested canvases drawn
    /// with it, whatever their render mode, display, camera or culling. Disabled graphics, culled,
    /// transparent and empty renderers and renderers without a Graphic are numbered too.</item>
    /// <item>relativeDepth counts within the renderer's nearest canvas: its renderers, and one
    /// number for each nested canvas drawn with it that numbers at least one renderer (an
    /// overriding canvas inside it counts; an empty one, or one holding only empty, inactive or
    /// disabled content, takes none). A nested canvas's own renderer belongs to it.</item>
    /// <item>A disabled canvas numbers nothing below it (-1), except the enabled overriding
    /// canvases there, which number themselves.</item>
    /// <item>A renderer whose canvas changes (reparenting, a canvas enabled, disabled, added or
    /// removed above it, its object deactivated, <c>overrideSorting</c> changed) is -1 at once,
    /// and is numbered again in the next canvas update; a renderer that stays in its canvas
    /// (moved among its siblings, its own object deactivated) keeps its numbers until
    /// then.</item>
    /// </list>
    /// </summary>
    internal sealed partial class CanvasSystem
    {
        static List<CanvasRenderer> s_Numbered = new List<CanvasRenderer>();
        static List<CanvasRenderer> s_NumberedBefore = new List<CanvasRenderer>();
        static int s_DepthPass;

        /// <summary>Numbers every canvas renderer; those numbered last time and not now get -1.</summary>
        internal static void UpdateDepths()
        {
            s_DepthPass++;
            (s_Numbered, s_NumberedBefore) = (s_NumberedBefore, s_Numbered);
            s_Numbered.Clear();
            for (int i = 0; i < s_Canvases.Count; i++)
            {
                var canvas = s_Canvases[i];
                if (canvas == null || canvas.m_Destroyed || !canvas.IsActiveAndEnabledNoThrow) continue;
                if (canvas.m_OverrideSorting || !HasCanvasAbove(canvas.transform)) NumberSortingCanvas(canvas);
            }
            foreach (var cr in s_NumberedBefore)
                if (cr.m_DepthPass != s_DepthPass) ClearDepth(cr);
            s_NumberedBefore.Clear();
        }

        /// <summary>Numbers a canvas that sorts on its own; whether it numbered any renderer.</summary>
        static bool NumberSortingCanvas(Canvas canvas)
        {
            if (canvas.m_DepthPass == s_DepthPass) return canvas.m_NumbersRenderers;
            canvas.m_DepthPass = s_DepthPass;
            int absolute = 0;
            canvas.m_NumbersRenderers = NumberObject(canvas.transform, canvas, canvas, ref absolute);
            return canvas.m_NumbersRenderers;
        }

        /// <summary>Numbers the renderers of an object that belongs to <paramref name="canvas"/>
        /// (its own object or one below it) and of everything below it that belongs to it.</summary>
        static bool NumberObject(Transform t, Canvas root, Canvas canvas, ref int absolute)
        {
            int relative = 0;
            return NumberContent(t, root, canvas, ref absolute, ref relative);
        }

        static bool NumberContent(Transform t, Canvas root, Canvas canvas, ref int absolute, ref int relative)
        {
            bool any = false;
            foreach (var c in t.m_GameObject.m_Components)
            {
                if (!(c is CanvasRenderer cr) || cr.m_Destroyed) continue;
                if (cr.m_DepthPass != s_DepthPass) s_Numbered.Add(cr);
                cr.m_DepthPass = s_DepthPass;
                cr.m_AbsoluteDepth = absolute++;
                cr.m_RelativeDepth = relative++;
                cr.m_DepthRoot = root;
                cr.m_DepthCanvas = canvas;
                any = true;
            }
            foreach (var child in t.m_Children)
            {
                if (!child.m_GameObject.m_IsActive) continue;
                var nested = CanvasOf(child);
                if (nested == null) { any |= NumberContent(child, root, canvas, ref absolute, ref relative); continue; }
                // A disabled canvas numbers nothing; overriding canvases below it number themselves.
                if (!nested.m_Enabled) continue;
                if (nested.m_OverrideSorting) { any |= NumberSortingCanvas(nested); continue; }
                // A nested canvas drawn with this one: one number here when it numbers anything.
                if (NumberObject(child, root, nested, ref absolute)) { relative++; any = true; }
            }
            return any;
        }

        /// <summary>Whether a canvas lies above an active canvas's object (its ancestors are active).</summary>
        static bool HasCanvasAbove(Transform t)
        {
            for (var p = t.m_Parent; p != null; p = p.m_Parent)
                if (CanvasOf(p) != null) return true;
            return false;
        }

        static Canvas CanvasOf(Transform t)
        {
            foreach (var c in t.m_GameObject.m_Components)
                if (c is Canvas canvas && !canvas.m_Destroyed) return canvas;
            return null;
        }

        static void ClearDepth(CanvasRenderer cr)
        {
            cr.m_AbsoluteDepth = cr.m_RelativeDepth = -1;
            cr.m_DepthRoot = cr.m_DepthCanvas = null;
        }

        /// <summary>After the canvases above <paramref name="t"/> may have changed (it was
        /// reparented, or its own canvas was enabled, disabled, deactivated, removed or
        /// changed overrideSorting): every renderer at or below it whose canvas or sorting canvas is no
        /// longer the one that numbered it, or is disabled or inactive, gets -1 at once.</summary>
        internal static void ReleaseDepths(Transform t)
        {
            if (s_Numbered.Count == 0 || t == null) return;
            Canvas nearest = null, root = null;
            bool dead = false;
            for (var p = t.m_Parent; p != null && (root == null || !root.m_OverrideSorting); p = p.m_Parent)
            {
                var c = CanvasOf(p);
                if (c == null) continue;
                if (nearest == null) nearest = c;
                if (!c.IsActiveAndEnabledNoThrow) dead = true;
                root = c;
            }
            Release(t, nearest, root, dead);
        }

        static void Release(Transform t, Canvas nearest, Canvas root, bool dead)
        {
            var own = CanvasOf(t);
            if (own != null)
            {
                nearest = own;
                if (own.m_OverrideSorting || root == null) { root = own; dead = !own.IsActiveAndEnabledNoThrow; }
                else if (!own.IsActiveAndEnabledNoThrow) dead = true;
            }
            foreach (var c in t.m_GameObject.m_Components)
                if (c is CanvasRenderer cr && cr.m_AbsoluteDepth >= 0 &&
                    (dead || cr.m_DepthCanvas != nearest || cr.m_DepthRoot != root))
                    ClearDepth(cr);
            foreach (var child in t.m_Children) Release(child, nearest, root, dead);
        }
    }
}
