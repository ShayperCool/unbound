using System;
using System.Collections.Generic;
using Unbound.Runtime;
using UnityEditor;
using UnityEngine;
using Xunit;

namespace UnityEngine.Tests
{
    [Collection("Serial")]
    public class ImguiLayoutCalcFocusTests
    {
        sealed class TestLogHandler : ILogHandler
        {
            public readonly List<(LogType Type, string Message)> Entries = new List<(LogType, string)>();
            public void LogFormat(LogType logType, Object context, string format, params object[] args)
            {
                string message = (args != null && args.Length > 0) ? string.Format(format, args) : format;
                Entries.Add((logType, message));
            }
            public void LogException(Exception exception, Object context) =>
                Entries.Add((LogType.Exception, exception.GetType().Name + ":" + exception.Message));
        }

        static void RunOnGuiPass(int scope, EventType eventType, Action body)
        {
            var previousEvent = Event.current;
            var current = new Event { type = eventType };
            Event.current = current;
            GUIUtility.BeginGUI(0, scope, 1);
            GUI.ResetState();
            try
            {
                body();
                if (eventType == EventType.Layout)
                    GUILayoutUtility.Layout();
            }
            finally
            {
                GUILayoutUtility.End();
                Event.current = previousEvent;
            }
        }

        [Fact]
        public void NarrowVerticalAndHorizontalParentsShrinkStretchableEntriesOnly()
        {
            var stretchX = new GUIStyle { stretchWidth = true, stretchHeight = false };
            var fixedX = new GUIStyle { stretchWidth = false, stretchHeight = false };

            var col = new GUILayoutGroup { isVertical = true };
            var eStretch = new GUILayoutEntry(130, 180, 10, 10, stretchX, null);
            var eFixed = new GUILayoutEntry(130, 180, 10, 10, fixedX, null);
            var eMaxW = new GUILayoutEntry(130, 180, 10, 10, stretchX, new[] { GUILayout.MaxWidth(150) });
            col.Add(eStretch);
            col.Add(eFixed);
            col.Add(eMaxW);
            col.CalcWidth();
            col.SetHorizontal(0, 100);
            Assert.Equal(100f, eStretch.rect.width);
            Assert.Equal(130f, eFixed.rect.width);
            Assert.Equal(130f, eMaxW.rect.width);

            col.SetHorizontal(0, 200);
            Assert.Equal(200f, eStretch.rect.width);
            Assert.Equal(180f, eFixed.rect.width);
            Assert.Equal(150f, eMaxW.rect.width);

            var stretchY = new GUIStyle { stretchWidth = false, stretchHeight = true };
            var fixedY = new GUIStyle { stretchWidth = false, stretchHeight = false };

            var row = new GUILayoutGroup { isVertical = false };
            var vStretch = new GUILayoutEntry(15, 15, 45, 65, stretchY, null);
            var vFixed = new GUILayoutEntry(15, 15, 45, 65, fixedY, null);
            var vMaxH = new GUILayoutEntry(15, 15, 45, 65, stretchY, new[] { GUILayout.MaxHeight(55) });
            row.Add(vStretch);
            row.Add(vFixed);
            row.Add(vMaxH);
            row.CalcWidth();
            row.SetHorizontal(0, 200);
            row.CalcHeight();
            row.SetVertical(0, 30);
            Assert.Equal(30f, vStretch.rect.height);
            Assert.Equal(45f, vFixed.rect.height);
            Assert.Equal(45f, vMaxH.rect.height);

            row.SetVertical(0, 80);
            Assert.Equal(80f, vStretch.rect.height);
            Assert.Equal(65f, vFixed.rect.height);
            Assert.Equal(55f, vMaxH.rect.height);
        }

        [Fact]
        public void FractionalVerticalRowsRoundEachRectWhileAccumulatingUnroundedPosition()
        {
            var style = new GUIStyle { margin = new RectOffset(7, 5, 9, 6), stretchWidth = false };
            var col = new GUILayoutGroup { isVertical = true };
            var entries = new GUILayoutEntry[4];
            for (int i = 0; i < 4; i++)
            {
                entries[i] = new GUILayoutEntry(83, 83, 39.4f, 39.4f, style, null);
                col.Add(entries[i]);
            }
            col.CalcWidth();
            col.SetHorizontal(0, 140);
            col.CalcHeight();
            col.SetVertical(0, 240);

            Assert.Equal(new Rect(0, 0, 83, 39), entries[0].rect);
            Assert.Equal(new Rect(0, 48, 83, 39), entries[1].rect);
            Assert.Equal(new Rect(0, 97, 83, 39), entries[2].rect);
            Assert.Equal(new Rect(0, 145, 83, 39), entries[3].rect);
        }

