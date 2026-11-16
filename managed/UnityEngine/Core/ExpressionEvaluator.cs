using System;
using System.Collections.Generic;
using System.Globalization;

namespace UnityEngine
{
    /// <summary>
    /// Evaluates the arithmetic typed into numeric fields (Unity 6000.0 Scripting API
    /// ExpressionEvaluator.Evaluate: "Evaluates a math expression from a string"). Operators
    /// + - * / % ^ and parentheses, functions sqrt, sin, cos, tan, floor, ceil, round and the
    /// two-argument L (linear ramp over an index) and R (random); the names x, v, f and # stand for
    /// a value given later, as do L and R, so such an expression is kept for later instead of being
    /// evaluated; "+=", "-=", "*=" and "/=" prefixes apply the rest to that value.
    ///
    /// Behaviour matches the Unity 6000.0.71f1 Editor (tests/projects/numericfields): expressions are
    /// split at operators, parentheses, commas and spaces; a minus after an operator or an opening
    /// parenthesis (or first) negates; operators then go to postfix order by precedence (functions
    /// 4, ^ and negation 5 with ^ right-associative and negation left-associative, * / % 3, + - 2);
    /// each intermediate result passes through its decimal text as the Editor's Mono runtime prints
    /// it (15 significant digits), and a literal outside the type's range does not parse.
    /// </summary>
    public partial class ExpressionEvaluator
    {
        /// <summary>A parsed expression in postfix order, evaluated later against a value.</summary>
        internal partial class Expression
        {
            internal readonly string[] rpnTokens;
            internal readonly bool hasVariables;

            internal Expression(string expression)
            {
                var tokens = Tokenize(Normalize(expression), out hasVariables);
                MarkNegations(tokens);
                rpnTokens = ToPostfix(tokens);
            }

            public bool Evaluate<T>(ref T value, int index = 0, int count = 1) => EvaluateAs(rpnTokens, ref value, index, count);

            public override bool Equals(object obj) =>
                obj is Expression other && ((ReadOnlySpan<string>)rpnTokens).SequenceEqual(other.rpnTokens);

            public override int GetHashCode() => rpnTokens.GetHashCode();

            public override string ToString() => string.Join(" ", rpnTokens);
        }

        enum Op { Add, Sub, Mul, Div, Mod, Neg, Pow, Sqrt, Sin, Cos, Tan, Floor, Ceil, Round, Rand, Linear }

        sealed class Operator
        {
            internal readonly Op op;
            internal readonly int precedence;
            internal readonly int arity;
            internal readonly bool rightAssociative;

            internal Operator(Op op, int precedence, int arity, bool rightAssociative = false)
            {
                this.op = op;
                this.precedence = precedence;
                this.arity = arity;
                this.rightAssociative = rightAssociative;
            }
        }

        /// <summary>The negation operator's token; a "-" read as negation is replaced with it.</summary>
        const string Negation = "_";

        static readonly Dictionary<string, Operator> s_Operators = new Dictionary<string, Operator>
        {
            ["-"] = new Operator(Op.Sub, 2, 2),
            ["+"] = new Operator(Op.Add, 2, 2),
            ["/"] = new Operator(Op.Div, 3, 2),
            ["*"] = new Operator(Op.Mul, 3, 2),
            ["%"] = new Operator(Op.Mod, 3, 2),
            ["^"] = new Operator(Op.Pow, 5, 2, rightAssociative: true),
            [Negation] = new Operator(Op.Neg, 5, 1),
            ["sqrt"] = new Operator(Op.Sqrt, 4, 1),
            ["cos"] = new Operator(Op.Cos, 4, 1),
            ["sin"] = new Operator(Op.Sin, 4, 1),
            ["tan"] = new Operator(Op.Tan, 4, 1),
            ["floor"] = new Operator(Op.Floor, 4, 1),
            ["ceil"] = new Operator(Op.Ceil, 4, 1),
            ["round"] = new Operator(Op.Round, 4, 1),
            ["R"] = new Operator(Op.Rand, 4, 2),
            ["L"] = new Operator(Op.Linear, 4, 2),
        };

