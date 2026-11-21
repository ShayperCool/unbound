using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine.UIElements;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>EnumField's entries are the enum data Unity's EnumField reads (UnityCsReference 6000.0
    /// EnumField.PopulateDataFromType, EnumDataUtility): obsolete values only with
    /// includeObsoleteValues and never those obsolete as errors, [InspectorOrder] applied to the
    /// values as the enum data orders them, one warning for duplicate names.</summary>
    public class EnumFieldDataTests
    {
        public enum WithErrorObsolete
        {
            Keep,
            [Obsolete("old")] Old,
#pragma warning disable CS0618
            [Obsolete("gone", true)] Gone,
#pragma warning restore CS0618
        }

        [InspectorOrder(InspectorSort.ByValue)]
        public enum SignedByValue { Plus = 3, Minus = -2, Zero = 0 }

        [InspectorOrder(InspectorSort.ByValue)]
        public enum UnsignedByValue : byte { Low = 2, High = 255, Mid = 100 }

        public enum Duplicated
        {
            [InspectorName("Same")] First,
            [InspectorName("Same")] Second,
        }

        [Flags]
        public enum FlagsFirst { None = 0, MyValue = 1, OtherValue = 2 }

        sealed class RecordingMenu : IGenericMenu
        {
            public readonly List<string> Items = new List<string>();
            public void AddItem(string itemName, bool isChecked, Action action) => Items.Add(itemName);
            public void AddItem(string itemName, bool isChecked, Action<object> action, object data) => Items.Add(itemName);
            public void AddDisabledItem(string itemName, bool isChecked) => Items.Add(itemName);
            public void AddSeparator(string path) { }
            public void DropDown(Rect position, VisualElement targetElement = null, bool anchored = false) { }
        }

        static List<string> Menu(EnumField field)
        {
            var menu = new RecordingMenu();
            field.createMenuCallback = () => menu;
            field.ShowMenu();
            return menu.Items;
        }

        [Fact]
        public void ObsoleteAsErrorValuesAreNeverListed()
        {
            var plain = new EnumField(WithErrorObsolete.Keep);
            Assert.Equal(new[] { "Keep" }, Menu(plain));
            var withObsolete = new EnumField();
            withObsolete.Init(WithErrorObsolete.Keep, true);
            Assert.Equal(new[] { "Keep", "Old (Obsolete)" }, Menu(withObsolete));
            // An obsolete value the entries leave out shows no text.
#pragma warning disable CS0618
            plain.value = WithErrorObsolete.Old;
#pragma warning restore CS0618
            Assert.Equal(string.Empty, plain.text);
        }

        [Fact]
        public void ByValueOrderFollowsTheEnumDataValues()
        {
            Assert.Equal(new[] { "Minus", "Zero", "Plus" }, Menu(new EnumField(SignedByValue.Zero)));
            // The enum data stores a byte enum's all-bits value (255) as ~0, which sorts first.
            Assert.Equal(new[] { "High", "Low", "Mid" }, Menu(new EnumField(UnsignedByValue.Low)));
        }

        [Fact]
        public void DuplicateNamesAreWarnedAboutOnce()
        {
            var warnings = new List<string>();
            var logger = Debug.unityLogger;
            var previous = logger.logHandler;
            logger.logHandler = new Capture(warnings);
            try
            {
                new EnumField(Duplicated.First);
                new EnumField(Duplicated.Second);
            }
            finally
            {
                logger.logHandler = previous;
            }
            Assert.Single(warnings, w => w.Contains("Duplicated") && w.Contains("same display name"));
        }

        [Fact]
        public void AFlagsEnumConvertedBeforeKeepsItsNicifiedNames()
        {
            EnumDataUtility.IntToEnumFlags(typeof(FlagsFirst), 3);
            Assert.Equal(new[] { "None", "My Value", "Other Value" }, Menu(new EnumField(FlagsFirst.None)));
        }

        sealed class Capture : ILogHandler
        {
            readonly List<string> m_Logs;
            public Capture(List<string> logs) => m_Logs = logs;
            public void LogFormat(LogType logType, Object context, string format, params object[] args) =>
                m_Logs.Add(args == null || args.Length == 0 ? format : string.Format(format, args));
            public void LogException(Exception exception, Object context) => m_Logs.Add(exception.Message);
        }
    }
}
