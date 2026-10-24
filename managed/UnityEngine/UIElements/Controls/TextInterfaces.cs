using System;

namespace UnityEngine.UIElements
{
    /// <summary>The selection of a selectable text element.</summary>
    public partial interface ITextSelection
    {
        bool isSelectable { get; set; }

        [Obsolete("cursorColor is deprecated. Please use the corresponding USS property (--unity-cursor-color) instead.")]
        Color cursorColor { get; set; }

        [Obsolete("selectionColor is deprecated. Please use the corresponding USS property (--unity-selection-color) instead.")]
        Color selectionColor { get; set; }

        int cursorIndex { get; set; }
        bool doubleClickSelectsWord { get; set; }
        int selectIndex { get; set; }
        bool tripleClickSelectsLine { get; set; }
        bool selectAllOnFocus { get; set; }
        bool selectAllOnMouseUp { get; set; }
        Vector2 cursorPosition { get; }
        internal float lineHeightAtCursorPosition { get; }
        internal float cursorWidth { get; set; }

        bool HasSelection();
        void SelectAll();
        void SelectNone();
        void SelectRange(int cursorIndex, int selectionIndex);
        internal void MoveTextEnd();
    }

    /// <summary>The edition settings and hooks of an editable text element.</summary>
    public partial interface ITextEdition
    {
        internal bool multiline { get; set; }
        bool isReadOnly { get; set; }
        int maxLength { get; set; }
        string placeholder { get; set; }
        bool isDelayed { get; set; }
        internal Func<char, bool> AcceptCharacter { get; set; }
        internal Action<bool> UpdateScrollOffset { get; set; }
        internal Action UpdateValueFromText { get; set; }
        internal Action UpdateTextFromValue { get; set; }
        internal Action MoveFocusToCompositeRoot { get; set; }
        internal Func<string> GetDefaultValueType { get; set; }
        char maskChar { get; set; }
        bool isPassword { get; set; }
        bool hidePlaceholderOnFocus { get; set; }

        bool autoCorrection
        {
            get
            {
                Debug.Log("Type " + GetType().Name + " implementing interface ITextEdition is missing the implementation for autoCorrection. Calling ITextEdition.autoCorrection of this type will always return false.");
                return false;
            }
            set => Debug.Log("Type " + GetType().Name + " implementing interface ITextEdition is missing the implementation for autoCorrection. Assigning a value to ITextEdition.autoCorrection will not update its value.");
        }

        bool hideMobileInput
        {
            get
            {
                Debug.Log("Type " + GetType().Name + " implementing interface ITextEdition is missing the implementation for hideMobileInput. Calling ITextEdition.hideMobileInput of this type will always return false.");
                return false;
            }
            set => Debug.Log("Type " + GetType().Name + " implementing interface ITextEdition is missing the implementation for hideMobileInput. Assigning a value to ITextEdition.hideMobileInput will not update its value.");
        }

        TouchScreenKeyboard touchScreenKeyboard
        {
            get
            {
                Debug.Log("Type " + GetType().Name + " implementing interface ITextEdition is missing the implementation for touchScreenKeyboard. Calling ITextEdition.touchScreenKeyboard of this type will always return null.");
                return null;
            }
        }

        TouchScreenKeyboardType keyboardType
        {
            get
            {
                Debug.Log("Type " + GetType().Name + " implementing interface ITextEdition is missing the implementation for keyboardType. Calling ITextEdition.keyboardType of this type will always return Default.");
                return TouchScreenKeyboardType.Default;
            }
            set => Debug.Log("Type " + GetType().Name + " implementing interface ITextEdition is missing the implementation for keyboardType. Assigning a value to ITextEdition.keyboardType will not update its value.");
        }

        internal void ResetValueAndText();
        internal void SaveValueAndText();
        internal void RestoreValueAndText();
        internal void UpdateText(string value);
        internal string CullString(string s);
    }

    public static partial class INotifyValueChangedExtensions
    {
        public static bool RegisterValueChangedCallback<T>(this INotifyValueChanged<T> control, EventCallback<ChangeEvent<T>> callback)
        {
            if (!(control is CallbackEventHandler handler)) return false;
            handler.RegisterCallback(callback);
            return true;
        }

        public static bool UnregisterValueChangedCallback<T>(this INotifyValueChanged<T> control, EventCallback<ChangeEvent<T>> callback)
        {
            if (!(control is CallbackEventHandler handler)) return false;
            handler.UnregisterCallback(callback);
            return true;
        }
    }
}
