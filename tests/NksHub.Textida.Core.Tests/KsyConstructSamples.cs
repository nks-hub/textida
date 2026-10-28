using NksHub.Textida.Core.Documents;
using NksHub.Textida.Core.Hex.Ksy;

namespace NksHub.Textida.Core.Tests;

/// <summary>A small template, the bytes it is read from and how deep the tree is shown (P8.10.2: every construct of the subset has a template and an expected tree).</summary>
internal sealed record KsySample(string Name, string Ksy, byte[] Data, int Depth = 1);

internal static class KsyConstructSamples
{
    /// <summary>The tree a sample reads into, one line per node (see <see cref="KsySamples.Dump"/>), after a line with the file extensions of the template when it has some.</summary>
    public static string Describe(KsySample sample)
    {
        if (!KsyParser.TryParse(sample.Ksy, out var template, out var error)) throw new InvalidOperationException($"{sample.Name}: {error!.Kind} line {error.Line} {error.Subject}");
        string extensions = template!.Extensions.Count > 0 ? "extensions: " + string.Join(",", template.Extensions) + "\n" : "";
        return extensions + KsySamples.Dump(new KsyReader(new ArraySnapshot(sample.Data), template).Root, sample.Depth);
    }

    public static IReadOnlyList<KsySample> All { get; } =
    [
        new("meta-seq", """
            meta:
              id: m
              title: Smallest template
              file-extension: [mm, mmm]
            seq:
              - id: a
                type: u1
            """, [7]),
        new("endian", """
            meta:
              id: e
              endian: be
            seq:
              - id: n
                type: u2
            """, [1, 2]),
        new("endian-switch-le", """
            meta:
              id: es
              endian:
                switch-on: sel
                cases:
                  1: le
                  2: be
            seq:
              - id: sel
                type: u1
              - id: n
                type: u2
            """, [1, 1, 2]),
        new("endian-switch-be", """
            meta:
              id: es
              endian:
                switch-on: sel
                cases:
                  1: le
                  2: be
            seq:
              - id: sel
                type: u1
              - id: n
                type: u2
            """, [2, 1, 2]),
        new("bit-endian", """
            meta:
              id: bo
              bit-endian: le
            seq:
              - id: low
                type: b4
              - id: high
                type: b4
            """, [0xB1]),
        new("bits", """
            meta:
              id: bits
            seq:
              - id: flag
                type: b1
              - id: kind
                type: b3
              - id: rest
                type: b12
              - id: tail
                type: u1
            """, [0xB6, 0x1E, 0x99]),
        new("encoding-str", """
            meta:
              id: s
              encoding: ASCII
            seq:
              - id: name
                type: str
                size: 2
            """, [(byte)'h', (byte)'i']),
        new("strz", """
            meta:
              id: z
              encoding: UTF-8
            seq:
              - id: first
                type: strz
              - id: second
                type: strz
            """, [(byte)'a', (byte)'b', 0, (byte)'c', 0]),
        new("size-and-size-eos", """
            meta:
              id: b
            seq:
              - id: head
                size: 2
              - id: rest
                size-eos: true
            """, [1, 2, 3, 4, 5]),
        new("types-and-nested-enum", """
            meta:
              id: t
            seq:
              - id: item
                type: pair
            types:
              pair:
                enums:
                  axis:
                    0: x
                    1: y
                seq:
                  - id: axis
                    type: u1
                    enum: axis
                  - id: value
                    type: u1
            """, [1, 9], 2),
        new("enums", """
            meta:
              id: en
            seq:
              - id: kind
                type: u1
                enum: kinds
            enums:
              kinds:
                1: first
                2: second
            """, [2]),
        new("int", """
            meta:
              id: i
              endian: le
            seq:
              - id: a
                type: u1
              - id: b
                type: s1
              - id: c
                type: u2be
              - id: d
                type: s4
              - id: e
                type: u8
            """, [0xFF, 0xFF, 0x01, 0x02, 0xFE, 0xFF, 0xFF, 0xFF, 1, 0, 0, 0, 0, 0, 0, 0]),
        new("float", """
            meta:
              id: f
              endian: be
            seq:
              - id: single
                type: f4
              - id: double
                type: f8le
            """, [0x3F, 0xC0, 0, 0, 0, 0, 0, 0, 0, 0, 0x02, 0x40]),
        new("repeat", """
            meta:
              id: r
            seq:
              - id: count
                type: u1
              - id: counted
                type: u1
                repeat: expr
                repeat-expr: count
              - id: until_zero
                type: u1
                repeat: until
                repeat-until: _ == 0
              - id: rest
                type: u1
                repeat: eos
            """, [2, 10, 11, 5, 6, 0, 7, 8], 2),
        new("if-false", """
            meta:
              id: c
            seq:
              - id: has_extra
                type: u1
              - id: extra
                type: u1
                if: has_extra == 1
              - id: tail
                type: u1
            """, [0, 9]),
        new("if-true", """
            meta:
              id: c
            seq:
              - id: has_extra
                type: u1
              - id: extra
                type: u1
                if: has_extra == 1
              - id: tail
                type: u1
            """, [1, 5, 9]),
        new("contents", """
            meta:
              id: ct
            seq:
              - id: magic
                contents: [0x7f, E, L, F]
            """, [0x7F, 0x45, 0x4C, 0x46]),
        new("contents-differs", """
            meta:
              id: ct
            seq:
              - id: magic
                contents: [0x7f, E, L, F]
            """, [1, 2, 3, 4]),
        new("switch-on", """
            meta:
              id: sw
              endian: be
            seq:
              - id: kind
                type: u1
              - id: body
                type:
                  switch-on: kind
                  cases:
                    1: u1
                    2: u2
                    _: u1
            """, [2, 1, 2]),
        new("switch-on-default", """
            meta:
              id: sw
              endian: be
            seq:
              - id: kind
                type: u1
              - id: body
                type:
                  switch-on: kind
                  cases:
                    1: u1
                    2: u2
                    _: u1
            """, [7, 1, 2]),
        new("instances", """
            meta:
              id: ins
            seq:
              - id: at
                type: u1
              - id: sub
                type: part
                size: 3
            types:
              part:
                seq:
                  - id: first
                    type: u1
                instances:
                  local:
                    pos: 1
                    type: u1
                  absolute:
                    io: _root._io
                    pos: 5
                    type: u1
                  twice:
                    value: first * 2
            instances:
              table:
                pos: at
                type: u1
            """, [4, 7, 8, 9, 10, 11, 12], 2),
        new("expressions", """
            meta:
              id: ex
              endian: be
            seq:
              - id: n
                type: u1
              - id: kind
                type: u1
                enum: kinds
              - id: body
                size: n + 1
                if: kind == kinds::on
            instances:
              half:
                value: n / 2.0
              scaled:
                value: (n * 1.5).to_i
            enums:
              kinds:
                0: off
                1: on
            """, [4, 1, 1, 2, 3, 4, 5]),
        new("process", """
            meta:
              id: pr
              encoding: ASCII
            seq:
              - id: key
                type: u1
              - id: secret
                type: str
                size: 3
                process: xor(key)
              - id: rotated
                size: 2
                process: rol(1)
              - id: shifted
                size: 2
                process: ror(key - 83)
            """, [0x55, 0x34, 0x37, 0x36, 0x81, 0x40, 0x0C, 0x02]),
    ];