        [Fact]
        public void UsedEventLayoutOutsideGroupReturnsDummyRectWithoutAdvancingCursor()
        {
            using var _ = TestRelease.Pin(TestRelease.Measured);
            const int scope = 9101;
            var style = new GUIStyle { stretchWidth = false };
            var wrap = new GUIStyle { wordWrap = true, stretchWidth = false };
            Rect beforeRepaint = Rect.zero;
            Rect usedWrap = Rect.zero;
            Rect usedAspect = Rect.zero;
            Rect usedLast = Rect.zero;
            Rect nextRepaint = Rect.zero;

            void RunArea(bool repaint)
            {
                GUILayout.BeginArea(new Rect(10, 12, 160, 120));
                try
                {
                    var b = GUILayoutUtility.GetRect(18, 7, style);
                    if (repaint) beforeRepaint = b;
                    if (repaint)
                    {
                        var ev = Event.current;
                        try
                        {
                            ev.type = EventType.MouseDown;
                            ev.Use();
                            usedWrap = GUILayoutUtility.GetRect(new GUIContent("Wrap text"), wrap);
                            usedAspect = GUILayoutUtility.GetAspectRect(2f);
                            usedLast = GUILayoutUtility.GetLastRect();
                            EditorGUILayout.LabelField("UsedLabel");
                        }
                        finally { ev.type = EventType.Repaint; }
                    }
                    var n = GUILayoutUtility.GetRect(21, 5, style);
                    if (repaint) nextRepaint = n;
                    if (!repaint) GUILayoutUtility.GetRect(39, 13, style);
                }
                finally { GUILayout.EndArea(); }
            }

            RunOnGuiPass(scope, EventType.Layout, () => RunArea(false));
            RunOnGuiPass(scope, EventType.Repaint, () => RunArea(true));

            Assert.Equal(new Rect(0, 0, 1, 1), usedWrap);
            Assert.Equal(new Rect(0, 0, 1, 1), usedAspect);
            Assert.Equal(new Rect(0, 0, 1, 1), usedLast);
            Assert.Equal(new Rect(0, 0, 18, 7), beforeRepaint);
            Assert.Equal(new Rect(0, 7, 21, 5), nextRepaint);
        }

