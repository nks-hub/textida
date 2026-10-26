using System.Buffers.Binary;
using NksHub.Textida.Core.Hex;

namespace NksHub.Textida.Core.Tests;

public class DataInspectorTests
{
    private static byte[] Be(byte[] le) => [.. le.Reverse()];

    private static void RoundTrip(InspectorKind kind, string text, byte[] littleEndian, string? shown = null)
    {
        foreach (bool big in new[] { false, true })
        {
            byte[] bytes = big ? Be(littleEndian) : littleEndian;
            var value = DataInspector.Decode(kind, bytes, big);
            Assert.Equal(InspectorStatus.Ok, value.Status);
            Assert.Equal(shown ?? text, value.Text);
            Assert.Equal(bytes.Length, value.Length);
            Assert.True(DataInspector.TryEncode(kind, text, big, out var encoded, out string error), error);
            Assert.Equal(bytes, encoded);
        }
    }

    [Theory]
    [InlineData(InspectorKind.Int8, "-128", new byte[] { 0x80 })]
    [InlineData(InspectorKind.Int8, "127", new byte[] { 0x7F })]
    [InlineData(InspectorKind.UInt8, "0", new byte[] { 0 })]
    [InlineData(InspectorKind.UInt8, "255", new byte[] { 0xFF })]
    [InlineData(InspectorKind.Int16, "-32768", new byte[] { 0x00, 0x80 })]
    [InlineData(InspectorKind.Int16, "32767", new byte[] { 0xFF, 0x7F })]
    [InlineData(InspectorKind.UInt16, "65535", new byte[] { 0xFF, 0xFF })]
    [InlineData(InspectorKind.UInt16, "258", new byte[] { 0x02, 0x01 })]
    [InlineData(InspectorKind.Int32, "-2147483648", new byte[] { 0, 0, 0, 0x80 })]
    [InlineData(InspectorKind.Int32, "2147483647", new byte[] { 0xFF, 0xFF, 0xFF, 0x7F })]
    [InlineData(InspectorKind.UInt32, "4294967295", new byte[] { 0xFF, 0xFF, 0xFF, 0xFF })]
    [InlineData(InspectorKind.UInt32, "16909060", new byte[] { 4, 3, 2, 1 })]
    [InlineData(InspectorKind.Int64, "-9223372036854775808", new byte[] { 0, 0, 0, 0, 0, 0, 0, 0x80 })]
    [InlineData(InspectorKind.Int64, "9223372036854775807", new byte[] { 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0x7F })]
    [InlineData(InspectorKind.UInt64, "18446744073709551615", new byte[] { 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF })]
    [InlineData(InspectorKind.UInt64, "0", new byte[] { 0, 0, 0, 0, 0, 0, 0, 0 })]
    public void Integers_at_their_limits_in_both_byte_orders(InspectorKind kind, string text, byte[] littleEndian) => RoundTrip(kind, text, littleEndian);

    [Theory]
    [InlineData(InspectorKind.Uleb128, "0", new byte[] { 0 })]
    [InlineData(InspectorKind.Uleb128, "127", new byte[] { 0x7F })]
    [InlineData(InspectorKind.Uleb128, "128", new byte[] { 0x80, 0x01 })]
    [InlineData(InspectorKind.Uleb128, "624485", new byte[] { 0xE5, 0x8E, 0x26 })]
    [InlineData(InspectorKind.Uleb128, "18446744073709551615", new byte[] { 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0x01 })]
    [InlineData(InspectorKind.Sleb128, "0", new byte[] { 0 })]
    [InlineData(InspectorKind.Sleb128, "-1", new byte[] { 0x7F })]
    [InlineData(InspectorKind.Sleb128, "63", new byte[] { 0x3F })]
    [InlineData(InspectorKind.Sleb128, "64", new byte[] { 0xC0, 0x00 })]
    [InlineData(InspectorKind.Sleb128, "-64", new byte[] { 0x40 })]
    [InlineData(InspectorKind.Sleb128, "-65", new byte[] { 0xBF, 0x7F })]
    [InlineData(InspectorKind.Sleb128, "-123456", new byte[] { 0xC0, 0xBB, 0x78 })]
    [InlineData(InspectorKind.Sleb128, "9223372036854775807", new byte[] { 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0x00 })]
    [InlineData(InspectorKind.Sleb128, "-9223372036854775808", new byte[] { 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x7F })]
    public void Leb128_has_one_byte_order_and_round_trips(InspectorKind kind, string text, byte[] bytes)
    {
        foreach (bool big in new[] { false, true })
        {
            var value = DataInspector.Decode(kind, bytes, big);
            Assert.Equal(InspectorStatus.Ok, value.Status);
            Assert.Equal(text, value.Text);
            Assert.Equal(bytes.Length, value.Length);
            Assert.True(DataInspector.TryEncode(kind, text, big, out var encoded, out _));
            Assert.Equal(bytes, encoded);
        }
    }

