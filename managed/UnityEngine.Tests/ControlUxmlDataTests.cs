using System.Collections.Generic;
using UnityEngine.UIElements;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>The UXML serialized data of controls packages derive their own from: made without
    /// a "not implemented" report, and creating the control (as Unity's generated data does).</summary>
    public class ControlUxmlDataTests
    {
        sealed class Capture : ILogHandler
        {
            internal readonly List<string> Lines = new List<string>();
            public void LogException(System.Exception exception, Object context) => Lines.Add(exception.Message);
            public void LogFormat(LogType logType, Object context, string format, params object[] args) => Lines.Add(string.Format(format, args));
        }

        [Fact]
        public void LabelTabViewAndTwoPaneSplitViewDataCreateTheirControls()
        {
            var capture = new Capture();
            var previous = Debug.unityLogger.logHandler;
            Debug.unityLogger.logHandler = capture;
            try
            {
                Assert.IsType<Label>(new Label.UxmlSerializedData().CreateInstance());
                Assert.IsType<TabView>(new TabView.UxmlSerializedData().CreateInstance());
                Assert.IsType<TwoPaneSplitView>(new TwoPaneSplitView.UxmlSerializedData().CreateInstance());
            }
            finally { Debug.unityLogger.logHandler = previous; }
            Assert.DoesNotContain(capture.Lines, line => line.Contains("UxmlSerializedData"));
        }
    }
}
