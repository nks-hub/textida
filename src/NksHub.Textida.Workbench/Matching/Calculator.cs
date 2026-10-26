using System.Globalization;

namespace NksHub.Textida.Workbench.Matching;

public enum CalculatorError { None, Empty, Syntax, DivisionByZero, Overflow, TooComplex }

/// <summary>The outcome of <see cref="Calculator.Evaluate"/>: the value as text, or why there is none.</summary>
public readonly record struct CalculatorResult(CalculatorError Error, decimal Value)
{
    public bool Ok => Error == CalculatorError.None;

    /// <summary>The value without trailing zeros, in the invariant format (a dot), so it can be pasted into code.</summary>
    public string Text => Value.ToString("0.############", CultureInfo.InvariantCulture);
}

/// <summary>
/// The arithmetic of the palette (<c>=1+2*3</c>): numbers, <c>+ - * / % ^</c>, unary signs and parentheses, evaluated with decimals.
/// It is a parser of this small grammar and nothing else; the text is never compiled or run as code.
/// </summary>
public static class Calculator
{
    public const int MaxLength = 200;
    private const int MaxDepth = 40;

    public static CalculatorResult Evaluate(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return new(CalculatorError.Empty, 0);
        if (text.Length > MaxLength) return new(CalculatorError.TooComplex, 0);
        try
        {
            var parser = new Parser(text);
            var value = parser.Expression(0);
            parser.SkipSpaces();
            return parser.AtEnd ? new(CalculatorError.None, value) : new(CalculatorError.Syntax, 0);
        }
        catch (DivideByZeroException)
        {
            return new(CalculatorError.DivisionByZero, 0);
        }
        catch (OverflowException)
        {
            return new(CalculatorError.Overflow, 0);
        }
        catch (FormatException)
        {
            return new(CalculatorError.Syntax, 0);
        }
        catch (InvalidOperationException)
        {
            return new(CalculatorError.TooComplex, 0);
        }
    }

    private sealed class Parser(string text)
    {
        private int _at;

        public bool AtEnd => _at >= text.Length;

        public void SkipSpaces()
        {
            while (_at < text.Length && char.IsWhiteSpace(text[_at])) _at++;
        }

        private bool Take(char c)
        {
            SkipSpaces();
            if (_at >= text.Length || text[_at] != c) return false;
            _at++;
            return true;
        }

        public decimal Expression(int depth)
        {
            if (depth > MaxDepth) throw new InvalidOperationException();
            var value = Term(depth);
            while (true)
            {
                if (Take('+')) value += Term(depth);
                else if (Take('-')) value -= Term(depth);
                else return value;
            }
        }

        private decimal Term(int depth)
        {
            var value = Unary(depth);
            while (true)
            {
                if (Take('*')) value *= Unary(depth);
                else if (Take('/')) value /= Unary(depth);
                else if (Take('%')) value %= Unary(depth);
                else return value;
            }
        }

        private decimal Unary(int depth)
        {
            if (depth > MaxDepth) throw new InvalidOperationException();
            if (Take('-')) return -Unary(depth + 1);
            if (Take('+')) return Unary(depth + 1);
            var value = Atom(depth);
            return Take('^') ? Power(value, Unary(depth + 1)) : value;
        }

        private static decimal Power(decimal b, decimal e)
        {
            if (b == 0 && e < 0) throw new DivideByZeroException();
            double result = Math.Pow((double)b, (double)e);
            if (double.IsNaN(result) || double.IsInfinity(result)) throw new OverflowException();
            return (decimal)result;
        }

        private decimal Atom(int depth)
        {
            SkipSpaces();
            if (Take('('))
            {
                var inner = Expression(depth + 1);
                if (!Take(')')) throw new FormatException();
                return inner;
            }
            int start = _at;
            while (_at < text.Length && (char.IsAsciiDigit(text[_at]) || text[_at] == '.')) _at++;
            if (start == _at) throw new FormatException();
            return decimal.Parse(text.AsSpan(start, _at - start), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture);
        }
    }
}