        static Pcg32 s_Random = new Pcg32(0);

        public static bool Evaluate<T>(string expression, out T value) => Evaluate(expression, out value, out _);

        /// <summary>Evaluates <paramref name="expression"/>; one that refers to a value given later
        /// (x, v, f, #, L, R) is not evaluated and comes back in <paramref name="delayed"/>.</summary>
        internal static bool Evaluate<T>(string expression, out T value, out Expression delayed)
        {
            delayed = null;
            if (TryParseNumber(expression, out value)) return true;
            var parsed = new Expression(expression);
            value = default;
            if (parsed.hasVariables)
            {
                delayed = parsed;
                return false;
            }
            return EvaluateAs(parsed.rpnTokens, ref value, 0, 1);
        }

        internal static void SetRandomState(uint state) => s_Random = new Pcg32(state);

        // ---- parsing ----

        /// <summary>Trims the text, drops one trailing run of an operator character (a sum still
        /// being typed) and turns "op=rest" into "x op (rest)".</summary>
        static string Normalize(string expression)
        {
            var text = expression.Trim();
            if (text.Length == 0) return text;
            char last = text[text.Length - 1];
            if (s_Operators.ContainsKey(last.ToString())) text = text.TrimEnd(last);
            if (text.Length >= 2 && text[1] == '=')
            {
                string rest = text.Substring(2);
                switch (text[0])
                {
                    case '+': return "x+(" + rest + ")";
                    case '-': return "x-(" + rest + ")";
                    case '*': return "x*(" + rest + ")";
                    case '/': return "x/(" + rest + ")";
                }
            }
            return text;
        }

        static bool IsSeparatorOrOperator(string token) =>
            token == "(" || token == ")" || token == "," || s_Operators.ContainsKey(token);

        static bool IsVariable(string token) => token == "x" || token == "v" || token == "f" || token == "#";

        static bool IsDelayedFunction(string token) =>
            s_Operators.TryGetValue(token, out var o) && (o.op == Op.Rand || o.op == Op.Linear);

        /// <summary>Splits at every single-character operator, parenthesis and comma (each its own
        /// token) and at spaces; the other characters run together into one token.</summary>
        static List<string> Tokenize(string text, out bool hasVariables)
        {
            var tokens = new List<string>();
            int start = -1;
            for (int i = 0; i < text.Length; i++)
            {
                string single = text[i].ToString();
                bool separator = IsSeparatorOrOperator(single);
                if (separator || text[i] == ' ')
                {
                    if (start >= 0) tokens.Add(text.Substring(start, i - start));
                    start = -1;
                    if (separator) tokens.Add(single);
                }
                else if (start < 0)
                {
                    start = i;
                }
            }
            if (start >= 0) tokens.Add(text.Substring(start));
            hasVariables = tokens.Exists(t => IsVariable(t) || IsDelayedFunction(t));
            return tokens;
        }

        /// <summary>A leading "-", and a "-" after an operator, comma or "(", negates.</summary>
        static void MarkNegations(List<string> tokens)
        {
            if (tokens.Count == 0) return;
            if (tokens[0] == "-") tokens[0] = Negation;
            for (int i = 1; i < tokens.Count - 1; i++)
                if (tokens[i] == "-" && IsSeparatorOrOperator(tokens[i - 1]) && tokens[i - 1] != ")")
                    tokens[i] = Negation;
        }

