using System;
using System.Collections.Generic;
using Unbound.Runtime;
using UnityEditor;
using UnityEngine;
using Xunit;

namespace UnityEngine.Tests
{
    [Collection("Serial")]
    public sealed class EditorLayoutGroupsTests
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

        static void RunOnGuiPass(int scope, EventType eventType, Action body, Event customEvent = null)
        {
            var previousEvent = Event.current;
            var current = customEvent ?? new Event { type = eventType };
            Event.current = current;
            GUIUtility.BeginGUI(0, scope, 1);
            GUI.ResetState();
            try
            {
                body();
                if (current.type == EventType.Layout)
                    GUILayoutUtility.Layout();
            }
            finally
            {
                GUILayoutUtility.End();
                Event.current = previousEvent;
            }
        }

        static void RunLayoutThenRepaint(int scope, Action body)
        {
            RunOnGuiPass(scope, EventType.Layout, body);
            RunOnGuiPass(scope, EventType.Repaint, body);
        }

        [Fact]
        public void PointsToPixelsAndPixelsToPointsScaleByPixelsPerPoint()
        {
            float saved = GUIUtility.pixelsPerPoint;
            try
            {
                foreach (float ppp in new[] { 1f, 2f, 1.5f })
                {
                    GUIUtility.pixelsPerPoint = ppp;
                    var rect = new Rect(10f, 20f, 30f, 40f);
                    var pxRect = EditorGUIUtility.PointsToPixels(rect);
                    Assert.Equal(new Rect(10f * ppp, 20f * ppp, 30f * ppp, 40f * ppp), pxRect);
                    var ptRect = EditorGUIUtility.PixelsToPoints(pxRect);
                    Assert.Equal(rect.x, ptRect.x, 4);
                    Assert.Equal(rect.y, ptRect.y, 4);
                    Assert.Equal(rect.width, ptRect.width, 4);
                    Assert.Equal(rect.height, ptRect.height, 4);

                    var vec = new Vector2(12.5f, -7.25f);
                    var pxVec = EditorGUIUtility.PointsToPixels(vec);
                    Assert.Equal(new Vector2(12.5f * ppp, -7.25f * ppp), pxVec);
                    var ptVec = EditorGUIUtility.PixelsToPoints(pxVec);
                    Assert.Equal(vec.x, ptVec.x, 4);
                    Assert.Equal(vec.y, ptVec.y, 4);
                }
            }
            finally
            {
                GUIUtility.pixelsPerPoint = saved;
            }
        }

        [Fact]
        public void LoadRequiredLogsExactErrorWhenMissingAndReturnsAdoptedResource()
        {
            const string guid = "eeeeeeeeeeeeeeeeeeeeeeeeeeeeee01";
            const string assetPath = "Assets/Editor Default Resources/UnitTestRequired.asset";
            AssetDatabase.List("[{\"guid\":\"" + guid + "\",\"path\":\"" + assetPath + "\",\"kind\":\"asset\"}]");
            var asset = DefaultAsset.Create("", HideFlags.None);
            AssetDatabaseRuntime.Adopt(guid, assetPath, asset, DefaultAsset.FileId);

            var prev = Debug.unityLogger.logHandler;
            var logs = new TestLogHandler();
            Debug.unityLogger.logHandler = logs;
            try
            {
                Assert.Same(asset, EditorGUIUtility.LoadRequired("UnitTestRequired.asset"));
                Assert.Same(asset, EditorGUIUtility.LoadRequired(assetPath));
                Assert.Empty(logs.Entries);

                Assert.Null(EditorGUIUtility.LoadRequired("NoSuchResource.txt"));
                var entry = Assert.Single(logs.Entries);
                Assert.Equal((LogType.Error, "Unable to find required resource at NoSuchResource.txt"), entry);

                logs.Entries.Clear();
                Assert.Throws<ArgumentException>(() => EditorGUIUtility.LoadRequired(""));
                Assert.Throws<NullReferenceException>(() => EditorGUIUtility.LoadRequired(null));
                Assert.Empty(logs.Entries);
            }
            finally
            {
                Debug.unityLogger.logHandler = prev;
                AssetDatabaseRuntime.Invalidate(guid);
                AssetDatabase.Invalidate();
                Object.DestroyImmediate(asset);
            }
        }