        [Fact]
        public void NullTextAndNullContentLabelsMatchMeasuredPrefixAndLogging()
        {
            using var _ = TestRelease.Pin(TestRelease.Measured);
            Assert.True(EditorGUIControls.HasLabelContent(null));
            Assert.True(EditorGUIControls.HasLabelContent(new GUIContent((string)null)));
            Assert.False(EditorGUIControls.HasLabelContent(GUIContent.none));
            Assert.False(EditorGUIControls.HasLabelContent(new GUIContent("", "Tooltip")));

            var tex = new Texture2D(4, 4);
            try
            {
                var t1 = GUIContent.Temp("first");
                t1.image = tex;
                Assert.Same(tex, t1.image);
                var t2 = GUIContent.Temp("plain");
                Assert.Same(t1, t2);
                Assert.Null(t2.image);
            }
            finally { Object.DestroyImmediate(tex); }

            int prevIndent = EditorGUI.indentLevel;
            float prevLabelWidth = EditorGUIUtility.labelWidth;
            var capture = new TestLogHandler();
            var prevHandler = Debug.unityLogger.logHandler;
            try
            {
                EditorGUI.indentLevel = 1;
                EditorGUIUtility.labelWidth = 62;
                var pos = new Rect(10, 10, 140, 18);

                Debug.unityLogger.logHandler = capture;
                Rect layoutNullText = Rect.zero;
                Rect layoutNullContent = Rect.zero;
                RunOnGuiPass(9102, EventType.Layout, () =>
                {
                    int id = GUIUtility.GetControlID("TestNullPrefix".GetHashCode(), FocusType.Keyboard, pos);
                    layoutNullText = EditorGUI.PrefixLabel(pos, id, new GUIContent((string)null));
                    layoutNullContent = EditorGUI.PrefixLabel(pos, id, (GUIContent)null);
                });
                Assert.Empty(capture.Entries);
                Assert.Equal(new Rect(74, 10, 76, 18), layoutNullText);
                Assert.Equal(new Rect(74, 10, 76, 18), layoutNullContent);

                Rect repaintNullText = Rect.zero;
                Rect repaintNullContent = Rect.zero;
                RunOnGuiPass(9102, EventType.Repaint, () =>
                {
                    int id = GUIUtility.GetControlID("TestNullPrefix".GetHashCode(), FocusType.Keyboard, pos);
                    repaintNullText = EditorGUI.PrefixLabel(pos, id, new GUIContent((string)null));
                    Assert.Empty(capture.Entries);
                    repaintNullContent = EditorGUI.PrefixLabel(pos, id, (GUIContent)null);
                });
                Assert.Equal(new Rect(74, 10, 76, 18), repaintNullText);
                Assert.Equal(new Rect(74, 10, 76, 18), repaintNullContent);
                Assert.Single(capture.Entries);
                Assert.Equal(LogType.Error, capture.Entries[0].Type);
                Assert.Equal("Style.DrawPrefixLabel may not be called with GUIContent that is null.", capture.Entries[0].Message);
            }
            finally
            {
                Debug.unityLogger.logHandler = prevHandler;
                EditorGUI.indentLevel = prevIndent;
                EditorGUIUtility.labelWidth = prevLabelWidth;
            }
        }

        [Fact]
        public void ControlIdListPreservesFocusEligibilityAcrossPassesAndIgnoresUsedEvents()
        {
            const int scope = 9103;
            int hintA = "TestIdList_A".GetHashCode();
            int hintB = "TestIdList_B".GetHashCode();
            int hintC = "TestIdList_C".GetHashCode();
            int hintD = "TestIdList_D".GetHashCode();
            var rect = new Rect(0, 0, 80, 18);

            GUIUtility.ResetControlIdListsForTests();

            int idA1 = 0, idB1 = 0, idC1 = 0;
            bool focusA1 = false, focusB1 = false, focusC1 = false;
            RunOnGuiPass(scope, EventType.Layout, () =>
            {
                idA1 = GUIUtility.GetControlID(hintA, FocusType.Passive, rect);
                idB1 = GUIUtility.GetControlID(hintB, FocusType.Keyboard, rect);
                idC1 = GUIUtility.GetControlID(hintC, FocusType.Passive, rect);
                focusA1 = EditorGUIUtility.CanHaveKeyboardFocus(idA1);
                focusB1 = EditorGUIUtility.CanHaveKeyboardFocus(idB1);
                focusC1 = EditorGUIUtility.CanHaveKeyboardFocus(idC1);
            });
            Assert.False(focusA1);
            Assert.True(focusB1);
            Assert.False(focusC1);

            int idA2 = 0, idB2 = 0, idD2 = 0, usedId = 0, usedStep = -1;
            bool focusA2 = false, focusB2 = false, focusD2 = false;
            RunOnGuiPass(scope, EventType.Repaint, () =>
            {
                idA2 = GUIUtility.GetControlID(hintA, FocusType.Keyboard, rect);
                idB2 = GUIUtility.GetControlID(hintB, FocusType.Passive, rect);
                idD2 = GUIUtility.GetControlID(hintD, FocusType.Keyboard, rect);
                focusA2 = EditorGUIUtility.CanHaveKeyboardFocus(idA2);
                focusB2 = EditorGUIUtility.CanHaveKeyboardFocus(idB2);
                focusD2 = EditorGUIUtility.CanHaveKeyboardFocus(idD2);

                int idxBefore = EditorGUIUtility.GetSearchIndexOfControlIDList();
                Event.current.type = EventType.MouseDown;
                Event.current.Use();
                GUI.SetNextControlName("used_control_should_not_bind");
                usedId = GUIUtility.GetControlID("UsedHint".GetHashCode(), FocusType.Keyboard, rect);
                usedStep = EditorGUIUtility.GetSearchIndexOfControlIDList() - idxBefore;
                Event.current.type = EventType.Repaint;
                int afterUsedId = GUIUtility.GetControlID("AfterUsedHint".GetHashCode(), FocusType.Keyboard, rect);
                GUI.FocusControl("used_control_should_not_bind");
                Assert.Equal(afterUsedId, GUIUtility.keyboardControl);
            });

            Assert.Equal(idA1, idA2);
            Assert.Equal(idB1, idB2);
            Assert.NotEqual(idC1, idD2);
            Assert.False(focusA2);
            Assert.True(focusB2);
            Assert.True(focusD2);
            Assert.Equal(-1, usedId);
            Assert.Equal(0, usedStep);
        }

