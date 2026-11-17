using UnityEngine.TextCore.Text;
using UnityEngine.UIElements.Experimental;

namespace UnityEngine.UIElements
{
    /// <summary>
    /// The pointer handling of a text element's links, set up each time its text is generated:
    /// <c>&lt;link&gt;</c> tags get Pointer*LinkTagEvents (down and up over a link, over when the
    /// pointer comes onto one, move while it stays on it, out when it leaves it or the element);
    /// <c>&lt;a href&gt;</c> links open their address when the pointer is released over them (a
    /// well-formed absolute URI) and, on Editor panels, show the link cursor while hovered. Matches
    /// the Unity 6000.0.71f1 Editor (tests/projects/uitklinks).
    /// </summary>
    internal partial class TextEventHandler
    {
        readonly TextElement m_TextElement;
        EventCallback<PointerDownEvent> m_LinkTagOnPointerDown;
        EventCallback<PointerUpEvent> m_LinkTagOnPointerUp;
        EventCallback<PointerMoveEvent> m_LinkTagOnPointerMove;
        EventCallback<PointerOutEvent> m_LinkTagOnPointerOut;
        EventCallback<PointerUpEvent> m_ATagOnPointerUp;
        EventCallback<PointerMoveEvent> m_ATagOnPointerMove;
        EventCallback<PointerOverEvent> m_ATagOnPointerOver;
        EventCallback<PointerOutEvent> m_ATagOnPointerOut;
        bool m_LinkCallbacksRegistered, m_ATagCallbacksRegistered, m_ATagCursorCallbacksRegistered;
        internal bool isOverridingCursor;
        internal int currentLinkIDHash = -1;
        internal bool hasLinkTag;
        internal bool hasATag;

        TextInfo textInfo => m_TextElement.uitkTextHandle.textInfo;

        public TextEventHandler(TextElement textElement) => m_TextElement = textElement;

        public void OnDestroy()
        {
            if (m_LinkCallbacksRegistered) UnregisterLinkCallbacks();
            if (m_ATagCallbacksRegistered) UnregisterATagCallbacks();
        }

        bool HasAllocatedLinkCallbacks() => m_LinkTagOnPointerDown != null;

        void AllocateLinkCallbacks()
        {
            if (HasAllocatedLinkCallbacks()) return;
            m_LinkTagOnPointerDown = LinkTagOnPointerDown;
            m_LinkTagOnPointerUp = LinkTagOnPointerUp;
            m_LinkTagOnPointerMove = LinkTagOnPointerMove;
            m_LinkTagOnPointerOut = LinkTagOnPointerOut;
        }

        bool HasAllocatedATagCallbacks() => m_ATagOnPointerUp != null;

        void AllocateATagCallbacks()
        {
            if (HasAllocatedATagCallbacks()) return;
            m_ATagOnPointerUp = ATagOnPointerUp;
            m_ATagOnPointerMove = ATagOnPointerMove;
            m_ATagOnPointerOver = ATagOnPointerOver;
            m_ATagOnPointerOut = ATagOnPointerOut;
        }

        void UnregisterLinkCallbacks()
        {
            m_TextElement.UnregisterCallback(m_LinkTagOnPointerDown, TrickleDown.TrickleDown);
            m_TextElement.UnregisterCallback(m_LinkTagOnPointerUp, TrickleDown.TrickleDown);
            m_TextElement.UnregisterCallback(m_LinkTagOnPointerMove, TrickleDown.TrickleDown);
            m_TextElement.UnregisterCallback(m_LinkTagOnPointerOut, TrickleDown.TrickleDown);
            m_LinkCallbacksRegistered = false;
        }

        void UnregisterATagCallbacks()
        {
            m_TextElement.UnregisterCallback(m_ATagOnPointerUp, TrickleDown.TrickleDown);
            if (m_ATagCursorCallbacksRegistered)
            {
                m_TextElement.UnregisterCallback(m_ATagOnPointerMove, TrickleDown.TrickleDown);
                m_TextElement.UnregisterCallback(m_ATagOnPointerOver, TrickleDown.TrickleDown);
                m_TextElement.UnregisterCallback(m_ATagOnPointerOut, TrickleDown.TrickleDown);
                m_ATagCursorCallbacksRegistered = false;
            }
            m_ATagCallbacksRegistered = false;
        }

