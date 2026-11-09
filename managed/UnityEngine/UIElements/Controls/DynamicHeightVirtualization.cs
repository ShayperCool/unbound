using System;
using System.Collections.Generic;

namespace UnityEngine.UIElements
{
    /// <summary>
    /// Virtualization for rows of their own heights (CollectionVirtualizationMethod.DynamicHeight).
    /// Rows not laid out yet count with a default height (the smallest row height seen, else an inline
    /// fixed item height, else 22); each laid-out row registers its height, and running sums of the
    /// registered heights give the content height above any index. Scrolling keeps an anchor (the
    /// first visible index and the offset into it) and cycles rows from one end to the other; rows
    /// past the viewport are hidden. Newly bound rows wait for their layout, then the scroll view is
    /// updated once all of them reported. Matches the Unity 6000.0.71f1 Editor,
    /// tests/projects/uitkcollections (dynamic section: bind/unbind order while scrolling, scroll
    /// offsets, content padding and height, ScrollToItem, refresh and rebuild).
    /// </summary>
    internal partial class DynamicHeightVirtualizationController<T> : VerticalVirtualizationController<T> where T : ReusableCollectionItem, new()
    {
        readonly struct ContentHeightCacheInfo
        {
            public readonly float sum;
            public readonly int count;

            public ContentHeightCacheInfo(float sum, int count)
            {
                this.sum = sum;
                this.count = count;
            }
        }

        enum VirtualizationChange
        {
            None,
            Resize,
            Scroll,
            ForcedScroll,
        }

        enum ScrollDirection
        {
            Idle,
            Up,
            Down,
        }

        // Row height assumed before any row was measured (and with no inline fixed item height).
        const float k_DefaultItemHeight = 22f;
        // Extra space below a row scrolled to from above, so it ends inside the viewport.
        const float k_ScrollToItemMargin = 22f;
        // A resize this long after the previous one fills at once instead of waiting for the scheduler.
        const float k_ForceRefreshIntervalInMilliseconds = 100f;

        int m_HighestCachedIndex = -1;
        readonly Dictionary<int, float> m_ItemHeightCache = new Dictionary<int, float>(32);
        readonly Dictionary<int, ContentHeightCacheInfo> m_ContentHeightCache = new Dictionary<int, ContentHeightCacheInfo>(32);
        readonly HashSet<int> m_WaitingCache = new HashSet<int>();
        readonly List<int> m_OutOfBounds = new List<int>();
        int? m_ScrolledToItemIndex;
        int m_ForcedFirstVisibleItem = -1;
        int m_ForcedLastVisibleItem = -1;
        bool m_StickToBottom;
        VirtualizationChange m_LastChange;
        ScrollDirection m_ScrollDirection;
        Vector2 m_DelayedScrollOffset = Vector2.negativeInfinity;
        float m_AccumulatedHeight;
        float m_MinimumItemHeight = -1f;
        readonly Action m_FillCallback;
        readonly Action m_ScrollResetCallback;
        readonly Action<ReusableCollectionItem> m_GeometryChangedCallback;
        readonly Predicate<int> m_IndexOutOfBoundsPredicate;
        IVisualElementScheduledItem m_ScheduledItem;
        IVisualElementScheduledItem m_ScrollResetScheduledItem;
        IVisualElementScheduledItem m_RefreshScrollOffsetScheduledItem;
        bool m_FillExecuted;
        long m_TimeSinceFillScheduledMs;

        internal IReadOnlyDictionary<int, float> itemHeightCache => m_ItemHeightCache;

        float defaultExpectedHeight
        {
            get
            {
                if (m_MinimumItemHeight > 0f) return m_MinimumItemHeight;
                if (m_CollectionView.m_ItemHeightIsInline && m_CollectionView.fixedItemHeight > 0f) return m_CollectionView.fixedItemHeight;
                return k_DefaultItemHeight;
            }
        }

        float contentPadding
        {
            get => serializedData.contentPadding;
            set
            {
                m_CollectionView.scrollView.contentContainer.style.paddingTop = value;
                serializedData.contentPadding = value;
            }
        }

        float contentHeight
        {
            get => serializedData.contentHeight;
            set
            {
                m_CollectionView.scrollView.contentContainer.style.height = value;
                serializedData.contentHeight = value;
            }
        }