    [Fact]
    public void Leb128_out_of_range_overlong_and_cut_short()
    {
        Assert.False(DataInspector.TryEncode(InspectorKind.Uleb128, "-1", false, out _, out _));
        Assert.False(DataInspector.TryEncode(InspectorKind.Uleb128, "18446744073709551616", false, out _, out _));
        Assert.False(DataInspector.TryEncode(InspectorKind.Sleb128, "9223372036854775808", false, out _, out _));
        Assert.Equal(InspectorStatus.Invalid, DataInspector.Decode(InspectorKind.Uleb128, [0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x01], false).Status);
        Assert.Equal(InspectorStatus.Invalid, DataInspector.Decode(InspectorKind.Uleb128, [0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0x7F], false).Status);
        Assert.Equal(InspectorStatus.Incomplete, DataInspector.Decode(InspectorKind.Uleb128, [0x80, 0x80], false).Status);
        Assert.Equal(InspectorStatus.Incomplete, DataInspector.Decode(InspectorKind.Sleb128, [], false).Status);
    }

    [Fact]
    public void Floats_round_trip_at_their_limits_in_both_byte_orders()
    {
        RoundTrip(InspectorKind.Float16, "65500", BitConverter.GetBytes(BitConverter.HalfToUInt16Bits((Half)65504f)));
        RoundTrip(InspectorKind.Float16, "-0.5", BitConverter.GetBytes(BitConverter.HalfToUInt16Bits((Half)(-0.5f))));
        RoundTrip(InspectorKind.Float32, "3.4028235E+38", BitConverter.GetBytes(float.MaxValue));
        RoundTrip(InspectorKind.Float32, "1.1754944E-38", BitConverter.GetBytes(float.Epsilon * 8388608f));
        RoundTrip(InspectorKind.Float32, "1.5", BitConverter.GetBytes(1.5f));
        RoundTrip(InspectorKind.Float64, "1.7976931348623157E+308", BitConverter.GetBytes(double.MaxValue));
        RoundTrip(InspectorKind.Float64, "-5E-324", BitConverter.GetBytes(-double.Epsilon));
        RoundTrip(InspectorKind.Float64, "0.1", BitConverter.GetBytes(0.1));
    }

    [Fact]
    public void Floats_that_do_not_fit_are_refused_and_not_rounded_to_infinity()
    {
        Assert.False(DataInspector.TryEncode(InspectorKind.Float16, "65520", false, out _, out _));
        Assert.False(DataInspector.TryEncode(InspectorKind.Float32, "1e39", false, out _, out _));
        Assert.False(DataInspector.TryEncode(InspectorKind.Float64, "1e309", false, out _, out _));
        Assert.False(DataInspector.TryEncode(InspectorKind.Float32, "abc", false, out _, out _));
        Assert.Equal("NaN", DataInspector.Decode(InspectorKind.Float32, BitConverter.GetBytes(float.NaN), false).Text);
        Assert.Equal("Infinity", DataInspector.Decode(InspectorKind.Float64, BitConverter.GetBytes(double.PositiveInfinity), false).Text);
    }