        [Fact]
        public void GroupStretchPropagationAndRootTopLevelWidthCapMatchMeasuredLayout()
        {
            var stretchY = new GUIStyle { stretchWidth = false, stretchHeight = true };
            var fixedY = new GUIStyle { stretchWidth = false, stretchHeight = false };

            // A horizontal group with a stretchable vertical child propagates stretchHeight when maxHeight == 0.
            var outer = new GUILayoutGroup { isVertical = true };
            var row = new GUILayoutGroup(GUIStyle.none, null) { isVertical = false };
            var childStretch = new GUILayoutEntry(15, 15, 45, 65, stretchY, null);
            var childFixed = new GUILayoutEntry(15, 15, 45, 65, fixedY, null);
            row.Add(childStretch);
            row.Add(childFixed);
            outer.Add(row);
            outer.CalcWidth();
            outer.SetHorizontal(0, 200);
            outer.CalcHeight();
            Assert.True(row.stretchHeight > 0);
            outer.SetVertical(0, 80);
            Assert.Equal(80f, childStretch.rect.height);
            Assert.Equal(65f, childFixed.rect.height);

            // A non-zero MaxWidth clears stretchWidth on a group, while ExpandWidth(false) with GUIStyle.none still stretches.
            var rowMaxW = new GUILayoutGroup(GUIStyle.none, new[] { GUILayout.MaxWidth(120) }) { isVertical = false };
            rowMaxW.Add(new GUILayoutEntry(145, 145, 10, 10, fixedY, null));
            rowMaxW.CalcWidth();
            Assert.Equal(0, rowMaxW.stretchWidth);

            var rowNoExpand = new GUILayoutGroup(GUIStyle.none, new[] { GUILayout.ExpandWidth(false) }) { isVertical = false };
            rowNoExpand.Add(new GUILayoutEntry(145, 145, 10, 10, fixedY, null));
            rowNoExpand.CalcWidth();
            Assert.True(rowNoExpand.stretchWidth > 0);

            // Root topLevel layout caps width to topLevel.maxWidth when smaller than Screen.width.
            const int scope = 9104;
            var plain = new GUIStyle { stretchWidth = true, stretchHeight = false };
            Rect first = Rect.zero;
            RunOnGuiPass(scope, EventType.Layout, () =>
            {
                GUILayoutUtility.GetRect(16, 6, plain);
                GUILayoutUtility.GetRect(41, 14, plain);
            });
            RunOnGuiPass(scope, EventType.Repaint, () =>
            {
                first = GUILayoutUtility.GetRect(16, 6, plain);
                GUILayoutUtility.GetRect(41, 14, plain);
            });
            Assert.Equal(41f, first.width);
        }

