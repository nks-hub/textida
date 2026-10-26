using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace NksHub.Textida.App.Tests;

/// <summary>
/// Finds user-visible text written straight into code. A violation is a string literal containing a letter that ends up in
/// a UI property (<c>Text</c>, <c>Content</c>, <c>Header</c>, <c>Title</c> and similar), in an accessible name, in a menu constructor,
/// or a bare key passed to <c>Loc.Get</c> / <c>Loc.Plural</c> instead of a <c>K</c> constant. Text read through <c>Loc.*</c> is allowed.
/// </summary>
internal static class UiLiteralScanner
{
    private static readonly HashSet<string> UiProperties = ["Text", "Content", "Header", "Title", "PlaceholderText", "Watermark", "Label", "Tip", "Description"];
    private static readonly HashSet<string> UiCalls = ["SetName", "SetHelpText", "SetTip", "SetAccessKey", "SetLocalizedControlType"];
    private static readonly HashSet<string> UiConstructors = ["NativeMenuItem", "MenuCommand", "MenuGroup", "Run", "AccessText"];

    public sealed record Violation(string File, int Line, string Text, string Reason)
    {
        public override string ToString() => $"{File}:{Line}: {Reason}: \"{Text}\"";
    }

    public static IReadOnlyList<Violation> ScanSource(string file, string source)
    {
        var result = new List<Violation>();
        var root = CSharpSyntaxTree.ParseText(source).GetRoot();

        foreach (var node in root.DescendantNodes())
        {
            switch (node)
            {
                case AssignmentExpressionSyntax a when PropertyName(a.Left) is { } name && UiProperties.Contains(name):
                    Collect(a.Right, $"assigned to {name}");
                    break;
                case InvocationExpressionSyntax inv when MethodName(inv) is { } m && UiCalls.Contains(m):
                    foreach (var arg in inv.ArgumentList.Arguments) Collect(arg.Expression, $"argument of {m}");
                    break;
                case InvocationExpressionSyntax inv when IsLocKeyCall(inv) && inv.ArgumentList.Arguments.Count > 0 && inv.ArgumentList.Arguments[0].Expression is LiteralExpressionSyntax key:
                    result.Add(new Violation(file, Line(key), key.Token.ValueText, "bare key, use a K constant"));
                    break;
                case BaseObjectCreationExpressionSyntax create when TypeName(create) is { } t && UiConstructors.Contains(t) && create.ArgumentList is { } args:
                    foreach (var arg in args.Arguments) Collect(arg.Expression, $"constructor of {t}");
                    break;
            }
        }
        return result;

        void Collect(ExpressionSyntax expr, string reason)
        {
            foreach (var token in Literals(expr))
                if (token.Text.Any(char.IsLetter)) result.Add(new Violation(file, Line(token.Node), token.Text, reason));
        }

        int Line(SyntaxNode n) => n.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
    }

    /// <summary>
    /// Text that would reach the UI as part of the expression itself: literals, interpolated text, concatenation and conditionals.
    /// Calls, lambdas, collections and nested constructors are not entered, so a tag passed to a helper is not mistaken for UI text.
    /// </summary>
    private static IEnumerable<(string Text, SyntaxNode Node)> Literals(SyntaxNode expr)
    {
        switch (expr)
        {
            case LiteralExpressionSyntax lit when lit.IsKind(SyntaxKind.StringLiteralExpression):
                yield return (lit.Token.ValueText, lit);
                break;
            case InterpolatedStringExpressionSyntax interp:
                foreach (var text in interp.Contents.OfType<InterpolatedStringTextSyntax>()) yield return (text.TextToken.ValueText, text);
                break;
            case BinaryExpressionSyntax bin when bin.IsKind(SyntaxKind.AddExpression):
                foreach (var l in Literals(bin.Left)) yield return l;
                foreach (var r in Literals(bin.Right)) yield return r;
                break;
            case ConditionalExpressionSyntax cond:
                foreach (var l in Literals(cond.WhenTrue)) yield return l;
                foreach (var r in Literals(cond.WhenFalse)) yield return r;
                break;
            case ParenthesizedExpressionSyntax paren:
                foreach (var l in Literals(paren.Expression)) yield return l;
                break;
        }
    }

    private static string? PropertyName(ExpressionSyntax e) => e switch
    {
        IdentifierNameSyntax id => id.Identifier.Text,
        MemberAccessExpressionSyntax ma => ma.Name.Identifier.Text,
        _ => null,
    };

    private static string? MethodName(InvocationExpressionSyntax inv) => inv.Expression switch
    {
        MemberAccessExpressionSyntax ma => ma.Name.Identifier.Text,
        IdentifierNameSyntax id => id.Identifier.Text,
        _ => null,
    };

    private static string? TypeName(BaseObjectCreationExpressionSyntax c) => c is ObjectCreationExpressionSyntax o ? o.Type switch
    {
        IdentifierNameSyntax id => id.Identifier.Text,
        QualifiedNameSyntax q => q.Right.Identifier.Text,
        _ => null,
    } : null;

    private static bool IsLocCall(InvocationExpressionSyntax inv) => inv.Expression is MemberAccessExpressionSyntax { Expression: IdentifierNameSyntax { Identifier.Text: "Loc" } };

    private static bool IsLocKeyCall(InvocationExpressionSyntax inv) => IsLocCall(inv) && inv.Expression is MemberAccessExpressionSyntax { Name.Identifier.Text: "Get" or "Plural" };

    private static readonly Regex AxamlText = new(@"\b(?:Text|Content|Header|Title|Watermark|PlaceholderText|ToolTip\.Tip|AutomationProperties\.Name)\s*=\s*""(?<v>[^""{][^""]*)""", RegexOptions.Compiled);

    /// <summary>Same rule for markup: a plain attribute value with a letter in a UI attribute. Bindings and resource references start with a brace and pass.</summary>
    public static IReadOnlyList<Violation> ScanAxaml(string file, string xaml)
    {
        var result = new List<Violation>();
        foreach (Match m in AxamlText.Matches(xaml))
        {
            var v = m.Groups["v"].Value;
            if (v.Any(char.IsLetter)) result.Add(new Violation(file, xaml[..m.Index].Count(c => c == '\n') + 1, v, "hard-coded markup text"));
        }
        return result;
    }
}
