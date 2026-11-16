namespace UnityEngine
{
    /// <summary>What the numeric UI fields accept and how they read typed text (matches the Unity
    /// 6000.0.71f1 Editor, tests/projects/numericfields): an expression, or for float and double
    /// also "inf", "infinity" and "nan" in any case; an expression that needs a value (x, "+=", L,
    /// R) is applied to the field's text when the edit started. Integer and float results saturate
    /// at their type's range.</summary>
    internal static partial class UINumericFieldsUtils
    {
        public static readonly string k_AllowedCharactersForFloat = "inftynaeINFTYNAE0123456789.,-*/+%^()cosqrludxvRL=pP#";
        public static readonly string k_AllowedCharactersForInt = "0123456789-*/+%^()cosintaqrtelfundxvRL,=pPI#";
        public static readonly string k_DoubleFieldFormatString = "R";
        public static readonly string k_FloatFieldFormatString = "g7";
        public static readonly string k_IntFieldFormatString = "#######0";

        public static bool TryConvertStringToDouble(string str, out double value) => TryConvertStringToDouble(str, out value, out _);

        public static bool TryConvertStringToDouble(string str, out double value, out ExpressionEvaluator.Expression expr)
        {
            expr = null;
            switch (str.ToLower())
            {
                case "inf":
                case "infinity":
                    value = double.PositiveInfinity;
                    return true;
                case "-inf":
                case "-infinity":
                    value = double.NegativeInfinity;
                    return true;
                case "nan":
                    value = double.NaN;
                    return true;
            }
            return ExpressionEvaluator.Evaluate(str, out value, out expr);
        }

        public static bool TryConvertStringToDouble(string str, string initialValueAsString, out double value, out ExpressionEvaluator.Expression expression)
        {
            bool success = TryConvertStringToDouble(str, out value, out expression);
            return success || ApplyToInitialText(expression, initialValueAsString, TryConvertStringToDouble, ref value);
        }

        public static bool TryConvertStringToFloat(string str, string initialValueAsString, out float value, out ExpressionEvaluator.Expression expression)
        {
            bool success = TryConvertStringToDouble(str, initialValueAsString, out var v, out expression);
            value = Mathf.ClampToFloat(v);
            return success;
        }

        public static bool TryConvertStringToLong(string str, out long value) => ExpressionEvaluator.Evaluate(str, out value, out _);

        public static bool TryConvertStringToLong(string str, out long value, out ExpressionEvaluator.Expression expr) => ExpressionEvaluator.Evaluate(str, out value, out expr);

        public static bool TryConvertStringToLong(string str, string initialValueAsString, out long value, out ExpressionEvaluator.Expression expression)
        {
            bool success = TryConvertStringToLong(str, out value, out expression);
            return success || ApplyToInitialText(expression, initialValueAsString, TryConvertStringToLong, ref value);
        }

        public static bool TryConvertStringToULong(string str, out ulong value, out ExpressionEvaluator.Expression expr) => ExpressionEvaluator.Evaluate(str, out value, out expr);

        public static bool TryConvertStringToULong(string str, string initialValueAsString, out ulong value, out ExpressionEvaluator.Expression expression)
        {
            bool success = TryConvertStringToULong(str, out value, out expression);
            return success || ApplyToInitialText(expression, initialValueAsString, TryConvertStringToULong, ref value);
        }

        delegate bool Reader<T>(string text, out T value, out ExpressionEvaluator.Expression expression);

        /// <summary>An expression that needs a value, applied to the text the edit started from.</summary>
        static bool ApplyToInitialText<T>(ExpressionEvaluator.Expression expression, string initialValueAsString, Reader<T> read, ref T value)
        {
            if (expression == null || string.IsNullOrEmpty(initialValueAsString) || !read(initialValueAsString, out var initial, out _)) return false;
            value = initial;
            return expression.Evaluate(ref value);
        }

        public static bool TryConvertStringToInt(string str, string initialValueAsString, out int value, out ExpressionEvaluator.Expression expression)
        {
            bool success = TryConvertStringToLong(str, initialValueAsString, out var v, out expression);
            value = Mathf.ClampToInt(v);
            return success;
        }

        public static bool TryConvertStringToUInt(string str, string initialValueAsString, out uint value, out ExpressionEvaluator.Expression expression)
        {
            bool success = TryConvertStringToLong(str, initialValueAsString, out var v, out expression);
            value = Mathf.ClampToUInt(v);
            return success;
        }
    }
}