        int anchoredIndex
        {
            get => serializedData.anchoredItemIndex;
            set => serializedData.anchoredItemIndex = value;
        }

        float anchorOffset
        {
            get => serializedData.anchorOffset;
            set => serializedData.anchorOffset = value;
        }

        float viewportMaxOffset => m_ScrollView.scrollOffset.y + m_ScrollView.contentViewport.layout.height;

        protected override bool alwaysRebindOnRefresh => false;

        public DynamicHeightVirtualizationController(BaseVerticalCollectionView collectionView) : base(collectionView)
        {
            m_FillCallback = Fill;
            m_GeometryChangedCallback = OnRecycledItemGeometryChanged;
            m_IndexOutOfBoundsPredicate = IsIndexOutOfBounds;
            m_ScrollResetCallback = ResetScroll;
            collectionView.RegisterCallback<DetachFromPanelEvent>(OnDetachFromPanelEvent);
            collectionView.RegisterCallback<GeometryChangedEvent>(OnGeometryChangedEvent);
        }

        void OnGeometryChangedEvent(GeometryChangedEvent _)
        {
            if (!m_ScrolledToItemIndex.HasValue) return;
            if (ShouldDeferScrollToItem(m_ScrolledToItemIndex ?? -1)) ScheduleDeferredScrollToItem();
            m_ScrolledToItemIndex = null;
        }

        public override void Refresh(bool rebuild)
        {
            CleanItemHeightCache();
            int activeCount = m_ActiveItems.Count;
            bool removedWaiting = false;
            if (rebuild) m_WaitingCache.Clear();
            else removedWaiting = m_WaitingCache.RemoveWhere(m_IndexOutOfBoundsPredicate) > 0;
            base.Refresh(rebuild);
            m_ScrollDirection = ScrollDirection.Idle;
            m_LastChange = VirtualizationChange.None;
            if (!m_CollectionView.HasValidDataAndBindings()) return;
            if (removedWaiting || activeCount != m_ActiveItems.Count)
            {
                if (m_RefreshScrollOffsetScheduledItem == null) m_RefreshScrollOffsetScheduledItem = m_CollectionView.schedule.Execute(RefreshScrollOffset);
                else if (!m_RefreshScrollOffsetScheduledItem.isActive) m_RefreshScrollOffsetScheduledItem.Resume();
            }
            ScheduleFill();
        }

        public override void ScrollToItem(int index)
        {
            if (index < -1) return;
            if (visibleItemCount == 0)
            {
                m_ScrolledToItemIndex = index;
                return;
            }
            ShouldDeferScrollToItem(index);
            float contentLayoutHeight = m_ScrollView.contentContainer.layout.height;
            float viewportHeight = m_ScrollView.contentViewport.layout.height;
            var before = m_ScrollView.scrollOffset;
            if (index == -1)
            {
                m_ForcedLastVisibleItem = itemsCount - 1;
                m_ForcedFirstVisibleItem = -1;
                m_StickToBottom = true;
                m_ScrollView.scrollOffset = new Vector2(0f, viewportHeight >= contentLayoutHeight ? 0f : contentLayoutHeight);
            }
            else if (firstVisibleIndex >= index)
            {
                var offset = new Vector2(0f, GetContentHeightForIndex(index - 1));
                if (offset == m_ScrollView.scrollOffset) return;
                m_ForcedFirstVisibleItem = index;
                m_ForcedLastVisibleItem = -1;
                m_ScrollView.scrollOffset = offset;
            }
            else
            {
                float bottom = GetContentHeightForIndex(index);
                if (float.IsNaN(viewportHeight) || bottom < contentPadding + viewportHeight) return;
                m_ForcedLastVisibleItem = index;
                m_ForcedFirstVisibleItem = -1;
                m_ScrollView.scrollOffset = new Vector2(0f, bottom - viewportHeight + k_ScrollToItemMargin);
            }
            if (before == m_ScrollView.scrollOffset) OnScrollUpdate();
        }

