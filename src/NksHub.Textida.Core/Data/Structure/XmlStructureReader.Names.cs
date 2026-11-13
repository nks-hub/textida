namespace NksHub.Textida.Core.Data.Structure;

public static partial class XmlStructureReader
{
    private sealed partial class Parser
    {
        private const ulong HashStart = 14695981039346656037;
        private const string XmlNamespace = "http://www.w3.org/XML/1998/namespace";
        private const string XmlnsNamespace = "http://www.w3.org/2000/xmlns/";
        private readonly record struct NamePart(long Start, long End, ulong Hash)
        {
            public bool IsEmpty => Start == End;
            public static NamePart Empty(long at) => new(at, at, HashStart);
        }
        private readonly record struct QName(NamePart Prefix, NamePart Local)
        {
            public long Start => Prefix.IsEmpty ? Local.Start : Prefix.Start;
            public long End => Local.End;
        }
        private readonly record struct NamespaceReference(long Id, int Binding, ulong Hash);
        private readonly record struct Binding(NamePart Prefix, long Id, long ValueStart, long ValueEnd, ulong Hash, bool Empty, int Previous);
        private readonly record struct Attribute(long Id, QName Name, NamespaceReference Namespace, int Previous);

        private static ulong Hash(ulong hash, int scalar) => unchecked((hash ^ (uint)scalar) * 1099511628211);
        private static ulong LiteralHash(string text)
        {
            ulong hash = HashStart;
            foreach (char c in text) hash = Hash(hash, c);
            return hash;
        }

        private static QName ReadQName(StructureCharReader source)
        {
            var first = ReadName(source, allowColon: false);
            return source.Consume(':') ? new(first, ReadName(source, allowColon: false)) : new(NamePart.Empty(first.Start), first);
        }

        private static NamePart ReadName(StructureCharReader source, bool allowColon)
        {
            long start = source.Position;
            int c = source.Peek();
            if (!NameStart(c) && !(allowColon && c == ':')) throw Error(source, start, "Expected an XML name.");
            ulong hash = HashStart;
            do { hash = Hash(hash, source.Read()); c = source.Peek(); }
            while (NameCharacter(c) || (allowColon && c == ':'));
            return new(start, source.Position, hash);
        }

        private static bool NameStart(int c) => c == '_' || c is
            >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= 0xC0 and <= 0xD6 or >= 0xD8 and <= 0xF6 or
            >= 0xF8 and <= 0x2FF or >= 0x370 and <= 0x37D or >= 0x37F and <= 0x1FFF or
            >= 0x200C and <= 0x200D or >= 0x2070 and <= 0x218F or >= 0x2C00 and <= 0x2FEF or
            >= 0x3001 and <= 0xD7FF or >= 0xF900 and <= 0xFDCF or >= 0xFDF0 and <= 0xFFFD or
            >= 0x10000 and <= 0xEFFFF;

        private static bool NameCharacter(int c) => NameStart(c) || c is '-' or '.' or >= '0' and <= '9' or 0xB7 or
            >= 0x300 and <= 0x36F or >= 0x203F and <= 0x2040;

        private static bool XmlCharacter(int c) => c is 9 or 10 or 13 or >= 0x20 and <= 0xD7FF or
            >= 0xE000 and <= 0xFFFD or >= 0x10000 and <= 0x10FFFF;

        private static int ReadXmlCharacter(StructureCharReader source)
        {
            long start = source.Position;
            int c = source.Read();
            if (!XmlCharacter(c)) throw Error(source, start, "Invalid XML character.");
            return c;
        }

        private static int ReadAttributeUnit(StructureCharReader source)
        {
            if (source.Peek() == '&') return ReadEntity(source);
            int c = ReadXmlCharacter(source);
            if (c == '\r') { source.Consume('\n'); return ' '; }
            return c is '\n' or '\t' ? ' ' : c;
        }

