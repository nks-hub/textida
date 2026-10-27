using System.Globalization;
using System.Runtime.CompilerServices;

namespace NksHub.Textida.Plugins.TextTools;

public enum ExpressionError { Syntax, UnknownName, Domain, Range, DivisionByZero }

public sealed class ExpressionException(ExpressionError error, int position, string detail) : Exception($"{error} at {position}: {detail}")
{
    public ExpressionError Error { get; } = error;
    /// <summary>Zero-based index in the text where the problem starts.</summary>
    public int Position { get; } = position;
    /// <summary>The name that is not known, or the name of the function whose argument is out of range.</summary>
    public string Detail { get; } = detail;
}

/// <summary>
/// Evaluates an arithmetic expression without running anything: a hand-written parser over numbers, <c>+ - * / ^ ( )</c>, <c>MOD</c> and the functions
/// ABS ATAN COS EXP LN ROUND SIN SQRT SQR TRUNC. Any other name is refused, so a call to something of the system never gets anywhere.
/// <c>^</c> takes a positive base only, <c>MOD</c> rounds both sides to whole numbers, and arguments must lie in the ranges the original program documents
/// where they fit a double (it computes with the wider Extended type, so results beyond the double range are an error here). Numbers are written with a dot.
/// </summary>
public static class ExpressionEvaluator
{
    private static readonly string[] Functions = ["ABS", "ATAN", "COS", "EXP", "LN", "ROUND", "SIN", "SQRT", "SQR", "TRUNC"];

    public static double Evaluate(string text) => new Parser(text).ParseAll();

    private sealed class Parser(string text)
    {
        private int _i;

        public double ParseAll()
        {
            var v = Expr();
            SkipSpace();
            if (_i < text.Length) throw new ExpressionException(ExpressionError.Syntax, _i, text[_i].ToString());
            return Check(v, _i);
        }

        private void SkipSpace()
        {
            while (_i < text.Length && char.IsWhiteSpace(text[_i])) _i++;
        }

        private char Peek()
        {
            SkipSpace();
            return _i < text.Length ? text[_i] : '\0';
        }

        private static double Check(double v, int at) =>
            double.IsFinite(v) ? v : throw new ExpressionException(ExpressionError.Range, at, "");

        private double Expr()
        {
            RuntimeHelpers.EnsureSufficientExecutionStack();
            var left = Term();
            while (Peek() is '+' or '-')
            {
                char op = text[_i++];
                var right = Term();
                left = Check(op == '+' ? left + right : left - right, _i);
            }
            return left;
        }

        private double Term()
        {
            var left = Unary();
            while (true)
            {
                char c = Peek();
                int at = _i;
                if (c == '*') { _i++; left = Check(left * Unary(), at); }
                else if (c == '/')
                {
                    _i++;
                    var right = Unary();
                    if (right == 0) throw new ExpressionException(ExpressionError.DivisionByZero, at, "");
                    left = Check(left / right, at);
                }
                else if (IsWordAt("MOD"))
                {
                    _i += 3;
                    left = Mod(left, Unary(), at);
                }
                else return left;
            }
        }

        private bool IsWordAt(string word) =>
            string.Compare(text, _i, word, 0, word.Length, StringComparison.OrdinalIgnoreCase) == 0
            && (_i + word.Length == text.Length || !char.IsLetterOrDigit(text[_i + word.Length]));

        private static double Mod(double a, double b, int at)
        {
            var x = Math.Round(a);
            var y = Math.Round(b);
            if (Math.Abs(x) >= 9.2e18 || Math.Abs(y) >= 9.2e18) throw new ExpressionException(ExpressionError.Range, at, "MOD");
            if (y == 0) throw new ExpressionException(ExpressionError.DivisionByZero, at, "MOD");
            return (long)x % (long)y;
        }

        private double Unary()
        {
            RuntimeHelpers.EnsureSufficientExecutionStack();
            char c = Peek();
            if (c == '-') { _i++; return -Unary(); }
            if (c == '+') { _i++; return Unary(); }
            return Power();
        }

        private double Power()
        {
            var b = Primary();
            if (Peek() != '^') return b;
            int at = _i++;
            var e = Unary();
            if (b <= 0) throw new ExpressionException(ExpressionError.Domain, at, "^");
            return Check(Math.Pow(b, e), at);
        }

        private double Primary()
        {
            char c = Peek();
            int at = _i;
            if (c == '(')
            {
                _i++;
                var v = Expr();
                if (Peek() != ')') throw new ExpressionException(ExpressionError.Syntax, _i, ")");
                _i++;
                return v;
            }
            if (char.IsAsciiDigit(c) || c == '.') return Number();
            if (char.IsAsciiLetter(c) || c == '_')
            {
                int start = _i;
                while (_i < text.Length && (char.IsAsciiLetterOrDigit(text[_i]) || text[_i] == '_')) _i++;
                var name = text[start.._i];
                if (Peek() != '(') throw new ExpressionException(ExpressionError.UnknownName, start, name);
                if (!Functions.Contains(name, StringComparer.OrdinalIgnoreCase)) throw new ExpressionException(ExpressionError.UnknownName, start, name);
                _i++;
                var arg = Expr();
                if (Peek() != ')') throw new ExpressionException(ExpressionError.Syntax, _i, ")");
                _i++;
                return Function(name, arg, start);
            }
            throw new ExpressionException(ExpressionError.Syntax, at, c == '\0' ? "" : c.ToString());
        }

        private double Number()
        {
            int start = _i;
            while (_i < text.Length && (char.IsAsciiDigit(text[_i]) || text[_i] == '.')) _i++;
            if (_i < text.Length && text[_i] is 'e' or 'E')
            {
                int save = _i++;
                if (_i < text.Length && text[_i] is '+' or '-') _i++;
                if (_i < text.Length && char.IsAsciiDigit(text[_i]))
                    while (_i < text.Length && char.IsAsciiDigit(text[_i])) _i++;
                else _i = save;
            }
            if (!double.TryParse(text.AsSpan(start, _i - start), NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
                throw new ExpressionException(ExpressionError.Syntax, start, text[start.._i]);
            return Check(v, start);
        }

        private static double Function(string name, double x, int at)
        {
            double Limited(double limit) => Math.Abs(x) <= limit ? x : throw new ExpressionException(ExpressionError.Range, at, name.ToUpperInvariant());
            switch (name.ToUpperInvariant())
            {
                case "ABS": return Math.Abs(x);
                case "ATAN": return Math.Atan(x);
                case "COS": return Math.Cos(Limited(1e18));
                case "SIN": return Math.Sin(Limited(1e18));
                case "EXP": return Check(Math.Exp(x), at);
                case "LN": return x > 0 ? Math.Log(x) : throw new ExpressionException(ExpressionError.Domain, at, "LN");
                case "ROUND": return Math.Round(Limited(1e9));
                case "TRUNC": return Math.Truncate(Limited(1e9));
                case "SQRT": return x >= 0 ? Math.Sqrt(x) : throw new ExpressionException(ExpressionError.Domain, at, "SQRT");
                case "SQR": return Check(x * x, at);
                default: throw new ExpressionException(ExpressionError.UnknownName, at, name);
            }
        }
    }
}