        /// <summary>Infix to postfix by precedence (the shunting-yard method); the delayed functions
        /// wait on the operator stack for their closing parenthesis.</summary>
        static string[] ToPostfix(List<string> tokens)
        {
            var pending = new Stack<string>();
            var output = new List<string>(tokens.Count);
            foreach (var token in tokens)
            {
                if (token == "(")
                {
                    pending.Push(token);
                }
                else if (token == ")")
                {
                    while (pending.Count > 0 && pending.Peek() != "(") output.Add(pending.Pop());
                    if (pending.Count > 0) pending.Pop();
                    if (pending.Count > 0 && IsDelayedFunction(pending.Peek())) output.Add(pending.Pop());
                }
                else if (token == ",")
                {
                    while (pending.Count > 0 && pending.Peek() != "(") output.Add(pending.Pop());
                }
                else if (s_Operators.TryGetValue(token, out var incoming))
                {
                    while (pending.Count > 0 && s_Operators.TryGetValue(pending.Peek(), out var top) &&
                           (incoming.rightAssociative ? incoming.precedence < top.precedence : incoming.precedence <= top.precedence))
                        output.Add(pending.Pop());
                    pending.Push(token);
                }
                else if (IsDelayedFunction(token))
                {
                    pending.Push(token);
                }
                else
                {
                    output.Add(token);
                }
            }
            while (pending.Count > 0) output.Add(pending.Pop());
            return output.ToArray();
        }

        // ---- evaluation ----

        /// <summary>Evaluates in double precision from <paramref name="value"/> and converts back to
        /// T; an unsigned result below zero becomes 0.</summary>
        static bool EvaluateAs<T>(string[] tokens, ref T value, int index, int count)
        {
            double v;
            bool ok;
            switch (value)
            {
                case float f:
                    v = f;
                    ok = EvaluatePostfix(tokens, ref v, index, count);
                    value = (T)(object)(float)v;
                    return ok;
                case int n:
                    v = n;
                    ok = EvaluatePostfix(tokens, ref v, index, count);
                    value = (T)(object)(int)v;
                    return ok;
                case long l:
                    v = l;
                    ok = EvaluatePostfix(tokens, ref v, index, count);
                    value = (T)(object)(long)v;
                    return ok;
                case ulong u:
                    v = u;
                    ok = EvaluatePostfix(tokens, ref v, index, count);
                    value = (T)(object)(ulong)Math.Max(v, 0d);
                    return ok;
                case double d:
                    v = d;
                    ok = EvaluatePostfix(tokens, ref v, index, count);
                    value = (T)(object)v;
                    return ok;
            }
            return false;
        }

        /// <summary>Runs the postfix tokens on a stack of decimal texts, as the release does: each
        /// result is printed and parsed again, so it keeps the printed digits.</summary>
        static bool EvaluatePostfix(string[] tokens, ref double value, int index, int count)
        {
            var stack = new Stack<string>();
            var operands = new List<double>(2);
            foreach (var token in tokens)
            {
                if (s_Operators.TryGetValue(token, out var op))
                {
                    operands.Clear();
                    bool numbers = true;
                    while (stack.Count > 0 && !IsSeparatorOrOperator(stack.Peek()) && operands.Count < op.arity)
                    {
                        numbers &= TryParseNumber(stack.Pop(), out double operand);
                        operands.Add(operand);
                    }
                    if (!numbers || operands.Count != op.arity) return false;
                    operands.Reverse();
                    stack.Push(Print(Apply(op.op, operands, index, count)));
                }
                else if (IsVariable(token))
                {
                    stack.Push(token == "#" ? index.ToString(CultureInfo.InvariantCulture) : Print(value));
                }
                else
                {
                    stack.Push(token);
                }
            }
            return stack.Count == 1 && TryParseNumber(stack.Pop(), out value);
        }

        static string Print(double value) => Unbound.MonoNumberFormat.Format(value, null, CultureInfo.InvariantCulture);