        public override void Resize(Vector2 size)
        {
            contentHeight = Mathf.Max(GetExpectedContentHeight(), contentHeight);
            float viewportHeight = m_ScrollView.contentViewport.layout.height;
            float maxOffset = Mathf.Max(0f, contentHeight - viewportHeight);
            float offset = Mathf.Min(serializedData.scrollOffset.y, maxOffset);
            m_ScrollView.verticalScroller.slider.SetHighValueWithoutNotify(maxOffset);
            m_ScrollView.verticalScroller.slider.SetValueWithoutNotify(offset);
            serializedData.scrollOffset.y = m_ScrollView.verticalScroller.value;

            int fitting = Mathf.CeilToInt(m_CollectionView.ResolveItemHeight(size.y) / defaultExpectedHeight);
            if (fitting <= 0) return;
            int wanted = Mathf.Min(fitting + k_ExtraVisibleItems, itemsCount);
            if (m_ActiveItems.Count != wanted)
            {
                int count = m_ActiveItems.Count;
                if (count > wanted)
                {
                    for (int i = count - wanted; i > 0; i--) ReleaseItem(m_ActiveItems.Count - 1);
                }
                else
                {
                    int first = firstVisibleIndex >= 0 ? firstVisibleIndex : 0;
                    for (int i = 0; i < wanted - count; i++)
                    {
                        int index = i + first + count;
                        var item = GetOrMakeItemAtIndex();
                        if (IsIndexOutOfBounds(index))
                        {
                            HideItem(m_ActiveItems.Count - 1);
                            continue;
                        }
                        Setup(item, index);
                        MarkWaitingForLayout(item);
                    }
                }
            }

            long now = DateTime.UtcNow.Ticks / TimeSpan.TicksPerMillisecond;
            if (now - m_TimeSinceFillScheduledMs > k_ForceRefreshIntervalInMilliseconds && m_TimeSinceFillScheduledMs != 0 && !m_FillExecuted)
            {
                Fill();
                ResetScroll();
                m_TimeSinceFillScheduledMs = 0;
            }
            else
            {
                if (m_TimeSinceFillScheduledMs == 0) m_TimeSinceFillScheduledMs = now;
                ScheduleFill();
                ScheduleScrollDirectionReset();
                m_FillExecuted = false;
            }
            m_LastChange = VirtualizationChange.Resize;
        }

        public override void OnScroll(Vector2 scrollOffset)
        {
            if (m_DelayedScrollOffset == scrollOffset) return;
            m_DelayedScrollOffset = scrollOffset;
            if (m_ForcedFirstVisibleItem != -1 || m_ForcedLastVisibleItem != -1)
            {
                OnScrollUpdate();
                m_LastChange = VirtualizationChange.ForcedScroll;
                return;
            }
            if (m_LastChange == VirtualizationChange.Resize || m_LastChange == VirtualizationChange.ForcedScroll)
            {
                float maxOffset = Mathf.Max(0f, contentHeight - m_ScrollView.contentViewport.layout.height);
                float offset = Mathf.Min(m_ScrollView.scrollOffset.y, maxOffset);
                m_ScrollView.verticalScroller.slider.SetHighValueWithoutNotify(maxOffset);
                m_ScrollView.verticalScroller.slider.SetValueWithoutNotify(offset);
            }
            else
            {
                ScheduleScroll();
            }
        }

        void OnDetachFromPanelEvent(DetachFromPanelEvent evt)
        {
            if (m_ScheduledItem != null && m_ScheduledItem.isActive)
            {
                m_ScheduledItem.Pause();
                m_ScheduledItem = null;
            }
            if (m_ScrollResetScheduledItem != null && m_ScrollResetScheduledItem.isActive)
            {
                m_ScrollResetScheduledItem.Pause();
                m_ScrollResetScheduledItem = null;
            }
        }