        private static int ReadEntity(StructureCharReader source)
        {
            long start = source.Position;
            Expect(source, '&');
            if (source.Consume('#'))
            {
                int radix = source.Consume('x') ? 16 : 10;
                int value = 0;
                bool any = false;
                while (source.Peek() != ';')
                {
                    int c = source.Peek();
                    int digit = c is >= '0' and <= '9' ? c - '0' : radix == 16 && c is >= 'a' and <= 'f' ? c - 'a' + 10 :
                        radix == 16 && c is >= 'A' and <= 'F' ? c - 'A' + 10 : -1;
                    if (digit < 0 || digit >= radix || value > (0x10FFFF - digit) / radix)
                        throw Error(source, start, "Invalid numeric XML character reference.");
                    value = value * radix + digit;
                    any = true;
                    source.Read();
                }
                Expect(source, ';');
                if (!any || !XmlCharacter(value)) throw Error(source, start, "Invalid numeric XML character reference.");
                return value;
            }
            var name = ReadName(source, allowColon: true);
            Expect(source, ';');
            // Comparing hashes alone is insufficient for untrusted entity names.
            if (name.End > name.Start)
            {
                // All five predefined names are ASCII; the source codec still determines their byte length.
                int chars = 0;
                Span<char> token = stackalloc char[4];
                source.Seek(name.Start);
                while (source.Position < name.End)
                {
                    int c = source.Read();
                    if (chars < token.Length) token[chars] = c <= 127 ? (char)c : '\0';
                    if (chars <= token.Length) chars++;
                }
                Expect(source, ';');
                if (chars <= token.Length)
                {
                    var text = token[..chars];
                    if (text.SequenceEqual("amp")) return '&';
                    if (text.SequenceEqual("lt")) return '<';
                    if (text.SequenceEqual("gt")) return '>';
                    if (text.SequenceEqual("quot")) return '"';
                    if (text.SequenceEqual("apos")) return '\'';
                }
            }
            throw Error(source, start, "Custom XML entities are not supported.", StructureIssueKind.UnknownEntity);
        }

        private bool SameName(NamePart left, NamePart right)
        {
            if (left.Hash != right.Hash) return false;
            comparisonLeft.Seek(left.Start);
            comparisonRight.Seek(right.Start);
            while (comparisonLeft.Position < left.End && comparisonRight.Position < right.End)
                if (comparisonLeft.Read() != comparisonRight.Read()) return false;
            return comparisonLeft.Position == left.End && comparisonRight.Position == right.End;
        }

        private bool IsLiteral(NamePart name, string literal)
        {
            if (name.Hash != LiteralHash(literal)) return false;
            comparisonLeft.Seek(name.Start);
            foreach (char c in literal)
                if (comparisonLeft.Position >= name.End || comparisonLeft.Read() != c) return false;
            return comparisonLeft.Position == name.End;
        }

        private bool IsXmlIgnoringCase(NamePart name)
        {
            comparisonLeft.Seek(name.Start);
            foreach (char c in "xml")
            {
                if (comparisonLeft.Position >= name.End) return false;
                int actual = comparisonLeft.Read();
                if (actual != c && actual != c - 32) return false;
            }
            return comparisonLeft.Position == name.End;
        }

        private bool ValueIsLiteral(long start, long end, string literal)
        {
            comparisonLeft.Seek(start);
            foreach (char c in literal)
                if (comparisonLeft.Position >= end || ReadAttributeUnit(comparisonLeft) != c) return false;
            return comparisonLeft.Position == end;
        }