        [Fact]
        public void MinWidthMinHeightOnGroupsAndRepeatedScrollViewCalcHeightMatchUnity()
        {
            var stretchX = new GUIStyle { stretchWidth = true, stretchHeight = false };
            var stretchY = new GUIStyle { stretchWidth = false, stretchHeight = true };

            // Matches Unity 6000.0.71f1 (imguiskin minWidthGroup): MinWidth(50) on a group raises its
            // unset maxWidth from 0 to 50, so CalcWidth clears stretchWidth and caps the group at 50.
            var parentCol = new GUILayoutGroup { isVertical = true };
            var minWidthRow = new GUILayoutGroup(GUIStyle.none, new[] { GUILayout.MinWidth(50) }) { isVertical = false };
            var growChild = new GUILayoutEntry(30, 120, 12, 12, stretchX, null);
            minWidthRow.Add(growChild);
            parentCol.Add(minWidthRow);
            parentCol.CalcWidth();
            Assert.Equal(0, minWidthRow.stretchWidth);
            Assert.Equal(50f, minWidthRow.maxWidth);
            parentCol.SetHorizontal(0, 200);
            Assert.Equal(50f, minWidthRow.rect.width);
            Assert.Equal(50f, growChild.rect.width);

            // Symmetric MinHeight on a vertical group raises its unset maxHeight to 50 and clears stretchHeight.
            var parentRow = new GUILayoutGroup { isVertical = false };
            var minHeightCol = new GUILayoutGroup(GUIStyle.none, new[] { GUILayout.MinHeight(50) }) { isVertical = true };
            var tallChild = new GUILayoutEntry(20, 20, 30, 120, stretchY, null);
            minHeightCol.Add(tallChild);
            parentRow.Add(minHeightCol);
            parentRow.CalcWidth();
            parentRow.SetHorizontal(0, 100);
            parentRow.CalcHeight();
            Assert.Equal(0, minHeightCol.stretchHeight);
            Assert.Equal(50f, minHeightCol.maxHeight);
            parentRow.SetVertical(0, 200);
            Assert.Equal(50f, minHeightCol.rect.height);
            Assert.Equal(50f, tallChild.rect.height);

            // Leaf entry with MinWidth exceeding its initial maxWidth keeps maxWidth >= minWidth.
            var leaf = new GUILayoutEntry(10, 50, 12, 12, new GUIStyle(), new[] { GUILayout.MinWidth(80) });
            Assert.Equal(80f, leaf.minWidth);
            Assert.Equal(80f, leaf.maxWidth);

            // Repeated CalcHeight() inside GUIScrollGroup updates growing child minHeight/maxHeight/calcMaxHeight.
            var scroll = new GUIScrollGroup
            {
                isVertical = true,
                verticalScrollbar = new GUIStyle { fixedWidth = 15f },
                horizontalScrollbar = new GUIStyle { fixedHeight = 15f },
                allowHorizontalScroll = false,
                allowVerticalScroll = true,
            };
            var nested = new GUILayoutGroup(GUIStyle.none, null) { isVertical = true };
            var growing = new DynamicHeightEntry(20, 40, stretchY);
            nested.Add(growing);
            scroll.Add(nested);

            scroll.CalcWidth();
            scroll.SetHorizontal(0, 100);
            scroll.CalcHeight();
            Assert.True(nested.stretchHeight > 0);
            Assert.Equal(40f, nested.maxHeight);

            growing.CurrentHeight = 120f;
            scroll.CalcHeight();
            Assert.Equal(120f, nested.minHeight);
            Assert.Equal(120f, nested.maxHeight);
            Assert.Equal(120f, scroll.calcMaxHeight);
        }

        static GUIScrollGroup NewScrollGroup(params GUILayoutOption[] options)
        {
            var scroll = new GUIScrollGroup
            {
                isVertical = true,
                verticalScrollbar = new GUIStyle { fixedWidth = 15f },
                horizontalScrollbar = new GUIStyle { fixedHeight = 15f },
            };
            scroll.ApplyOptions(options);
            return scroll;
        }

        static void Arrange(GUILayoutGroup group, float width, float height)
        {
            group.CalcWidth();
            group.SetHorizontal(0f, width);
            group.CalcHeight();
            group.SetVertical(0f, height);
        }