        protected override void OnScrollUpdate()
        {
            var offset = float.IsNegativeInfinity(m_DelayedScrollOffset.y) ? m_ScrollView.scrollOffset : m_DelayedScrollOffset;
            if (float.IsNaN(m_ScrollView.contentViewport.layout.height) || float.IsNaN(offset.y)) return;
            m_LastChange = VirtualizationChange.Scroll;
            contentHeight = Mathf.Max(GetExpectedContentHeight(), contentHeight);
            m_ScrollDirection = offset.y < m_ScrollView.scrollOffset.y ? ScrollDirection.Up : ScrollDirection.Down;
            float maxOffset = Mathf.Max(0f, contentHeight - m_ScrollView.contentViewport.layout.height);
            if (offset.y <= 0f) m_ForcedFirstVisibleItem = 0;
            m_StickToBottom = maxOffset > 0f && Math.Abs(offset.y - m_ScrollView.verticalScroller.highValue) < float.Epsilon;
            m_ScrollView.SetScrollOffsetWithoutNotify(offset);
            serializedData.scrollOffset = m_ScrollView.scrollOffset;
            int first = m_ForcedFirstVisibleItem != -1 ? m_ForcedFirstVisibleItem : GetFirstVisibleItem(m_ScrollView.scrollOffset.y);
            contentPadding = GetContentHeightForIndex(first - 1);
            m_ForcedFirstVisibleItem = -1;
            if (first != firstVisibleIndex) CycleItems(first);
            else Fill();
            ScheduleScrollDirectionReset();
            m_DelayedScrollOffset = Vector2.negativeInfinity;
        }

        /// <summary>Makes <paramref name="firstIndex"/> the first visible index: rows move from one end
        /// of the active items to the other, are rebound in order and hidden past the viewport.</summary>
        void CycleItems(int firstIndex)
        {
            if (firstIndex == firstVisibleIndex) return;
            var previousFirst = firstVisibleItem;
            contentPadding = GetContentHeightForIndex(firstIndex - 1);
            firstVisibleIndex = firstIndex;
            if (m_ActiveItems.Count > 0)
            {
                if (previousFirst != null && m_ActiveItems.Count > Mathf.Abs(firstVisibleIndex - previousFirst.index))
                {
                    if (firstVisibleIndex < previousFirst.index)
                    {
                        int moved = previousFirst.index - firstVisibleIndex;
                        for (int i = 0; i < moved; i++)
                        {
                            var last = m_ActiveItems[m_ActiveItems.Count - 1];
                            m_ScrollInsertionList.Insert(0, last);
                            m_ActiveItems.RemoveAt(m_ActiveItems.Count - 1);
                            last.rootElement.SendToBack();
                        }
                        m_ActiveItems.InsertRange(0, m_ScrollInsertionList);
                        m_ScrollInsertionList.Clear();
                    }
                    else
                    {
                        int moved = 0;
                        int lastIndex = -1;
                        while (firstVisibleIndex > m_ActiveItems[moved].index && lastIndex < m_ActiveItems[moved].index)
                        {
                            var item = m_ActiveItems[moved];
                            lastIndex = item.index;
                            m_ScrollInsertionList.Add(item);
                            moved++;
                            item.rootElement.BringToFront();
                        }
                        m_ActiveItems.RemoveRange(0, moved);
                        m_ActiveItems.AddRange(m_ScrollInsertionList);
                        m_ScrollInsertionList.Clear();
                    }
                }
                float top = contentPadding;
                for (int i = 0; i < m_ActiveItems.Count; i++)
                {
                    var item = m_ActiveItems[i];
                    int index = firstVisibleIndex + i;
                    int previousIndex = item.index;
                    bool shown = item.rootElement.style.display == DisplayStyle.Flex;
                    m_WaitingCache.Remove(previousIndex);
                    if (IsIndexOutOfBounds(index))
                    {
                        HideItem(i);
                        continue;
                    }
                    Setup(item, index);
                    if (top > viewportMaxOffset) HideItem(i);
                    else if (index != previousIndex || !shown) MarkWaitingForLayout(item);
                    top += GetExpectedItemHeight(index);
                }
            }
            if (m_LastChange != VirtualizationChange.Resize) UpdateAnchor();
            ScheduleFill();
        }

        bool NeedsFill()
        {
            if (m_LastChange != VirtualizationChange.None || anchoredIndex < 0) return false;
            int lastShown = lastVisibleItem?.index ?? -1;
            float top = contentPadding;
            if (top > m_ScrollView.scrollOffset.y) return true;
            for (int i = firstVisibleIndex; i < itemsCount; i++)
            {
                if (top > viewportMaxOffset) break;
                if (top == viewportMaxOffset && !m_StickToBottom) break;
                top += GetExpectedItemHeight(i);
                if (i > lastShown) return true;
            }
            return false;
        }