    [Fact]
    public void Dates_at_their_limits_in_both_byte_orders()
    {
        RoundTrip(InspectorKind.UnixTime32, "1970-01-01 00:00:00", [0, 0, 0, 0]);
        RoundTrip(InspectorKind.UnixTime32, "2038-01-19 03:14:07", BitConverter.GetBytes(int.MaxValue));
        RoundTrip(InspectorKind.UnixTime32, "1901-12-13 20:45:52", BitConverter.GetBytes(int.MinValue));
        RoundTrip(InspectorKind.UnixTime64, "9999-12-31 23:59:59", BitConverter.GetBytes(253402300799L));
        RoundTrip(InspectorKind.UnixTime64, "0001-01-01 00:00:00", BitConverter.GetBytes(-62135596800L));
        RoundTrip(InspectorKind.FileTime, "1601-01-01 00:00:00", BitConverter.GetBytes(0L));
        RoundTrip(InspectorKind.FileTime, "1970-01-01 00:00:00", BitConverter.GetBytes(116444736000000000L));
        RoundTrip(InspectorKind.DotNetTicks, "0001-01-01 00:00:00", BitConverter.GetBytes(0L));
        Assert.Equal("9999-12-31 23:59:59.9999999", DataInspector.Decode(InspectorKind.DotNetTicks, BitConverter.GetBytes(DateTime.MaxValue.Ticks), false).Text);
        Assert.Equal(InspectorStatus.Invalid, DataInspector.Decode(InspectorKind.DotNetTicks, BitConverter.GetBytes(DateTime.MaxValue.Ticks + 1), false).Status);
        Assert.Equal(InspectorStatus.Invalid, DataInspector.Decode(InspectorKind.UnixTime64, BitConverter.GetBytes(253402300800L), false).Status);
        Assert.Equal(InspectorStatus.Invalid, DataInspector.Decode(InspectorKind.FileTime, BitConverter.GetBytes(-1L), false).Status);
        Assert.False(DataInspector.TryEncode(InspectorKind.UnixTime32, "2038-01-19 03:14:08", false, out _, out _));
        Assert.False(DataInspector.TryEncode(InspectorKind.FileTime, "1600-12-31 00:00:00", false, out _, out _));
        Assert.False(DataInspector.TryEncode(InspectorKind.UnixTime32, "not a date", false, out _, out _));
    }

    [Fact]
    public void Characters_in_every_encoding_and_byte_order()
    {
        RoundTrip(InspectorKind.Latin1, "ÿ", [0xFF], "ÿ U+00FF");
        Assert.Equal("· U+0000", DataInspector.Decode(InspectorKind.Latin1, [0], false).Text);
        foreach (bool big in new[] { false, true })
        {
            Assert.Equal("€ U+20AC", DataInspector.Decode(InspectorKind.Utf8, [0xE2, 0x82, 0xAC], big).Text);
            Assert.Equal("😀 U+1F600", DataInspector.Decode(InspectorKind.Utf8, [0xF0, 0x9F, 0x98, 0x80], big).Text);
            Assert.True(DataInspector.TryEncode(InspectorKind.Utf8, "😀", big, out var utf8, out _));
            Assert.Equal([0xF0, 0x9F, 0x98, 0x80], utf8);
            Assert.True(DataInspector.TryEncode(InspectorKind.Utf16, "U+1F600", big, out var pair, out _));
            Assert.Equal(big ? [0xD8, 0x3D, 0xDE, 0x00] : [0x3D, 0xD8, 0x00, 0xDE], pair);
            Assert.Equal("😀 U+1F600", DataInspector.Decode(InspectorKind.Utf16, pair, big).Text);
            Assert.True(DataInspector.TryEncode(InspectorKind.Utf32, "U+10FFFF", big, out var wide, out _));
            Assert.Equal(big ? [0, 0x10, 0xFF, 0xFF] : [0xFF, 0xFF, 0x10, 0], wide);
            Assert.Equal("U+10FFFF".Length > 0 ? 4 : 0, DataInspector.Decode(InspectorKind.Utf32, wide, big).Length);
        }
        Assert.False(DataInspector.TryEncode(InspectorKind.Latin1, "€", false, out _, out _));
        Assert.False(DataInspector.TryEncode(InspectorKind.Utf32, "U+110000", false, out _, out _));
        Assert.False(DataInspector.TryEncode(InspectorKind.Utf8, "ab", false, out _, out _));
        Assert.False(DataInspector.TryEncode(InspectorKind.Utf8, "U+D800", false, out _, out _));
    }