        [Fact]
        public void PingObjectHandlesNullInstanceIdAndBuiltinResourceWithoutLogging()
        {
            var prev = Debug.unityLogger.logHandler;
            var logs = new TestLogHandler();
            Debug.unityLogger.logHandler = logs;
            var go = new GameObject("PingTarget");
            try
            {
                EditorGUIUtility.PingObject((Object)null);
                EditorGUIUtility.PingObject(go);
                EditorGUIUtility.PingObject(go.GetInstanceID());
                EditorGUIUtility.PingObject(0);
                Assert.Empty(logs.Entries);

            }
            finally
            {
                Object.DestroyImmediate(go);
                Debug.unityLogger.logHandler = prev;
            }
        }

        [Fact]
        public void FadeGroupScalesHeightClipsHalfFadeConsumesMouseDownLocksContextWidthAndRestoresState()
        {
            const int scope = 9401;
            var prevLog = Debug.unityLogger.logHandler;
            var logs = new TestLogHandler();
            Debug.unityLogger.logHandler = logs;

            var prevHierarchy = EditorGUIUtility.hierarchyMode;
            var prevLabelWidth = EditorGUIUtility.labelWidth;
            var prevEnabled = GUI.enabled;
            var prevColor = GUI.color;

            var marginStyle = new GUIStyle(GUIStyle.none) { margin = new RectOffset(0, 0, 4, 4) };
            Rect beforeFade0 = default, group0 = default, afterFade0 = default;
            Rect groupHalf = default, childHalf = default, groupOne = default, childOne = default;
            bool vis0 = true, visHalf = false, visOne = false;
            bool restoredEnabled = false;
            Color restoredColor = default;
            float lwBefore = 0f, lwLocked = 0f, lwUnlocked = 0f;

            try
            {
                RunLayoutThenRepaint(scope, () =>
                {
                    GUILayout.BeginArea(new Rect(0, 0, 420, 220));
                    EditorGUIUtility.hierarchyMode = true;
                    EditorGUIUtility.labelWidth = 0f;

                    beforeFade0 = GUILayoutUtility.GetRect(80, 10, marginStyle);
                    vis0 = EditorGUILayout.BeginFadeGroup(0f);
                    GUILayoutUtility.GetRect(80, 20, marginStyle);
                    EditorGUILayout.EndFadeGroup();
                    group0 = GUILayoutUtility.GetLastRect();
                    afterFade0 = GUILayoutUtility.GetRect(80, 10, marginStyle);

                    GUI.enabled = true;
                    GUI.color = new Color(0.25f, 0.5f, 0.75f, 1f);
                    visHalf = EditorGUILayout.BeginFadeGroup(0.5f);
                    GUI.enabled = false;
                    GUI.color = Color.red;
                    childHalf = GUILayoutUtility.GetRect(80, 20, GUIStyle.none);
                    EditorGUILayout.EndFadeGroup();
                    groupHalf = GUILayoutUtility.GetLastRect();
                    restoredEnabled = GUI.enabled;
                    restoredColor = GUI.color;

                    lwBefore = EditorGUIUtility.labelWidth;
                    visOne = EditorGUILayout.BeginFadeGroup(1f);
                    GUI.BeginGroup(new Rect(0, 0, 120, 60));
                    lwLocked = EditorGUIUtility.labelWidth;
                    GUI.EndGroup();
                    childOne = GUILayoutUtility.GetRect(80, 20, GUIStyle.none);
                    EditorGUILayout.EndFadeGroup();
                    groupOne = GUILayoutUtility.GetLastRect();
                    GUI.BeginGroup(new Rect(0, 0, 120, 60));
                    lwUnlocked = EditorGUIUtility.labelWidth;
                    GUI.EndGroup();

                    if (Event.current.type == EventType.Repaint)
                        EditorGUILayout.EndFadeGroup();

                    GUILayout.EndArea();
                });

                Assert.False(vis0);
                Assert.True(visHalf);
                Assert.True(visOne);
                Assert.Equal(0f, group0.height);
                Assert.Equal(beforeFade0.yMax + 4f, afterFade0.y);
                Assert.Equal(10f, groupHalf.height);
                Assert.Equal(0f, childHalf.y);
                Assert.True(restoredEnabled);
                Assert.Equal(new Color(0.25f, 0.5f, 0.75f, 1f), restoredColor);
                Assert.Equal(20f, groupOne.height);
                Assert.Equal(groupOne.y, childOne.y);
                Assert.Equal(149f, lwBefore);
                Assert.Equal(149f, lwLocked);
                Assert.Equal(120f, lwUnlocked);

                var warn = Assert.Single(logs.Entries);
                Assert.Equal(LogType.Warning, warn.Type);
                Assert.Equal("Unexpected call to EndFadeGroup! Make sure to call EndFadeGroup the same number of times as BeginFadeGroup.", warn.Message);

                // MouseDown inside half-fade (0.5f) is consumed; inside full-fade (1f) it is preserved.
                EventType halfEventAfter = EventType.Ignore;
                EventType oneEventAfter = EventType.Ignore;
                var mouseHalf = new Event { type = EventType.MouseDown, mousePosition = new Vector2(10, 5) };
                RunOnGuiPass(scope + 1, EventType.Layout, () =>
                {
                    GUILayout.BeginArea(new Rect(0, 0, 200, 100));
                    EditorGUILayout.BeginFadeGroup(0.5f);
                    GUILayoutUtility.GetRect(80, 20, GUIStyle.none);
                    EditorGUILayout.EndFadeGroup();
                    GUILayout.EndArea();
                });
                RunOnGuiPass(scope + 1, EventType.MouseDown, () =>
                {
                    GUILayout.BeginArea(new Rect(0, 0, 200, 100));
                    EditorGUILayout.BeginFadeGroup(0.5f);
                    halfEventAfter = Event.current.type;
                    GUILayoutUtility.GetRect(80, 20, GUIStyle.none);
                    EditorGUILayout.EndFadeGroup();
                    GUILayout.EndArea();
                }, mouseHalf);
                Assert.Equal(EventType.Used, halfEventAfter);

                var mouseOne = new Event { type = EventType.MouseDown, mousePosition = new Vector2(10, 5) };
                RunOnGuiPass(scope + 2, EventType.Layout, () =>
                {
                    GUILayout.BeginArea(new Rect(0, 0, 200, 100));
                    EditorGUILayout.BeginFadeGroup(1f);
                    GUILayoutUtility.GetRect(80, 20, GUIStyle.none);
                    EditorGUILayout.EndFadeGroup();
                    GUILayout.EndArea();
                });
                RunOnGuiPass(scope + 2, EventType.MouseDown, () =>
                {
                    GUILayout.BeginArea(new Rect(0, 0, 200, 100));
                    EditorGUILayout.BeginFadeGroup(1f);
                    oneEventAfter = Event.current.type;
                    GUILayoutUtility.GetRect(80, 20, GUIStyle.none);
                    EditorGUILayout.EndFadeGroup();
                    GUILayout.EndArea();
                }, mouseOne);
                Assert.Equal(EventType.MouseDown, oneEventAfter);

                // FadeGroupScope double-dispose must only call EndFadeGroup once.
                logs.Entries.Clear();
                RunLayoutThenRepaint(scope + 3, () =>
                {
                    GUILayout.BeginArea(new Rect(0, 0, 200, 100));
                    var fadeScope = new EditorGUILayout.FadeGroupScope(0.5f);
                    Assert.True(fadeScope.visible);
                    GUILayoutUtility.GetRect(60, 20, GUIStyle.none);
                    fadeScope.Dispose();
                    fadeScope.Dispose();
                    GUILayout.EndArea();
                });
                Assert.Empty(logs.Entries);
            }
            finally
            {
                EditorGUIUtility.hierarchyMode = prevHierarchy;
                EditorGUIUtility.labelWidth = prevLabelWidth;
                GUI.enabled = prevEnabled;
                GUI.color = prevColor;
                Debug.unityLogger.logHandler = prevLog;
            }
        }