        /// <summary>Binds rows down to the viewport's bottom (and, when the content starts below the
        /// scroll offset, rows above the first), then applies the scroll position once no row waits
        /// for its layout.</summary>
        void Fill()
        {
            if (!m_CollectionView.HasValidDataAndBindings()) return;
            m_FillExecuted = true;
            if (m_ActiveItems.Count == 0)
            {
                contentHeight = 0f;
                contentPadding = 0f;
                return;
            }
            if (anchoredIndex < 0 || firstVisibleIndex < 0) return;
            if (contentPadding > contentHeight)
            {
                OnScrollUpdate();
                return;
            }
            float padding = contentPadding;
            float bottom = contentPadding;
            int used = 0;
            for (int i = firstVisibleIndex; i < itemsCount; i++)
            {
                if (bottom > viewportMaxOffset) break;
                if (bottom == viewportMaxOffset && !m_StickToBottom) break;
                bottom += GetExpectedItemHeight(i);
                var item = m_ActiveItems[used++];
                if (item.index != i || item.rootElement.style.display == DisplayStyle.None)
                {
                    Setup(item, i);
                    MarkWaitingForLayout(item);
                }
                if (used >= m_ActiveItems.Count) break;
            }
            if (firstVisibleIndex > 0 && contentPadding > m_ScrollView.scrollOffset.y)
            {
                for (int i = m_ActiveItems.Count - 1; i >= used && firstVisibleIndex != 0; i--)
                {
                    var item = m_ActiveItems[i];
                    m_ScrollInsertionList.Insert(0, item);
                    m_ActiveItems.RemoveAt(m_ActiveItems.Count - 1);
                    item.rootElement.SendToBack();
                    int index = --firstVisibleIndex;
                    Setup(item, index);
                    MarkWaitingForLayout(item);
                    padding -= GetExpectedItemHeight(index);
                    if (padding < m_ScrollView.scrollOffset.y) break;
                }
                m_ActiveItems.InsertRange(0, m_ScrollInsertionList);
                m_ScrollInsertionList.Clear();
            }
            contentPadding = padding;
            contentHeight = GetExpectedContentHeight();
            if (m_LastChange != VirtualizationChange.Resize) UpdateAnchor();
            if (m_WaitingCache.Count == 0)
            {
                m_ScrollView.scrollOffset = serializedData.scrollOffset;
                ResetScroll();
                ApplyScrollViewUpdate(true);
            }
        }

        void UpdateScrollViewContainer(float previousHeight, float newHeight)
        {
            if (m_StickToBottom) return;
            if (m_ForcedLastVisibleItem >= 0)
            {
                float bottom = GetContentHeightForIndex(m_ForcedLastVisibleItem);
                m_ScrollView.SetScrollOffsetWithoutNotify(new Vector2(m_ScrollView.scrollOffset.x, bottom + k_ScrollToItemMargin - m_ScrollView.contentViewport.layout.height));
            }
            else if (m_ScrollDirection == ScrollDirection.Up)
            {
                // Rows growing above the viewport push the content down: keep what is in view.
                m_ScrollView.SetScrollOffsetWithoutNotify(new Vector2(m_ScrollView.scrollOffset.x, m_ScrollView.scrollOffset.y + (newHeight - previousHeight)));
            }
        }

