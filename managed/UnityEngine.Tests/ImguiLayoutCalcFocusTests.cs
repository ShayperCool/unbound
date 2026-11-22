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
                var t1 = GUIContent.Temp(tex);
                Assert.Same(tex, t1.image);
                var t2 = GUIContent.Temp("plain");
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
        public void CalcHeightCompatGateFollowsMeasuredReleaseFamily()
        {
            using (TestRelease.Pin("6000.0.71f1"))
                Assert.True(Compat.ImguiTextCoreCalcHeightMeasured);
            using (TestRelease.Pin(TestRelease.Unmeasured))
                Assert.False(Compat.ImguiTextCoreCalcHeightMeasured);

            var fixedStyle = new GUIStyle { fixedHeight = 34f };
            Assert.Equal(34f, fixedStyle.CalcHeight(new GUIContent("abc"), 83f));
        }
    }
}