        [Fact]
        public void ScrollViewWithMaximumSizeDoesNotTakeItsParentsExtraSpace()
        {
            var stretchBoth = new GUIStyle { stretchWidth = true, stretchHeight = true };

            // A view limited to 200 wide in a 400 wide column keeps 200; its stretching content fills it.
            var column = new GUILayoutGroup { isVertical = true };
            var maxWidthView = NewScrollGroup(GUILayout.MaxWidth(200f));
            var content = new GUILayoutEntry(40f, 40f, 20f, 20f, stretchBoth);
            maxWidthView.Add(content);
            column.Add(maxWidthView);
            Arrange(column, 400f, 300f);
            Assert.Equal(0, maxWidthView.stretchWidth);
            Assert.Equal(200f, maxWidthView.rect.width);
            Assert.Equal(200f, content.rect.width);
            // The height has no maximum: the view still takes the column's height.
            Assert.Equal(300f, maxWidthView.rect.height);

            // The same for the height, in a row.
            var row = new GUILayoutGroup { isVertical = false };
            var maxHeightView = NewScrollGroup(GUILayout.MaxHeight(120f));
            var tall = new GUILayoutEntry(40f, 40f, 20f, 20f, stretchBoth);
            maxHeightView.Add(tall);
            row.Add(maxHeightView);
            Arrange(row, 200f, 400f);
            Assert.Equal(0, maxHeightView.stretchHeight);
            Assert.Equal(120f, maxHeightView.rect.height);
            Assert.Equal(120f, tall.rect.height);
            Assert.Equal(200f, maxHeightView.rect.width);

            // Control: without a maximum the view takes all of its parent.
            var freeColumn = new GUILayoutGroup { isVertical = true };
            var freeView = NewScrollGroup();
            freeView.Add(new GUILayoutEntry(40f, 40f, 20f, 20f, stretchBoth));
            freeColumn.Add(freeView);
            Arrange(freeColumn, 400f, 300f);
            Assert.Equal(400f, freeView.rect.width);
            Assert.Equal(300f, freeView.rect.height);

            // The maximum bounds the view, not what scrolls inside it: content wider than the
            // maximum keeps its width and the view shows a horizontal scrollbar.
            var narrowColumn = new GUILayoutGroup { isVertical = true };
            var narrowView = NewScrollGroup(GUILayout.MinWidth(100f), GUILayout.MaxWidth(200f));
            var wide = new GUILayoutEntry(300f, 300f, 20f, 20f, stretchBoth);
            narrowView.Add(wide);
            narrowColumn.Add(narrowView);
            Arrange(narrowColumn, 400f, 300f);
            Assert.Equal(200f, narrowView.rect.width);
            Assert.True(narrowView.needsHorizontalScrollbar);
            Assert.Equal(300f, wide.rect.width);

            // A row of stretching entries inside a limited view still shares the view's width.
            var rowView = NewScrollGroup(GUILayout.MaxWidth(200f));
            rowView.isVertical = false;
            var left = new GUILayoutEntry(20f, 20f, 20f, 20f, stretchBoth);
            var right = new GUILayoutEntry(20f, 20f, 20f, 20f, stretchBoth);
            rowView.Add(left);
            rowView.Add(right);
            var rowColumn = new GUILayoutGroup { isVertical = true };
            rowColumn.Add(rowView);
            Arrange(rowColumn, 400f, 300f);
            Assert.Equal(200f, rowView.rect.width);
            Assert.Equal(100f, left.rect.width);
            Assert.Equal(100f, right.rect.width);
        }

        sealed class DynamicHeightEntry : GUILayoutEntry
        {
            public float CurrentHeight;
            public DynamicHeightEntry(float minH, float initialH, GUIStyle style) : base(20, 20, minH, initialH, style) =>
                CurrentHeight = initialH;
            public override void CalcHeight() => minHeight = maxHeight = CurrentHeight;
        }