        /// <summary>Recomputes the content height and padding from the anchor, sets the scroller's
        /// range and value, then (unless only the dimensions changed) fills or hides the rows that
        /// scrolled out.</summary>
        void ApplyScrollViewUpdate(bool dimensionsOnly = false)
        {
            float intoFirst = m_ScrollView.scrollOffset.y - contentPadding;
            if (anchoredIndex >= 0)
            {
                if (firstVisibleIndex != anchoredIndex)
                {
                    CycleItems(anchoredIndex);
                    ScheduleFill();
                }
                firstVisibleIndex = anchoredIndex;
                intoFirst = anchorOffset;
            }
            float expected = contentHeight = GetExpectedContentHeight();
            contentPadding = GetContentHeightForIndex(firstVisibleIndex - 1);
            float maxOffset = Mathf.Max(0f, m_ScrollView.RoundToPanelPixelSize(expected - m_ScrollView.contentViewport.layout.height));
            float offset = Mathf.Min(contentPadding + intoFirst, maxOffset);
            if (m_StickToBottom && maxOffset > 0f)
            {
                offset = maxOffset;
            }
            else if (m_ForcedLastVisibleItem != -1)
            {
                float bottom = GetContentHeightForIndex(m_ForcedLastVisibleItem);
                offset = Mathf.Clamp(bottom + k_ScrollToItemMargin - m_ScrollView.contentViewport.layout.height, 0f, maxOffset);
                m_ForcedLastVisibleItem = -1;
            }
            m_ScrollView.verticalScroller.slider.SetHighValueWithoutNotify(maxOffset);
            m_ScrollView.verticalScroller.slider.SetValueWithoutNotify(offset);
            serializedData.scrollOffset.y = m_ScrollView.verticalScroller.slider.value;
            if (dimensionsOnly || m_LastChange == VirtualizationChange.Resize)
            {
                ScheduleScrollDirectionReset();
                return;
            }
            if (NeedsFill())
            {
                Fill();
            }
            else
            {
                float top = contentPadding;
                int previousFirst = firstVisibleIndex;
                int moved = 0;
                for (int i = 0; i < m_ActiveItems.Count; i++)
                {
                    var item = m_ActiveItems[i];
                    int index = item.index;
                    if (index < 0) break;
                    float height = GetExpectedItemHeight(index);
                    if (m_ActiveItems[i].rootElement.style.display == DisplayStyle.Flex)
                    {
                        if (top + height < m_ScrollView.scrollOffset.y)
                        {
                            item.rootElement.BringToFront();
                            HideItem(i);
                            m_ScrollInsertionList.Add(item);
                            moved++;
                            firstVisibleIndex++;
                        }
                        else if (top > viewportMaxOffset)
                        {
                            HideItem(i);
                        }
                    }
                    top += GetExpectedItemHeight(index);
                }
                m_ActiveItems.RemoveRange(0, moved);
                m_ActiveItems.AddRange(m_ScrollInsertionList);
                m_ScrollInsertionList.Clear();
                if (firstVisibleIndex != previousFirst)
                {
                    contentPadding = GetContentHeightForIndex(firstVisibleIndex - 1);
                    UpdateAnchor();
                }
                ScheduleScrollDirectionReset();
            }
            ScheduleDeferredScrollToItem();
        }

        void UpdateAnchor()
        {
            anchoredIndex = firstVisibleIndex;
            anchorOffset = m_ScrollView.scrollOffset.y - contentPadding;
        }

        void ScheduleFill()
        {
            if (m_ScheduledItem == null)
            {
                m_ScheduledItem = m_CollectionView.schedule.Execute(m_FillCallback);
                return;
            }
            m_ScheduledItem.Pause();
            m_ScheduledItem.Resume();
        }

        void ScheduleScrollDirectionReset()
        {
            if (m_ScrollResetScheduledItem == null)
            {
                m_ScrollResetScheduledItem = m_CollectionView.schedule.Execute(m_ScrollResetCallback);
                return;
            }
            m_ScrollResetScheduledItem.Pause();
            m_ScrollResetScheduledItem.Resume();
        }

        void ResetScroll()
        {
            m_ScrollDirection = ScrollDirection.Idle;
            m_LastChange = VirtualizationChange.None;
            m_ScrollView.UpdateContentViewTransform();
            UpdateAnchor();
        }

        public override int GetIndexFromPosition(Vector2 position)
        {
            int index = 0;
            for (float top = 0f; top < position.y; top += GetExpectedItemHeight(index++)) { }
            return index - 1;
        }

        public override float GetExpectedItemHeight(int index)
        {
            int dragged = GetDraggedIndex();
            if (dragged >= 0 && index == dragged) return 0f;
            return m_ItemHeightCache.TryGetValue(index, out float height) ? height : defaultExpectedHeight;
        }

        int GetFirstVisibleItem(float offset)
        {
            if (offset <= 0f) return 0;
            int index = -1;
            while (offset > 0f)
            {
                index++;
                offset -= GetExpectedItemHeight(index);
            }
            return index;
        }

        public override float GetExpectedContentHeight() => m_AccumulatedHeight + (itemsCount - m_ItemHeightCache.Count) * defaultExpectedHeight;