    [Fact]
    public void Invalid_and_cut_short_characters_are_told_apart()
    {
        Assert.Equal(InspectorStatus.Invalid, DataInspector.Decode(InspectorKind.Utf8, [0x80], false).Status);
        Assert.Equal(InspectorStatus.Invalid, DataInspector.Decode(InspectorKind.Utf8, [0xC0, 0x80], false).Status);
        Assert.Equal(InspectorStatus.Invalid, DataInspector.Decode(InspectorKind.Utf8, [0xE2, 0x41, 0x41], false).Status);
        Assert.Equal(InspectorStatus.Incomplete, DataInspector.Decode(InspectorKind.Utf8, [0xE2, 0x82], false).Status);
        Assert.Equal(3, DataInspector.Decode(InspectorKind.Utf8, [0xE2, 0x82], false).Length);
        Assert.Equal(InspectorStatus.Incomplete, DataInspector.Decode(InspectorKind.Utf16, [0x3D, 0xD8], false).Status);
        Assert.Equal(InspectorStatus.Invalid, DataInspector.Decode(InspectorKind.Utf16, [0x00, 0xDC], false).Status);
        Assert.Equal(InspectorStatus.Invalid, DataInspector.Decode(InspectorKind.Utf32, [0x00, 0xD8, 0, 0], false).Status);
        Assert.Equal(InspectorStatus.Incomplete, DataInspector.Decode(InspectorKind.Utf32, [1, 2, 3], false).Status);
    }

    [Fact]
    public void Guid_in_both_layouts()
    {
        var guid = Guid.Parse("00112233-4455-6677-8899-aabbccddeeff");
        byte[] microsoft = guid.ToByteArray();
        byte[] rfc = guid.ToByteArray(bigEndian: true);
        Assert.Equal("00112233-4455-6677-8899-aabbccddeeff", DataInspector.Decode(InspectorKind.Guid, microsoft, false).Text);
        Assert.Equal("00112233-4455-6677-8899-aabbccddeeff", DataInspector.Decode(InspectorKind.Guid, rfc, true).Text);
        Assert.True(DataInspector.TryEncode(InspectorKind.Guid, "00112233-4455-6677-8899-aabbccddeeff", true, out var bytes, out _));
        Assert.Equal(rfc, bytes);
        Assert.False(DataInspector.TryEncode(InspectorKind.Guid, "0011", false, out _, out _));
    }

    [Fact]
    public void Out_of_range_integers_say_so_at_once_and_are_never_clamped()
    {
        foreach (var (kind, text) in new[]
        {
            (InspectorKind.Int8, "128"), (InspectorKind.Int8, "-129"), (InspectorKind.UInt8, "256"), (InspectorKind.UInt8, "-1"),
            (InspectorKind.Int16, "32768"), (InspectorKind.UInt16, "65536"), (InspectorKind.Int32, "2147483648"), (InspectorKind.UInt32, "4294967296"),
            (InspectorKind.Int64, "9223372036854775808"), (InspectorKind.UInt64, "18446744073709551616"), (InspectorKind.UInt32, "0x1FFFFFFFF"),
        })
        {
            Assert.False(DataInspector.TryEncode(kind, text, false, out var bytes, out string error));
            Assert.Empty(bytes);
            Assert.NotEmpty(error);
        }
        Assert.False(DataInspector.TryEncode(InspectorKind.UInt32, "", false, out _, out _));
        Assert.False(DataInspector.TryEncode(InspectorKind.UInt32, "12x", false, out _, out _));
        Assert.True(DataInspector.TryEncode(InspectorKind.UInt32, "0xCAFEBABE", true, out var hex, out _));
        Assert.Equal([0xCA, 0xFE, 0xBA, 0xBE], hex);
        Assert.True(DataInspector.TryEncode(InspectorKind.Int16, "-0x8000", false, out var neg, out _));
        Assert.Equal([0x00, 0x80], neg);
    }

    [Fact]
    public void A_value_cut_short_by_the_end_of_the_data_is_incomplete_and_never_zero()
    {
        foreach (var kind in DataInspector.All)
        {
            int size = DataInspector.SizeOf(kind);
            if (size < 2) continue;
            var value = DataInspector.Decode(kind, new byte[size - 1], false);
            Assert.Equal(InspectorStatus.Incomplete, value.Status);
            Assert.Equal("", value.Text);
            Assert.Equal(size, value.Length);
        }
    }

    [Fact]
    public void Every_kind_decodes_without_throwing_from_random_bytes()
    {
        var r = new Random(3);
        for (int i = 0; i < 2000; i++)
        {
            var bytes = new byte[r.Next(0, 20)];
            r.NextBytes(bytes);
            foreach (var kind in DataInspector.All)
            foreach (bool big in new[] { false, true })
            {
                var v = DataInspector.Decode(kind, bytes, big);
                if (v.Status == InspectorStatus.Ok) Assert.True(v.Length <= bytes.Length);
            }
        }
    }
}