        [Fact]
        public void TryConvertStyleToSettingsResetsDirtiedTextHandleSettings()
        {
            var tgs = UnityEngine.TextCore.Text.TextHandle.settings;
            tgs.extraPadding = 1.25f;
            tgs.emojiFallbackSupport = false;
            tgs.fontWeight = UnityEngine.TextCore.Text.TextFontWeight.Heavy;
            tgs.isEditorRenderingModeBitmap = true;
            tgs.pixelsPerPoint = 3f;
            tgs.parseControlCharacters = true;
            tgs.isRightToLeft = true;
            tgs.isPlaceholder = true;
            tgs.characterSpacing = 4f;
            tgs.wordSpacing = 5f;
            tgs.paragraphSpacing = 6f;
            tgs.shouldConvertToLinearSpace = true;

            var style = new GUIStyle { fontSize = 12, wordWrap = true, richText = false };
            // Even when no native font asset is loaded (returns false), or when a font is absent,
            // verify a real font asset resets every dirtied field on TextHandle.settings.
            var font = new Font("TestResetFont");
            var textSettings = ScriptableObject.CreateInstance<UnityEngine.TextCore.Text.TextSettings>();
            var fontAsset = ScriptableObject.CreateInstance<UnityEngine.TextCore.Text.FontAsset>();
            textSettings.m_FontReferences.Add(new UnityEngine.TextCore.Text.TextSettings.FontReferenceMap(font, fontAsset));
            var prevGetter = IMGUITextHandle.GetEditorTextSettings;
            IMGUITextHandle.GetEditorTextSettings = () => textSettings;
            IMGUITextHandle.ResetForTests();
            try
            {
                style.font = font;
                bool ok = IMGUITextHandle.TryConvertStyleToSettings(
                    tgs, style, Color.white, new UnityEngine.TextCore.Text.RenderedText("hello", "\u200B"), new Rect(0, 0, 80, 100));
                Assert.True(ok);
                Assert.Equal(6f, tgs.extraPadding);
                Assert.True(tgs.emojiFallbackSupport);
                Assert.Equal(UnityEngine.TextCore.Text.TextFontWeight.Regular, tgs.fontWeight);
                Assert.False(tgs.isEditorRenderingModeBitmap);
                Assert.Equal(GUIUtility.pixelsPerPoint, tgs.pixelsPerPoint);
                Assert.False(tgs.parseControlCharacters);
                Assert.False(tgs.isRightToLeft);
                Assert.False(tgs.isPlaceholder);
                Assert.Equal(0f, tgs.characterSpacing);
                Assert.Equal(0f, tgs.wordSpacing);
                Assert.Equal(0f, tgs.paragraphSpacing);
                Assert.False(tgs.shouldConvertToLinearSpace);
                Assert.True(tgs.isIMGUI);
            }
            finally
            {
                IMGUITextHandle.GetEditorTextSettings = prevGetter;
                IMGUITextHandle.ResetForTests();
                Object.DestroyImmediate(fontAsset);
                Object.DestroyImmediate(textSettings);
                Object.DestroyImmediate(font);
            }
        }

        [Fact]
        public void UsedEventBeginAreaDoesNotMutateWindowsOrAdvanceCursor()
        {
            using var _ = TestRelease.Pin(TestRelease.Measured);
            const int scope = 9105;
            var style = new GUIStyle { stretchWidth = false };
            Rect insideUsed = Rect.zero;
            Rect afterUsedArea = Rect.zero;
            int windowsBeforeUsed = 0;
            int windowsAfterUsed = 0;

            RunOnGuiPass(scope, EventType.Layout, () =>
            {
                GUILayout.BeginArea(new Rect(18, 22, 130, 70));
                try { GUILayoutUtility.GetRect(25, 9, style); }
                finally { GUILayout.EndArea(); }
            });

            RunOnGuiPass(scope, EventType.Repaint, () =>
            {
                windowsBeforeUsed = GUILayoutUtility.current.windows.entries.Count;
                var ev = Event.current;
                try
                {
                    ev.type = EventType.MouseDown;
                    ev.Use();
                    GUILayout.BeginArea(new Rect(30, 30, 90, 60));
                    try { insideUsed = GUILayoutUtility.GetRect(50, 12); }
                    finally { GUILayout.EndArea(); }
                }
                finally { ev.type = EventType.Repaint; }
                windowsAfterUsed = GUILayoutUtility.current.windows.entries.Count;

                GUILayout.BeginArea(new Rect(18, 22, 130, 70));
                try { afterUsedArea = GUILayoutUtility.GetRect(25, 9, style); }
                finally { GUILayout.EndArea(); }
            });

            Assert.Equal(1, windowsBeforeUsed);
            Assert.Equal(1, windowsAfterUsed);
            Assert.Equal(new Rect(0, 0, 1, 1), insideUsed);
            Assert.Equal(new Rect(0, 0, 25, 9), afterUsedArea);
        }

        [Fact]
        public void CalcHeightOfAFixedHeightStyleIsItsFixedHeight()
        {
            var fixedStyle = new GUIStyle { fixedHeight = 34f };
            Assert.Equal(34f, fixedStyle.CalcHeight(new GUIContent("abc"), 83f));
        }
    }
}