    /// <summary>The tree each sample is expected to read into.</summary>
    public static IReadOnlyDictionary<string, string> Expected { get; } = new Dictionary<string, string>
    {
        ["meta-seq"] = """
            extensions: mm,mmm
            m=@0+1
              a=7@0+1
            """,
        ["endian"] = """
            e=@0+2
              n=258 (0x0102)@0+2
            """,
        ["endian-switch-le"] = """
            es=@0+3
              sel=1@0+1
              n=513 (0x0201)@1+2
            """,
        ["endian-switch-be"] = """
            es=@0+3
              sel=2@0+1
              n=258 (0x0102)@1+2
            """,
        ["bit-endian"] = """
            bo=@0+1
              low=1@0+1
              high=11@0+1
            """,
        ["bits"] = """
            bits=@0+3
              flag=true@0+1
              kind=3@0+1
              rest=1566@0+2
              tail=153@2+1
            """,
        ["encoding-str"] = """
            s=@0+2
              name="hi"@0+2
            """,
        ["strz"] = """
            z=@0+5
              first="ab"@0+3
              second="c"@3+2
            """,
        ["size-and-size-eos"] = """
            b=@0+5
              head=0102@0+2
              rest=030405@2+3
            """,
        ["types-and-nested-enum"] = """
            t=@0+2
              item=@0+2
                axis=y (1)@0+1
                value=9@1+1
            """,
        ["enums"] = """
            en=@0+1
              kind=second (2)@0+1
            """,
        ["int"] = """
            i=@0+16
              a=255@0+1
              b=-1@1+1
              c=258 (0x0102)@2+2
              d=-2@4+4
              e=1 (0x0000000000000001)@8+8
            """,
        ["float"] = """
            f=@0+12
              single=1.5@0+4
              double=2.25@4+8
            """,
        ["repeat"] = """
            r=@0+8
              count=2@0+1
              counted=@1+2
                counted[0]=10@1+1
                counted[1]=11@2+1
              until_zero=@3+3
                until_zero[0]=5@3+1
                until_zero[1]=6@4+1
                until_zero[2]=0@5+1
              rest=@6+2
                rest[0]=7@6+1
                rest[1]=8@7+1
            """,
        ["if-false"] = """
            c=@0+2
              has_extra=0@0+1
              tail=9@1+1
            """,
        ["if-true"] = """
            c=@0+3
              has_extra=1@0+1
              extra=5@1+1
              tail=9@2+1
            """,
        ["contents"] = """
            ct=@0+4
              magic=7F454C46@0+4
            """,
        ["contents-differs"] = """
            ct=@0+4
              magic=!Contents@0+4
            """,
        ["switch-on"] = """
            sw=@0+3
              kind=2@0+1
              body=258 (0x0102)@1+2
            """,
        ["switch-on-default"] = """
            sw=@0+3
              kind=7@0+1
              body=1@1+1
            """,
        ["instances"] = """
            ins=@0+7
              at=4@0+1
              sub=@1+3
                first=7@1+1
                local=8@2+1
                absolute=11@5+1
                twice=14@1+0
              table=10@4+1
            """,
        ["expressions"] = """
            ex=@0+7
              n=4@0+1
              kind=on (1)@1+1
              body=0102030405@2+5
              half=2@0+0
              scaled=6@0+0
            """,
        ["process"] = """
            pr=@0+8
              key=85@0+1
              secret="abc"@1+3
              rotated=0380@4+2
              shifted=0380@6+2
            """,
    };
}