        /// <summary>The link under a pointer event's position (in the element's content box), or -1.</summary>
        int LinkAt<T>(PointerEventBase<T> evt) where T : PointerEventBase<T>, new()
        {
            var position = evt.localPosition - new Vector3(m_TextElement.contentRect.min.x, m_TextElement.contentRect.min.y);
            return m_TextElement.uitkTextHandle.FindIntersectingLink(position);
        }

        /// <summary>The <c>&lt;link&gt;</c> (not <c>&lt;a&gt;</c>) under the event with an id, or false.</summary>
        bool TryGetLinkTag<T>(PointerEventBase<T> evt, out LinkInfo link) where T : PointerEventBase<T>, new()
        {
            link = default;
            int index = LinkAt(evt);
            if (index < 0) return false;
            link = textInfo.linkInfo[index];
            return link.hashCode != (int)MarkupTag.HREF && link.linkId != null && link.linkIdLength > 0;
        }

        void Send(EventBase evt)
        {
            using (evt)
            {
                evt.elementTarget = m_TextElement;
                m_TextElement.SendEvent(evt);
            }
        }

        void ATagOnPointerUp(PointerUpEvent pue)
        {
            int index = LinkAt(pue);
            if (index < 0) return;
            var link = textInfo.linkInfo[index];
            if (link.hashCode != (int)MarkupTag.HREF || link.linkId == null || link.linkIdLength <= 0) return;
            var url = link.GetLinkId();
            if (System.Uri.IsWellFormedUriString(url, System.UriKind.Absolute)) Application.OpenURL(url);
        }

        void ATagOnPointerOver(PointerOverEvent _) => isOverridingCursor = false;

        /// <summary>Editor panels only: the link cursor while over an <c>&lt;a&gt;</c> link, the
        /// element's own cursor again once off it (also onto a <c>&lt;link&gt;</c>).</summary>
        void ATagOnPointerMove(PointerMoveEvent pme)
        {
            int index = LinkAt(pme);
            var cursorManager = (m_TextElement.panel as BaseVisualElementPanel)?.cursorManager;
            if (index >= 0 && textInfo.linkInfo[index].hashCode == (int)MarkupTag.HREF)
            {
                if (isOverridingCursor) return;
                isOverridingCursor = true;
                cursorManager?.SetCursor(new Cursor { defaultCursorId = k_LinkCursorId });
                return;
            }
            // Off the <a> links (no link, or a <link>).
            if (!isOverridingCursor) return;
            cursorManager?.SetCursor(m_TextElement.computedValues.cursor);
            isOverridingCursor = false;
        }

        /// <summary>The `link` cursor keyword's id.</summary>
        const int k_LinkCursorId = 4;

        void ATagOnPointerOut(PointerOutEvent evt) => isOverridingCursor = false;

        void LinkTagOnPointerDown(PointerDownEvent pde)
        {
            if (TryGetLinkTag(pde, out var link))
                Send(PointerDownLinkTagEvent.GetPooled(pde, link.GetLinkId(), link.GetLinkText(textInfo)));
        }

        void LinkTagOnPointerUp(PointerUpEvent pue)
        {
            if (TryGetLinkTag(pue, out var link))
                Send(PointerUpLinkTagEvent.GetPooled(pue, link.GetLinkId(), link.GetLinkText(textInfo)));
        }