        [Fact]
        public void ScrollViewOverloadsAndScopesLayOutClampAndRespectHandleScrollWheel()
        {
            const int scope = 9410;
            var customBg = new GUIStyle(GUIStyle.none) { name = "UnitTestCustomScroll" };
            Vector2 posOver = default, posSmall = default, posVert = default, posHoriz = default, posStyle = default;
            Rect rectOver = default, childOver = default, childVert = default, childHoriz = default;

            RunLayoutThenRepaint(scope, () =>
            {
                GUILayout.BeginArea(new Rect(0, 0, 420, 260));

                posOver = EditorGUILayout.BeginScrollView(new Vector2(15f, 25f), GUILayout.Width(100), GUILayout.Height(80));
                childOver = GUILayoutUtility.GetRect(200, 200, GUIStyle.none);
                EditorGUILayout.EndScrollView();
                rectOver = GUILayoutUtility.GetLastRect();

                posSmall = EditorGUILayout.BeginScrollView(new Vector2(25f, 35f), false, false, GUILayout.Width(100), GUILayout.Height(80));
                GUILayoutUtility.GetRect(40, 30, GUIStyle.none);
                EditorGUILayout.EndScrollView();

                posStyle = EditorGUILayout.BeginScrollView(new Vector2(10f, 12f), customBg, GUILayout.Width(100), GUILayout.Height(80));
                GUILayoutUtility.GetRect(200, 200, GUIStyle.none);
                EditorGUILayout.EndScrollView();

                using (var vScope = new EditorGUILayout.VerticalScrollViewScope(new Vector2(10f, 20f), GUILayout.Width(100), GUILayout.Height(80)))
                {
                    Assert.True(vScope.handleScrollWheel);
                    posVert = vScope.scrollPosition;
                    childVert = GUILayoutUtility.GetRect(200, 200, GUIStyle.none);
                }

                using (var hScope = new EditorGUILayout.HorizontalScrollViewScope(new Vector2(10f, 20f), GUILayout.Width(100), GUILayout.Height(80)))
                {
                    Assert.True(hScope.handleScrollWheel);
                    posHoriz = hScope.scrollPosition;
                    childHoriz = GUILayoutUtility.GetRect(200, 200, GUIStyle.none);
                }

                GUILayout.EndArea();
            });

            Assert.Equal(new Vector2(15f, 25f), posOver);
            Assert.Equal(new Rect(0, 0, 100, 80), rectOver);
            Assert.Equal(200f, childOver.width);
            Assert.Equal(200f, childOver.height);
            Assert.Equal(Vector2.zero, posSmall);
            Assert.Equal(new Vector2(10f, 12f), posStyle);
            Assert.Equal(new Vector2(0f, 20f), posVert);
            Assert.Equal(new Vector2(10f, 0f), posHoriz);
            Assert.Equal(200f, childVert.height);
            Assert.Equal(200f, childHoriz.width);

            // ScrollViewScope.handleScrollWheel = true consumes ScrollWheel; false leaves it untouched.
            void RunWheelTest(int testScope, bool handleWheel, out EventType afterWheel)
            {
                RunOnGuiPass(testScope, EventType.Layout, () =>
                {
                    GUILayout.BeginArea(new Rect(0, 0, 200, 120));
                    using (var sv = new EditorGUILayout.ScrollViewScope(Vector2.zero, GUILayout.Width(100), GUILayout.Height(80)))
                    {
                        sv.handleScrollWheel = handleWheel;
                        GUILayoutUtility.GetRect(200, 200, GUIStyle.none);
                    }
                    GUILayout.EndArea();
                });

                var wheelEv = new Event
                {
                    type = EventType.ScrollWheel,
                    mousePosition = new Vector2(50, 40),
                    delta = new Vector2(0, 3f),
                };
                EventType observed = EventType.Ignore;
                RunOnGuiPass(testScope, EventType.ScrollWheel, () =>
                {
                    GUILayout.BeginArea(new Rect(0, 0, 200, 120));
                    using (var sv = new EditorGUILayout.ScrollViewScope(Vector2.zero, GUILayout.Width(100), GUILayout.Height(80)))
                    {
                        sv.handleScrollWheel = handleWheel;
                        GUILayoutUtility.GetRect(200, 200, GUIStyle.none);
                    }
                    observed = Event.current.type;
                    GUILayout.EndArea();
                }, wheelEv);
                afterWheel = observed;
            }

            RunWheelTest(scope + 1, handleWheel: true, out var wheelHandled);
            RunWheelTest(scope + 2, handleWheel: false, out var wheelIgnored);
            Assert.Equal(EventType.Used, wheelHandled);
            Assert.Equal(EventType.ScrollWheel, wheelIgnored);

            // ScrollViewScope double-dispose must be safe.
            RunLayoutThenRepaint(scope + 3, () =>
            {
                GUILayout.BeginArea(new Rect(0, 0, 200, 120));
                var sv = new EditorGUILayout.ScrollViewScope(Vector2.zero, false, false, GUIStyle.none, GUIStyle.none, GUIStyle.none,
                    GUILayout.Width(100), GUILayout.Height(80));
                GUILayoutUtility.GetRect(50, 50, GUIStyle.none);
                sv.Dispose();
                sv.Dispose();
                GUILayout.EndArea();
            });
        }