        private void AddBinding(Element element, Binding binding)
        {
            if (bindings.Count == MaximumNamespaces) throw Error(binding.ValueStart, "XML namespace scopes exceed the parser budget.", StructureIssueKind.ResourceLimit);
            int previous = namespaceHeads.GetValueOrDefault(binding.Prefix.Hash, -1);
            for (int at = previous; at >= element.ScopeStart; at = bindings[at].Previous)
            {
                context.CancellationToken.ThrowIfCancellationRequested();
                if (SameName(bindings[at].Prefix, binding.Prefix)) throw Error(binding.Prefix.Start, "Duplicate namespace declaration.");
            }
            bool xmlPrefix = IsLiteral(binding.Prefix, "xml");
            bool xmlUri = ValueIsLiteral(binding.ValueStart, binding.ValueEnd, XmlNamespace);
            if (IsLiteral(binding.Prefix, "xmlns") || ValueIsLiteral(binding.ValueStart, binding.ValueEnd, XmlnsNamespace) ||
                xmlPrefix != xmlUri || (!binding.Prefix.IsEmpty && binding.Empty))
                throw Error(binding.ValueStart, "Invalid reserved or empty namespace binding.");
            namespaceHeads[binding.Prefix.Hash] = bindings.Count;
            bindings.Add(binding with { Previous = previous });
        }

        private NamespaceReference Resolve(NamePart prefix, long start, bool attribute = false)
        {
            if (attribute && prefix.IsEmpty) return new(-1, -1, HashStart);
            if (IsLiteral(prefix, "xmlns")) throw Error(start, "The xmlns prefix cannot name an element or attribute.");
            for (int at = namespaceHeads.GetValueOrDefault(prefix.Hash, -1); at >= 0; at = bindings[at].Previous)
            {
                context.CancellationToken.ThrowIfCancellationRequested();
                var binding = bindings[at];
                if (SameName(prefix, binding.Prefix)) return binding.Empty ? new(-1, -1, HashStart) : new(binding.Id, at, binding.Hash);
            }
            if (prefix.IsEmpty) return new(-1, -1, HashStart);
            if (IsLiteral(prefix, "xml")) return new(-2, -2, LiteralHash(XmlNamespace));
            throw Error(start, "An XML namespace prefix is not declared.");
        }

        private bool SameNamespace(NamespaceReference left, NamespaceReference right)
        {
            if (left.Id == right.Id) return true;
            if (left.Hash != right.Hash || left.Id == -1 || right.Id == -1) return false;
            if (left.Id == -2) return ValueIsLiteral(bindings[right.Binding].ValueStart, bindings[right.Binding].ValueEnd, XmlNamespace);
            if (right.Id == -2) return ValueIsLiteral(bindings[left.Binding].ValueStart, bindings[left.Binding].ValueEnd, XmlNamespace);
            var a = bindings[left.Binding];
            var b = bindings[right.Binding];
            comparisonLeft.Seek(a.ValueStart);
            comparisonRight.Seek(b.ValueStart);
            while (comparisonLeft.Position < a.ValueEnd && comparisonRight.Position < b.ValueEnd)
                if (ReadAttributeUnit(comparisonLeft) != ReadAttributeUnit(comparisonRight)) return false;
            return comparisonLeft.Position == a.ValueEnd && comparisonRight.Position == b.ValueEnd;
        }

        private void ResolveAttributes()
        {
            for (int i = 0; i < attributes.Count; i++)
            {
                context.CancellationToken.ThrowIfCancellationRequested();
                var attribute = attributes[i];
                var ns = Resolve(attribute.Name.Prefix, attribute.Name.Start, attribute: true);
                context.Sink.SetNamespace(attribute.Id, ns.Id);
                var key = (attribute.Name.Local.Hash, ns.Hash);
                int previous = attributeHeads.GetValueOrDefault(key, -1);
                for (int at = previous; at >= 0; at = attributes[at].Previous)
                    if (SameName(attribute.Name.Local, attributes[at].Name.Local) && SameNamespace(ns, attributes[at].Namespace))
                        throw Error(attribute.Name.Start, "Duplicate attribute expanded name.");
                attributes[i] = attribute with { Namespace = ns, Previous = previous };
                attributeHeads[key] = i;
            }
        }
    }
}