        void LinkTagOnPointerMove(PointerMoveEvent pme)
        {
            int index = LinkAt(pme);
            if (index >= 0)
            {
                var link = textInfo.linkInfo[index];
                if (link.hashCode != (int)MarkupTag.HREF)
                {
                    if (currentLinkIDHash == -1)
                    {
                        currentLinkIDHash = link.hashCode;
                        Send(PointerOverLinkTagEvent.GetPooled(pme, link.GetLinkId(), link.GetLinkText(textInfo)));
                        return;
                    }
                    if (currentLinkIDHash == link.hashCode)
                    {
                        Send(PointerMoveLinkTagEvent.GetPooled(pme, link.GetLinkId(), link.GetLinkText(textInfo)));
                        return;
                    }
                }
            }
            // Off the link it was over: no link, an <a> link or another link (which gets its
            // over event on the next move).
            if (currentLinkIDHash == -1) return;
            currentLinkIDHash = -1;
            Send(PointerOutLinkTagEvent.GetPooled(pme, string.Empty));
        }

        void LinkTagOnPointerOut(PointerOutEvent poe)
        {
            if (currentLinkIDHash == -1) return;
            Send(PointerOutLinkTagEvent.GetPooled(poe, string.Empty));
            currentLinkIDHash = -1;
        }

        /// <summary>After the text is generated: registers the link callbacks the text needs (in a
        /// panel), and removes those it no longer needs.</summary>
        internal void HandleLinkAndATagCallbacks()
        {
            var panel = m_TextElement?.panel;
            if (panel == null) return;
            if (hasLinkTag && !m_LinkCallbacksRegistered)
            {
                AllocateLinkCallbacks();
                m_TextElement.RegisterCallback(m_LinkTagOnPointerDown, TrickleDown.TrickleDown);
                m_TextElement.RegisterCallback(m_LinkTagOnPointerUp, TrickleDown.TrickleDown);
                m_TextElement.RegisterCallback(m_LinkTagOnPointerMove, TrickleDown.TrickleDown);
                m_TextElement.RegisterCallback(m_LinkTagOnPointerOut, TrickleDown.TrickleDown);
                m_LinkCallbacksRegistered = true;
            }
            else if (!hasLinkTag && m_LinkCallbacksRegistered)
            {
                UnregisterLinkCallbacks();
            }
            if (hasATag && !m_ATagCallbacksRegistered)
            {
                AllocateATagCallbacks();
                m_TextElement.RegisterCallback(m_ATagOnPointerUp, TrickleDown.TrickleDown);
                if (panel.contextType == ContextType.Editor)
                {
                    m_TextElement.RegisterCallback(m_ATagOnPointerMove, TrickleDown.TrickleDown);
                    m_TextElement.RegisterCallback(m_ATagOnPointerOver, TrickleDown.TrickleDown);
                    m_TextElement.RegisterCallback(m_ATagOnPointerOut, TrickleDown.TrickleDown);
                    m_ATagCursorCallbacksRegistered = true;
                }
                m_ATagCallbacksRegistered = true;
            }
            else if (!hasATag && m_ATagCallbacksRegistered)
            {
                UnregisterATagCallbacks();
            }
        }

        /// <summary>Whether the generated text has a <c>&lt;link&gt;</c>; its text information is then
        /// kept for the pointer handling.</summary>
        internal void HandleLinkTag()
        {
            for (int i = 0; i < textInfo.linkCount; i++)
            {
                if (textInfo.linkInfo[i].hashCode == (int)MarkupTag.HREF) continue;
                hasLinkTag = true;
                m_TextElement.uitkTextHandle.AddTextInfoToPermanentCache();
                return;
            }
            if (!hasLinkTag) return;
            hasLinkTag = false;
            m_TextElement.uitkTextHandle.RemoveTextInfoFromPermanentCache();
        }

        /// <summary>Whether the generated text has an <c>&lt;a href&gt;</c>, as HandleLinkTag.</summary>
        internal void HandleATag()
        {
            for (int i = 0; i < textInfo.linkCount; i++)
            {
                if (textInfo.linkInfo[i].hashCode != (int)MarkupTag.HREF) continue;
                hasATag = true;
                m_TextElement.uitkTextHandle.AddTextInfoToPermanentCache();
                return;
            }
            if (!hasATag) return;
            hasATag = false;
            m_TextElement.uitkTextHandle.RemoveTextInfoFromPermanentCache();
        }
    }
}