        [Fact]
        public void SeparatorAndSpaceRespectExpandFlagAndThrowOutsideOnGui()
        {
            const int scope = 9420;
            Rect vSep = default, vSpace = default, vSpace12 = default, vSpace12NoExp = default;
            Rect hSep = default, hSepSibling = default, hNoExp = default, hNoExpSibling = default, hExp = default, hExpSibling = default;

            RunLayoutThenRepaint(scope, () =>
            {
                GUILayout.BeginArea(new Rect(0, 0, 420, 220));

                GUILayout.BeginVertical(GUILayout.Width(160));
                EditorGUILayout.Separator();
                vSep = GUILayoutUtility.GetLastRect();
                EditorGUILayout.Space();
                vSpace = GUILayoutUtility.GetLastRect();
                EditorGUILayout.Space(12f);
                vSpace12 = GUILayoutUtility.GetLastRect();
                EditorGUILayout.Space(12f, false);
                vSpace12NoExp = GUILayoutUtility.GetLastRect();
                GUILayout.EndVertical();

                GUILayout.BeginHorizontal(GUILayout.Width(160));
                EditorGUILayout.Separator();
                hSep = GUILayoutUtility.GetLastRect();
                hSepSibling = GUILayoutUtility.GetRect(20, 10, GUIStyle.none);
                GUILayout.EndHorizontal();

                GUILayout.BeginHorizontal(GUILayout.Width(160));
                EditorGUILayout.Space(12f, false);
                hNoExp = GUILayoutUtility.GetLastRect();
                hNoExpSibling = GUILayoutUtility.GetRect(20, 10, GUIStyle.none);
                GUILayout.EndHorizontal();

                GUILayout.BeginHorizontal(GUILayout.Width(160));
                EditorGUILayout.Space(12f, true);
                hExp = GUILayoutUtility.GetLastRect();
                hExpSibling = GUILayoutUtility.GetRect(20, 10, GUIStyle.none);
                GUILayout.EndHorizontal();

                GUILayout.EndArea();
            });

            Assert.Equal(new Rect(0, 0, 160, 6), vSep);
            Assert.Equal(new Rect(0, 6, 160, 6), vSpace);
            Assert.Equal(new Rect(0, 12, 160, 12), vSpace12);
            Assert.Equal(new Rect(0, 24, 12, 12), vSpace12NoExp);

            Assert.Equal(73f, hSep.width);
            Assert.Equal(73f, hSepSibling.x);
            Assert.Equal(87f, hSepSibling.width);
            Assert.Equal(12f, hNoExp.width);
            Assert.Equal(12f, hNoExpSibling.x);
            Assert.Equal(148f, hNoExpSibling.width);
            Assert.Equal(76f, hExp.width);
            Assert.Equal(76f, hExpSibling.x);
            Assert.Equal(84f, hExpSibling.width);

            var previousEvent = Event.current;
            Event.current = null;
            try
            {
                Assert.Throws<NullReferenceException>(() => EditorGUILayout.Separator());
                Assert.Throws<NullReferenceException>(() => EditorGUILayout.Space());
                Assert.Throws<NullReferenceException>(() => EditorGUILayout.Space(12f, false));
                Assert.Throws<NullReferenceException>(() => EditorGUILayout.BeginFadeGroup(1f));
                Assert.Throws<ArgumentException>(() => EditorGUILayout.BeginScrollView(Vector2.zero));
                Assert.Throws<NullReferenceException>(() =>
                    EditorGUILayout.BeginScrollView(Vector2.zero, false, false, GUIStyle.none, GUIStyle.none, GUIStyle.none));
                Assert.Throws<ArgumentException>(() => EditorGUILayout.EndScrollView());
            }
            finally
            {
                Event.current = previousEvent;
            }
        }
    }
}
