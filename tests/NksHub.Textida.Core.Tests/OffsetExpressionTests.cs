using NksHub.Textida.Core.Documents;
using NksHub.Textida.Core.Encodings;
using NksHub.Textida.Core.Hex;
using Doc = NksHub.Textida.Core.Documents.Document;

namespace NksHub.Textida.Core.Tests;

public class OffsetExpressionTests
{
    private static readonly OffsetContext TenGiB = new(10L << 30, 1024, 512, 8000);

    [Theory]
    [InlineData("0x1F40", 8000)]
    [InlineData("1F40h", 8000)]
    [InlineData("$1F40", 8000)]
    [InlineData("8000", 8000)]
    [InlineData("+0x100", 1280)]
    [InlineData("-16", 1008)]
    [InlineData("end-512", (10L << 30) - 512)]
    [InlineData("50%", 5L << 30)]
    [InlineData("4K", 4096)]
    [InlineData("1M", 1L << 20)]
    [InlineData("1G", 1L << 30)]
    [InlineData("(4 + 2) * 10 / 3", 20)]
    [InlineData("sel", 512)]
    [InlineData("val", 8000)]
    [InlineData("  + (2 * 0x100)  ", 1536)]
    [InlineData("end", 10L << 30)]
    [InlineData("100%", 10L << 30)]
    [InlineData("0", 0)]
    [InlineData("0X1f40 + $a + 10H", 8026)]
    [InlineData("2+3*4", 14)]
    [InlineData("20/3", 6)]
    [InlineData("(100%-512)/2", ((10L << 30) - 512) / 2)]
    public void Resolves_specification_examples_and_arithmetic(string expression, long expected) =>
        Assert.Equal(expected, OffsetExpression.Evaluate(expression, TenGiB));

    [Fact]
    public void Percentage_rounds_down_without_overflowing_the_intermediate_product()
    {
        Assert.Equal(2, OffsetExpression.Evaluate("50%", new OffsetContext(5, 0, 0)));
        Assert.Equal(2, OffsetExpression.Evaluate("(0-50)%+end", new OffsetContext(5, 0, 0)));
        Assert.Equal(long.MaxValue, OffsetExpression.Evaluate("100%", new OffsetContext(long.MaxValue, 0, 0)));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("0x")]
    [InlineData("$")]
    [InlineData("1F40")]
    [InlineData("0x1G")]
    [InlineData("K")]
    [InlineData("1.5K")]
    [InlineData("1 2")]
    [InlineData("(16")]
    [InlineData("16)")]
    [InlineData("3+")]
    [InlineData("3**2")]
    [InlineData("50%%")]
    [InlineData("other")]
    public void Rejects_invalid_input(string expression) =>
        Assert.Throws<FormatException>(() => OffsetExpression.Evaluate(expression, TenGiB));

    [Theory]
    [InlineData("end+1")]
    [InlineData("(-1)")]
    [InlineData("-1025")]
    [InlineData("101%")]
    public void Rejects_targets_outside_the_document(string expression) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => OffsetExpression.Evaluate(expression, TenGiB));

    [Theory]
    [InlineData("9223372036854775808")]
    [InlineData("0xFFFFFFFFFFFFFFFF")]
    [InlineData("9223372036854775807+1")]
    [InlineData("9223372036854775807K")]
    [InlineData("9223372036854775807*2")]
    [InlineData("(-9223372036854775807-1)/(-1)")]
    public void Rejects_overflow(string expression) =>
        Assert.Throws<OverflowException>(() => OffsetExpression.Evaluate(expression, TenGiB));

    [Fact]
    public void Rejects_division_by_zero() =>
        Assert.Throws<DivideByZeroException>(() => OffsetExpression.Evaluate("1/(2-2)", TenGiB));

    [Fact]
    public void Leading_sign_is_relative_but_parenthesized_arithmetic_is_absolute()
    {
        Assert.Equal(1008, OffsetExpression.Evaluate("-(2*8)", TenGiB));
        Assert.Equal(16, OffsetExpression.Evaluate("(+16)", TenGiB));
    }

    [Fact]
    public void Empty_document_accepts_eof_but_has_no_value()
    {
        using var document = Doc.FromBytes(Array.Empty<byte>(), PagedBackendFactory.Instance);
        Assert.Equal(0, OffsetExpression.Evaluate("end", document.GetSnapshot(), 0));
        Assert.Throws<FormatException>(() => OffsetExpression.Evaluate("val", document.GetSnapshot(), 0));
    }

    [Fact]
    public void Value_reads_four_little_endian_bytes_of_the_selected_revision()
    {
        byte[] bytes = new byte[1024];
        bytes[4] = 0x02;
        bytes[5] = 0x01;
        using var document = Doc.FromBytes(bytes, PagedBackendFactory.Instance, new DocumentOptions { Format = TextCodec.Cp1252 });
        var before = document.GetSnapshot();
        Assert.True(document.Replace(new ByteRange(4, 8), new byte[] { 0x03, 0, 0, 0 }).Applied);
        Assert.Equal(258, OffsetExpression.Evaluate("val", before, 4));
        Assert.Equal(3, OffsetExpression.Evaluate("val", document.GetSnapshot(), 4));
        Assert.Equal(123, OffsetExpression.Evaluate("sel", before, 4, 123));
        Assert.Throws<FormatException>(() => OffsetExpression.Evaluate("val", before, 1021));
        Assert.Equal(1024, OffsetExpression.Evaluate("end", before, 1024));
    }

    [Fact]
    public void Missing_value_is_only_an_error_when_referenced()
    {
        var context = TenGiB with { Value = null };
        Assert.Equal(1, OffsetExpression.Evaluate("1", context));
        Assert.Throws<FormatException>(() => OffsetExpression.Evaluate("val", context));
    }

    [Fact]
    public void Rejects_invalid_context_and_excessive_nesting()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => OffsetExpression.Evaluate("0", new OffsetContext(2, 3, 0)));
        Assert.Throws<ArgumentOutOfRangeException>(() => OffsetExpression.Evaluate("sel", new OffsetContext(2, 0, -1)));
        Assert.Throws<FormatException>(() => OffsetExpression.Evaluate(new string('(', 100) + "1" + new string(')', 100), TenGiB));
        Assert.Throws<FormatException>(() => OffsetExpression.Evaluate(new string('+', 100) + "1", TenGiB));
        Assert.Throws<FormatException>(() => OffsetExpression.Evaluate(new string('1', 4097), TenGiB));
    }
}
