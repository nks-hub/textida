namespace NksHub.Textida.Core.Encodings;

/// <summary>
/// The three ISO/IEC 8859 parts that the code page data of .NET does not carry. Each string holds the 96 characters of the bytes 0xA0 to 0xFF, taken from the mapping tables the Unicode Consortium
/// publishes (unicode.org/Public/MAPPINGS/ISO8859); the bytes below 0xA0 are the C0 and C1 controls and ASCII, the same code points as in every part.
/// </summary>
internal static class IsoTables
{
    /// <summary>ISO/IEC 8859-10: the characters of bytes 0xA0 to 0xFF, from the mapping table of the Unicode Consortium (bytes below 0xA0 are the same code points).</summary>
    internal const string Iso10 = "\u00A0\u0104\u0112\u0122\u012A\u0128\u0136\u00A7\u013B\u0110\u0160\u0166\u017D\u00AD\u016A\u014A\u00B0\u0105\u0113\u0123\u012B\u0129\u0137\u00B7\u013C\u0111\u0161\u0167\u017E\u2015\u016B\u014B\u0100\u00C1\u00C2\u00C3\u00C4\u00C5\u00C6\u012E\u010C\u00C9\u0118\u00CB\u0116\u00CD\u00CE\u00CF\u00D0\u0145\u014C\u00D3\u00D4\u00D5\u00D6\u0168\u00D8\u0172\u00DA\u00DB\u00DC\u00DD\u00DE\u00DF\u0101\u00E1\u00E2\u00E3\u00E4\u00E5\u00E6\u012F\u010D\u00E9\u0119\u00EB\u0117\u00ED\u00EE\u00EF\u00F0\u0146\u014D\u00F3\u00F4\u00F5\u00F6\u0169\u00F8\u0173\u00FA\u00FB\u00FC\u00FD\u00FE\u0138";

    /// <summary>ISO/IEC 8859-14: the characters of bytes 0xA0 to 0xFF, from the mapping table of the Unicode Consortium (bytes below 0xA0 are the same code points).</summary>
    internal const string Iso14 = "\u00A0\u1E02\u1E03\u00A3\u010A\u010B\u1E0A\u00A7\u1E80\u00A9\u1E82\u1E0B\u1EF2\u00AD\u00AE\u0178\u1E1E\u1E1F\u0120\u0121\u1E40\u1E41\u00B6\u1E56\u1E81\u1E57\u1E83\u1E60\u1EF3\u1E84\u1E85\u1E61\u00C0\u00C1\u00C2\u00C3\u00C4\u00C5\u00C6\u00C7\u00C8\u00C9\u00CA\u00CB\u00CC\u00CD\u00CE\u00CF\u0174\u00D1\u00D2\u00D3\u00D4\u00D5\u00D6\u1E6A\u00D8\u00D9\u00DA\u00DB\u00DC\u00DD\u0176\u00DF\u00E0\u00E1\u00E2\u00E3\u00E4\u00E5\u00E6\u00E7\u00E8\u00E9\u00EA\u00EB\u00EC\u00ED\u00EE\u00EF\u0175\u00F1\u00F2\u00F3\u00F4\u00F5\u00F6\u1E6B\u00F8\u00F9\u00FA\u00FB\u00FC\u00FD\u0177\u00FF";

    /// <summary>ISO/IEC 8859-16: the characters of bytes 0xA0 to 0xFF, from the mapping table of the Unicode Consortium (bytes below 0xA0 are the same code points).</summary>
    internal const string Iso16 = "\u00A0\u0104\u0105\u0141\u20AC\u201E\u0160\u00A7\u0161\u00A9\u0218\u00AB\u0179\u00AD\u017A\u017B\u00B0\u00B1\u010C\u0142\u017D\u201D\u00B6\u00B7\u017E\u010D\u0219\u00BB\u0152\u0153\u0178\u017C\u00C0\u00C1\u00C2\u0102\u00C4\u0106\u00C6\u00C7\u00C8\u00C9\u00CA\u00CB\u00CC\u00CD\u00CE\u00CF\u0110\u0143\u00D2\u00D3\u00D4\u0150\u00D6\u015A\u0170\u00D9\u00DA\u00DB\u00DC\u0118\u021A\u00DF\u00E0\u00E1\u00E2\u0103\u00E4\u0107\u00E6\u00E7\u00E8\u00E9\u00EA\u00EB\u00EC\u00ED\u00EE\u00EF\u0111\u0144\u00F2\u00F3\u00F4\u0151\u00F6\u015B\u0171\u00F9\u00FA\u00FB\u00FC\u0119\u021B\u00FF";


    /// <summary>The 256-entry table of a part: the code points of the bytes below 0xA0 are their own values, the rest come from <paramref name="upper"/>.</summary>
    internal static char[] Build(string upper)
    {
        var table = new char[256];
        for (int b = 0; b < 0xA0; b++) table[b] = (char)b;
        for (int b = 0xA0; b < 256; b++) table[b] = upper[b - 0xA0];
        return table;
    }
}
