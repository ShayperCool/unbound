using System;

namespace UnityEngine
{
    public partial struct RangeInt
    {
        public int start;
        public int length;

        public RangeInt(int start, int length)
        {
            this.start = start;
            this.length = length;
        }

        public int end => start + length;
    }

    /// <summary>The one display there is: the game's window (or headless screen).</summary>
    public partial class Display
    {
        internal IntPtr nativeDisplay;

        public static Display[] displays = { new Display() };

        internal Display() { }

        internal Display(IntPtr nativeDisplay)
        {
            this.nativeDisplay = nativeDisplay;
        }

        public static Display main => displays[0];

        public static int activeEditorGameViewTarget { get; internal set; }

        [Unbound.EditorFaithful("the Editor has one display, its Game view: activating and sizing displays is player-only")]
        public void Activate() { }

        [Unbound.EditorFaithful("the Editor has one display, its Game view: activating and sizing displays is player-only")]
        public void Activate(int width, int height, RefreshRate refreshRate) { }

        [Obsolete("Activate(int, int, int) is deprecated. Use Activate(int, int, RefreshRate) instead.", false)]
        [Unbound.EditorFaithful("the Editor has one display, its Game view: activating and sizing displays is player-only")]
        public void Activate(int width, int height, int refreshRate) { }

        [Unbound.EditorFaithful("the Editor has one display, its Game view: activating and sizing displays is player-only")]
        public void SetParams(int width, int height, int x, int y) { }

        [Unbound.EditorFaithful("the Editor has one display, its Game view: activating and sizing displays is player-only")]
        public void SetRenderingResolution(int w, int h) { }

        [Obsolete("MultiDisplayLicense has been deprecated.", false)]
        [Unbound.EditorFaithful("deprecated: every license allows multiple displays")]
        public static bool MultiDisplayLicense() => true;

        /// <summary>The position on the display it falls on, with that display's index in z.</summary>
        public static Vector3 RelativeMouseAt(Vector3 inputMouseCoordinates) => new Vector3(inputMouseCoordinates.x, inputMouseCoordinates.y, 0f);

        public int renderingWidth => Screen.width;

        public int renderingHeight => Screen.height;

        public int systemWidth => Screen.width;

        public int systemHeight => Screen.height;

        [Unbound.EditorFaithful("the one display, the Game view, is active")]
        public bool active => true;

        [Unbound.EditorFaithful("the Editor's Game view needs no extra blit (no HDR output)")]
        public bool requiresBlitToBackbuffer => false;

        [Unbound.EditorFaithful("the Editor's Game view target is sRGB-capable; URP's final blit matches it (tests/projects/urp)")]
        public bool requiresSrgbBlitToBackbuffer => false;
    }

    /// <summary>The Editor's record of properties a component drives (RectTransforms laid out by
    /// layout groups), which it keeps out of saved scenes. Unbound saves no scenes: nothing to record.</summary>
    [Unbound.EditorFaithful("the record only keeps driven values out of saved scenes and the Inspector; Play mode does not observe it")]
    internal partial class DrivenPropertyManager
    {
        public static void RegisterProperty(Object driver, Object target, string propertyPath) { }

        public static void TryRegisterProperty(Object driver, Object target, string propertyPath) { }

        public static void UnregisterProperty(Object driver, Object target, string propertyPath) { }

        public static void UnregisterProperties(Object driver) { }
    }

    public enum TouchScreenKeyboardType
    {
        Default = 0,
        ASCIICapable = 1,
        NumbersAndPunctuation = 2,
        URL = 3,
        NumberPad = 4,
        PhonePad = 5,
        NamePhonePad = 6,
        EmailAddress = 7,
        [Obsolete("Wii U is no longer supported as of Unity 2018.1.")] NintendoNetworkAccount = 8,
        Social = 9,
        Search = 10,
        DecimalPad = 11,
        OneTimeCode = 12,
    }

    /// <summary>
    /// The on-screen keyboard, as the Editor on a desktop has it: not supported, so input fields
    /// read the physical keyboard. A keyboard opened anyway keeps its text and reports itself visible.
    /// </summary>
    public partial class TouchScreenKeyboard
    {
        public enum Status
        {
            Visible = 0,
            Done = 1,
            Canceled = 2,
            LostFocus = 3,
        }

        public enum InputFieldAppearance
        {
            Customizable = 0,
            AlwaysVisible = 1,
            AlwaysHidden = 2,
        }