        static double Apply(Op op, List<double> operands, int index, int count)
        {
            double a = operands.Count >= 1 ? operands[0] : 0;
            double b = operands.Count >= 2 ? operands[1] : 0;
            switch (op)
            {
                case Op.Neg: return -a;
                case Op.Add: return a + b;
                case Op.Sub: return a - b;
                case Op.Mul: return a * b;
                case Op.Div: return a / b;
                case Op.Mod: return a % b;
                case Op.Pow: return Math.Pow(a, b);
                case Op.Sqrt: return a <= 0 ? 0 : Math.Sqrt(a);
                case Op.Floor: return Math.Floor(a);
                case Op.Ceil: return Math.Ceiling(a);
                case Op.Round: return Math.Round(a);
                case Op.Cos: return Math.Cos(a);
                case Op.Sin: return Math.Sin(a);
                case Op.Tan: return Math.Tan(a);
                case Op.Rand:
                {
                    // 24 random bits as a fraction of their range.
                    const uint Bits24 = 0xFFFFFF;
                    double fraction = (s_Random.Next() & Bits24) / (double)Bits24;
                    return a + fraction * (b - a);
                }
                case Op.Linear:
                {
                    if (count < 1) count = 1;
                    double fraction = count < 2 ? 0.5 : index / (double)(count - 1);
                    return a + fraction * (b - a);
                }
            }
            return 0;
        }

        /// <summary>Reads one number of type T: a comma counts as the decimal point, a trailing f, d
        /// or l after a digit is dropped, "pi" names π for float and double, the empty text is 0, and
        /// a value beyond the type's range (an infinite float or double) does not parse.</summary>
        static bool TryParseNumber<T>(string text, out T result)
        {
            result = default;
            var lower = text.Replace(',', '.').ToLowerInvariant();
            if (lower.Length > 1 && char.IsDigit(lower[lower.Length - 2])) lower = lower.TrimEnd('f', 'd', 'l');
            if (lower.Length == 0) return true;
            var number = CultureInfo.InvariantCulture.NumberFormat;
            if (typeof(T) == typeof(float))
            {
                bool ok = TryParseFinite(lower, out double d);
                result = (T)(object)(ok ? (float)d : 0f);
                return ok;
            }
            if (typeof(T) == typeof(double))
            {
                bool ok = TryParseFinite(lower, out double d);
                result = (T)(object)(ok ? d : 0d);
                return ok;
            }
            if (typeof(T) == typeof(int))
            {
                bool ok = int.TryParse(lower, NumberStyles.Integer, number, out var n);
                result = (T)(object)n;
                return ok;
            }
            if (typeof(T) == typeof(long))
            {
                bool ok = long.TryParse(lower, NumberStyles.Integer, number, out var l);
                result = (T)(object)l;
                return ok;
            }
            if (typeof(T) == typeof(ulong))
            {
                bool ok = ulong.TryParse(lower, NumberStyles.Integer, number, out var u);
                result = (T)(object)u;
                return ok;
            }
            return false;
        }

        /// <summary>A finite decimal number or "pi"; a float is read as a double and rounded, as on the
        /// release's runtime. That runtime reads no infinity or NaN from lower-case text and fails on
        /// a literal beyond the double range, where .NET reads infinity.</summary>
        static bool TryParseFinite(string lower, out double value)
        {
            if (lower == "pi")
            {
                value = Math.PI;
                return true;
            }
            return double.TryParse(lower, NumberStyles.Float, CultureInfo.InvariantCulture.NumberFormat, out value) && double.IsFinite(value);
        }

        /// <summary>The PCG32 (XSH RR) generator R draws from.</summary>
        struct Pcg32
        {
            const ulong Multiplier = 6364136223846793005ul;
            readonly ulong m_Increment;
            ulong m_State;

            internal Pcg32(ulong seed, ulong sequence = 0)
            {
                m_Increment = (sequence << 1) | 1u;
                m_State = 0;
                Advance();
                m_State += seed;
                Advance();
            }

            void Advance() => m_State = unchecked(m_State * Multiplier + m_Increment);

            internal uint Next()
            {
                ulong old = m_State;
                Advance();
                uint xorShifted = (uint)(((old >> 18) ^ old) >> 27);
                int rotation = (int)(old >> 59);
                return (xorShifted >> rotation) | (xorShifted << (-rotation & 31));
            }
        }
    }
}