        /// <summary>The content height from the top through row <paramref name="lastIndex"/>: the
        /// nearest registered running sum at or below it plus default heights for the rows after.</summary>
        float GetContentHeightForIndex(int lastIndex)
        {
            if (lastIndex < 0) return 0f;
            int dragged = GetDraggedIndex();
            if (m_HighestCachedIndex <= lastIndex && m_ContentHeightCache.TryGetValue(m_HighestCachedIndex, out var highest))
                return FromCached(lastIndex, highest);
            float sum = 0f;
            for (int i = lastIndex; i >= 0; i--)
            {
                if (m_ContentHeightCache.TryGetValue(i, out var cached)) return sum + FromCached(i, cached);
                sum += dragged == i ? 0f : defaultExpectedHeight;
            }
            return sum;

            float FromCached(int index, ContentHeightCacheInfo info)
            {
                float height = info.sum + (index - info.count + 1) * defaultExpectedHeight;
                if (dragged >= 0 && index >= dragged) height -= m_DraggedItem.rootElement.layout.height;
                return height;
            }
        }

        ContentHeightCacheInfo GetCachedContentHeight(int index)
        {
            for (; index >= 0; index--)
                if (m_ContentHeightCache.TryGetValue(index, out var info)) return info;
            return default;
        }

        void RegisterItemHeight(int index, float height)
        {
            if (height <= 0f) return;
            float resolved = m_CollectionView.ResolveItemHeight(height);
            if (m_ItemHeightCache.TryGetValue(index, out float previous)) m_AccumulatedHeight -= previous;
            m_AccumulatedHeight += resolved;
            m_ItemHeightCache[index] = resolved;
            if (index > m_HighestCachedIndex) m_HighestCachedIndex = index;
            bool isNew = previous == 0f;
            var before = GetCachedContentHeight(index - 1);
            m_ContentHeightCache[index] = new ContentHeightCacheInfo(before.sum + resolved, before.count + 1);
            foreach (var entry in m_ItemHeightCache)
            {
                if (entry.Key <= index) continue;
                var info = m_ContentHeightCache[entry.Key];
                m_ContentHeightCache[entry.Key] = new ContentHeightCacheInfo(info.sum - previous + resolved, isNew ? info.count + 1 : info.count);
            }
        }

        void UnregisterItemHeight(int index)
        {
            if (!m_ItemHeightCache.TryGetValue(index, out float height)) return;
            m_AccumulatedHeight -= height;
            m_ItemHeightCache.Remove(index);
            m_ContentHeightCache.Remove(index);
            int highest = -1;
            foreach (var entry in m_ItemHeightCache)
            {
                if (entry.Key > index)
                {
                    var info = m_ContentHeightCache[entry.Key];
                    m_ContentHeightCache[entry.Key] = new ContentHeightCacheInfo(info.sum - height, info.count - 1);
                }
                if (entry.Key > highest) highest = entry.Key;
            }
            m_HighestCachedIndex = highest;
        }

        void CleanItemHeightCache()
        {
            if (!IsIndexOutOfBounds(m_HighestCachedIndex)) return;
            m_OutOfBounds.Clear();
            foreach (int key in m_ItemHeightCache.Keys)
                if (IsIndexOutOfBounds(key)) m_OutOfBounds.Add(key);
            foreach (int key in m_OutOfBounds) UnregisterItemHeight(key);
            m_OutOfBounds.Clear();
            m_MinimumItemHeight = -1f;
        }

        void OnRecycledItemGeometryChanged(ReusableCollectionItem item)
        {
            if (item.index == -1 || item.isDragGhost || float.IsNaN(item.rootElement.layout.height) || item.rootElement.layout.height == 0f) return;
            if (UpdateRegisteredHeight(item)) ApplyScrollViewUpdate();
        }