        string m_Text;
        Status m_Status = Status.Visible;
        int m_CharacterLimit;
        RangeInt m_Selection;
        readonly TouchScreenKeyboardType m_Type;

        public TouchScreenKeyboard(string text, TouchScreenKeyboardType keyboardType, bool autocorrection, bool multiline, bool secure, bool alert, string textPlaceholder, int characterLimit)
        {
            m_Text = text ?? "";
            m_Type = keyboardType;
            m_CharacterLimit = characterLimit;
            m_Selection = new RangeInt(m_Text.Length, 0);
        }

        public static TouchScreenKeyboard Open(string text, [Internal.DefaultValue("TouchScreenKeyboardType.Default")] TouchScreenKeyboardType keyboardType, [Internal.DefaultValue("true")] bool autocorrection, [Internal.DefaultValue("false")] bool multiline, [Internal.DefaultValue("false")] bool secure, [Internal.DefaultValue("false")] bool alert, [Internal.DefaultValue("\"\"")] string textPlaceholder, [Internal.DefaultValue("0")] int characterLimit) =>
            new TouchScreenKeyboard(text, keyboardType, autocorrection, multiline, secure, alert, textPlaceholder, characterLimit);

        public static TouchScreenKeyboard Open(string text, TouchScreenKeyboardType keyboardType, bool autocorrection, bool multiline, bool secure, bool alert, string textPlaceholder) =>
            Open(text, keyboardType, autocorrection, multiline, secure, alert, textPlaceholder, 0);

        public static TouchScreenKeyboard Open(string text, TouchScreenKeyboardType keyboardType, bool autocorrection, bool multiline, bool secure, bool alert) =>
            Open(text, keyboardType, autocorrection, multiline, secure, alert, "", 0);

        public static TouchScreenKeyboard Open(string text, TouchScreenKeyboardType keyboardType, bool autocorrection, bool multiline, bool secure) =>
            Open(text, keyboardType, autocorrection, multiline, secure, false, "", 0);

        public static TouchScreenKeyboard Open(string text, TouchScreenKeyboardType keyboardType, bool autocorrection, bool multiline) =>
            Open(text, keyboardType, autocorrection, multiline, false, false, "", 0);

        public static TouchScreenKeyboard Open(string text, TouchScreenKeyboardType keyboardType, bool autocorrection) =>
            Open(text, keyboardType, autocorrection, false, false, false, "", 0);

        public static TouchScreenKeyboard Open(string text, TouchScreenKeyboardType keyboardType) =>
            Open(text, keyboardType, true, false, false, false, "", 0);

        public static TouchScreenKeyboard Open(string text) => Open(text, TouchScreenKeyboardType.Default, true, false, false, false, "", 0);

        [Unbound.EditorFaithful("measured: tests/projects/platform")]
        public static bool isSupported => false;

        [Unbound.EditorFaithful("in-place editing is Android-only")]
        public static bool isInPlaceEditingAllowed => false;

        public static bool hideInput { get; set; }

        public static InputFieldAppearance inputFieldAppearance => InputFieldAppearance.Customizable;

        public static Rect area => new Rect(0f, 0f, 0f, 0f);

        [Unbound.EditorFaithful("measured: tests/projects/platform")]
        public static bool visible => false;

        public string text
        {
            get => m_Text;
            set => m_Text = value ?? "";
        }

        public bool active
        {
            get => m_Status == Status.Visible;
            set => m_Status = value ? Status.Visible : Status.LostFocus;
        }

        [Obsolete("Property done is deprecated, use status instead", false)]
        public bool done => m_Status != Status.Visible;

        [Obsolete("Property wasCanceled is deprecated, use status instead.", false)]
        public bool wasCanceled => m_Status == Status.Canceled;

        public Status status => m_Status;

        public int characterLimit
        {
            get => m_CharacterLimit;
            set => m_CharacterLimit = value;
        }

        [Unbound.EditorFaithful("this keyboard keeps its selection (see selection)")]
        public bool canGetSelection => true;

        [Unbound.EditorFaithful("this keyboard keeps its selection (see selection)")]
        public bool canSetSelection => true;

        public RangeInt selection
        {
            get => m_Selection;
            set => m_Selection = value;
        }

        public TouchScreenKeyboardType type => m_Type;

        public int targetDisplay { get; set; }
    }
}