        /// <summary>Registers a laid-out row's height (less its top padding, which an animated drag
        /// uses); a row smaller than the default lowers the default and resizes. True when no row
        /// waits for its layout any more.</summary>
        bool UpdateRegisteredHeight(ReusableCollectionItem item)
        {
            if (item.index == -1 || item.isDragGhost || float.IsNaN(item.rootElement.layout.height) || item.rootElement.layout.height == 0f) return false;
            if (item.rootElement.layout.height < defaultExpectedHeight)
            {
                m_MinimumItemHeight = item.rootElement.layout.height;
                Resize(m_ScrollView.layout.size);
            }
            float height = item.rootElement.layout.height - item.rootElement.resolvedStyle.paddingTop;
            bool known = m_ItemHeightCache.TryGetValue(item.index, out float registered);
            float expected = known ? GetExpectedItemHeight(item.index) : defaultExpectedHeight;
            if (m_WaitingCache.Count == 0)
            {
                if (height > expected)
                {
                    m_StickToBottom = false;
                }
                else
                {
                    float maxOffset = Mathf.Max(0f, contentHeight - m_ScrollView.contentViewport.layout.height);
                    m_StickToBottom = maxOffset > 0f && m_ScrollView.scrollOffset.y >= m_ScrollView.verticalScroller.highValue + (height - expected);
                }
            }
            if (!known || !Mathf.Approximately(height, registered))
            {
                RegisterItemHeight(item.index, height);
                UpdateScrollViewContainer(expected, height);
                if (m_WaitingCache.Count == 0) return true;
            }
            return m_WaitingCache.Remove(item.index) && m_WaitingCache.Count == 0;
        }

        internal override T GetOrMakeItemAtIndex(int activeItemIndex = -1, int scrollViewIndex = -1)
        {
            var item = base.GetOrMakeItemAtIndex(activeItemIndex, scrollViewIndex);
            item.onGeometryChanged += m_GeometryChangedCallback;
            return item;
        }

        internal override void ReleaseItem(int activeItemsIndex)
        {
            var item = m_ActiveItems[activeItemsIndex];
            item.onGeometryChanged -= m_GeometryChangedCallback;
            int index = item.index;
            UnregisterItemHeight(index);
            base.ReleaseItem(activeItemsIndex);
            m_WaitingCache.Remove(index);
        }

        internal override void StartDragItem(ReusableCollectionItem item)
        {
            m_WaitingCache.Remove(item.index);
            base.StartDragItem(item);
            m_DraggedItem.onGeometryChanged -= m_GeometryChangedCallback;
        }

        internal override void EndDrag(int dropIndex)
        {
            bool down = m_DraggedItem.index < dropIndex;
            int from = m_DraggedItem.index;
            int step = down ? 1 : -1;
            float draggedHeight = GetExpectedItemHeight(from);
            for (int i = from; i != dropIndex; i += step)
            {
                float here = GetExpectedItemHeight(i);
                float next = GetExpectedItemHeight(i + step);
                if (!Mathf.Approximately(here, next)) RegisterItemHeight(i, next);
            }
            RegisterItemHeight(down ? dropIndex - 1 : dropIndex, draggedHeight);
            if (firstVisibleIndex > m_DraggedItem.index)
            {
                firstVisibleIndex = GetFirstVisibleItem(m_ScrollView.scrollOffset.y);
                UpdateAnchor();
            }
            m_DraggedItem.onGeometryChanged += m_GeometryChangedCallback;
            base.EndDrag(dropIndex);
        }

        void HideItem(int activeItemsIndex)
        {
            var item = m_ActiveItems[activeItemsIndex];
            item.rootElement.style.display = DisplayStyle.None;
            m_WaitingCache.Remove(item.index);
        }

        /// <summary>The row waits for its layout: its last layout is forgotten so the next layout
        /// reports its geometry even at the same size.</summary>
        void MarkWaitingForLayout(T item)
        {
            if (item.isDragGhost) return;
            m_WaitingCache.Add(item.index);
            item.rootElement.lastLayout = Rect.zero;
            item.rootElement.MarkDirtyRepaint();
        }

        bool IsIndexOutOfBounds(int i) => m_CollectionView.itemsSource == null || i >= itemsCount;

        void RefreshScrollOffset()
        {
            contentHeight = GetExpectedContentHeight();
            m_ScrollView.verticalScroller.slider.SetHighValueWithoutNotify(Mathf.Max(0f, contentHeight - m_ScrollView.contentViewport.layout.height));
            m_ScrollView.scrollOffset = serializedData.scrollOffset;
            serializedData.scrollOffset.y = m_ScrollView.verticalScroller.value;
        }
    }
}
