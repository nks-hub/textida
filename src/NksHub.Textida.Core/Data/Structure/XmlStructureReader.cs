namespace NksHub.Textida.Core.Data.Structure;

public static partial class XmlStructureReader
{
    public static void Parse(StructureParseContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        new Parser(context).Parse();
    }

    private sealed partial class Parser
    {
        // These caps bound the active frames, declarations and attribute lookup tables below 16 MiB.
        private const int MaximumDepth = 4096;
        private const int MaximumAttributes = 16384;
        private const int MaximumNamespaces = 16384;
        private readonly StructureParseContext context;
        private readonly StructureCharReader reader;
        private readonly StructureCharReader comparisonLeft;
        private readonly StructureCharReader comparisonRight;
        private readonly List<Element> elements = [];
        private readonly List<Binding> bindings = [];
        private readonly List<Attribute> attributes = [];
        private readonly Dictionary<ulong, int> namespaceHeads = [];
        private readonly Dictionary<(ulong Name, ulong Namespace), int> attributeHeads = [];
        private long documentId;
        private long documentChildren;
        private int roots;

        public Parser(StructureParseContext context)
        {
            this.context = context;
            reader = context.OpenReader();
            comparisonLeft = context.OpenReader();
            comparisonRight = context.OpenReader();
        }

        public void Parse()
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            documentId = context.Sink.AddNode(StructureKind.Document, -1, 0, -1, -1, 0, 0, 0);
            while (reader.Peek() >= 0)
            {
                context.CancellationToken.ThrowIfCancellationRequested();
                if (reader.StartsWith("<!--")) Comment();
                else if (reader.StartsWith("<![CDATA[")) CData();
                else if (reader.StartsWith("<?")) ProcessingInstruction();
                else if (reader.StartsWith("</")) CloseElement();
                else if (reader.StartsWith("<!DOCTYPE") || reader.StartsWith("<!ENTITY") ||
                         reader.StartsWith("<!ELEMENT") || reader.StartsWith("<!ATTLIST") || reader.StartsWith("<!NOTATION"))
                    throw Error(reader.Position, "DTD declarations are forbidden.", StructureIssueKind.ForbiddenDtd);
                else if (reader.Peek() == '<') OpenElement();
                else Text();
            }
            if (elements.Count != 0) throw Error(reader.Position, "The element has no closing tag.");
            if (roots != 1) throw Error(reader.Position, "XML requires one root element.");
            context.Sink.CompleteNode(documentId, 0, reader.Length, reader.Length, documentChildren);
        }

        private long Add(StructureKind kind, long start, long nameStart, long nameEnd, long valueStart)
        {
            long parent = elements.Count == 0 ? documentId : elements[^1].Id;
            long ordinal = elements.Count == 0 ? documentChildren++ : elements[^1].Children++;
            return context.Sink.AddNode(kind, parent, start, nameStart, nameEnd, valueStart, ordinal, elements.Count + 1);
        }

        private void OpenElement()
        {
            long start = reader.Position;
            reader.Read();
            var name = ReadQName(reader);
            if (elements.Count == MaximumDepth) throw Error(start, "XML nesting exceeds the parser budget.", StructureIssueKind.ResourceLimit);
            if (elements.Count == 0 && roots++ != 0) throw Error(start, "XML has more than one root element.");
            long id = Add(StructureKind.XmlElement, start, name.Start, name.End, reader.Position);
            var element = new Element(id, name, bindings.Count);
            elements.Add(element);
            attributes.Clear();
            attributeHeads.Clear();
            int attributeCount = 0;
            bool empty;
            while (true)
            {
                long beforeSpace = reader.Position;
                reader.SkipWhitespace();
                if (reader.Consume('>')) { empty = false; break; }
                if (reader.Consume('/'))
                {
                    Expect(reader, '>');
                    empty = true;
                    break;
                }
                if (reader.Position == beforeSpace) throw Error(reader.Position, "Attributes require separating whitespace.");
                if (++attributeCount > MaximumAttributes) throw Error(reader.Position, "XML attributes exceed the parser budget.", StructureIssueKind.ResourceLimit);
                ReadAttribute(element);
            }
            element.ValueStart = reader.Position;
            context.Sink.SetNamespace(id, Resolve(name.Prefix, name.Start).Id);
            ResolveAttributes();
            if (empty) CompleteElement(element, reader.Position, reader.Position);
        }

        private void ReadAttribute(Element element)
        {
            long start = reader.Position;
            var name = ReadQName(reader);
            reader.SkipWhitespace();
            Expect(reader, '=');
            reader.SkipWhitespace();
            int quote = reader.Read();
            if (quote is not ('\'' or '"')) throw Error(start, "An attribute value requires quotes.");
            long valueStart = reader.Position;
            ulong hash = HashStart;
            bool empty = true;
            while (reader.Peek() != quote)
            {
                if (reader.Peek() < 0) throw Error(start, "The attribute value is not closed.");
                if (reader.Peek() == '<') throw Error(reader.Position, "An attribute cannot contain a literal '<'.");
                hash = Hash(hash, ReadAttributeUnit(reader));
                empty = false;
            }
            long valueEnd = reader.Position;
            reader.Read();
            bool declaration = IsLiteral(name.Prefix, "xmlns") || (name.Prefix.IsEmpty && IsLiteral(name.Local, "xmlns"));
            long id = Add(declaration ? StructureKind.XmlNamespace : StructureKind.XmlAttribute, start, name.Start, name.End, valueStart);
            context.Sink.CompleteNode(id, valueStart, valueEnd, reader.Position, 0);
            if (declaration)
            {
                var prefix = name.Prefix.IsEmpty ? NamePart.Empty(name.Start) : name.Local;
                AddBinding(element, new Binding(prefix, id, valueStart, valueEnd, hash, empty, -1));
            }
            else attributes.Add(new Attribute(id, name, default, -1));
        }

        private void CloseElement()
        {
            long start = reader.Position;
            Take("</");
            if (elements.Count == 0) throw Error(start, "There is no element to close.");
            var name = ReadQName(reader);
            var element = elements[^1];
            if (!SameName(name.Prefix, element.Name.Prefix) || !SameName(name.Local, element.Name.Local))
                throw Error(start, "The closing name does not match the opening name.");
            reader.SkipWhitespace();
            Expect(reader, '>');
            CompleteElement(element, start, reader.Position);
        }

        private void CompleteElement(Element element, long valueEnd, long end)
        {
            context.Sink.CompleteNode(element.Id, element.ValueStart, valueEnd, end, element.Children);
            for (int i = bindings.Count - 1; i >= element.ScopeStart; i--)
            {
                var binding = bindings[i];
                if (binding.Previous < 0) namespaceHeads.Remove(binding.Prefix.Hash);
                else namespaceHeads[binding.Prefix.Hash] = binding.Previous;
            }
            bindings.RemoveRange(element.ScopeStart, bindings.Count - element.ScopeStart);
            elements.RemoveAt(elements.Count - 1);
        }

        private void Text()
        {
            long start = reader.Position;
            while (true)
            {
                int c = reader.Peek();
                if (c < 0 || c == '<') break;
                if (elements.Count == 0 && c is not (' ' or '\t' or '\r' or '\n'))
                    throw Error(reader.Position, "Only whitespace is allowed outside the root element.");
                if (c == ']' && reader.StartsWith("]]>")) throw Error(reader.Position, "Text cannot contain ']]>'.");
                if (c == '&') ReadEntity(reader);
                else ReadXmlCharacter(reader);
            }
            Leaf(StructureKind.XmlText, start, -1, -1, start, reader.Position);
        }

        private void CData()
        {
            long start = reader.Position;
            if (elements.Count == 0) throw Error(start, "CDATA requires an enclosing element.");
            Take("<![CDATA[");
            long valueStart = reader.Position;
            while (!reader.StartsWith("]]>"))
            {
                if (reader.Peek() < 0) throw Error(start, "CDATA is not closed.");
                ReadXmlCharacter(reader);
            }
            long valueEnd = reader.Position;
            Take("]]>");
            Leaf(StructureKind.XmlCData, start, -1, -1, valueStart, valueEnd);
        }

        private void Comment()
        {
            long start = reader.Position;
            Take("<!--");
            long valueStart = reader.Position;
            while (!reader.StartsWith("-->"))
            {
                if (reader.Peek() < 0) throw Error(start, "The comment is not closed.");
                if (reader.StartsWith("--")) throw Error(reader.Position, "A comment cannot contain '--'.");
                ReadXmlCharacter(reader);
            }
            long valueEnd = reader.Position;
            Take("-->");
            Leaf(StructureKind.XmlComment, start, -1, -1, valueStart, valueEnd);
        }

        private void ProcessingInstruction()
        {
            long start = reader.Position;
            Take("<?");
            var name = ReadName(reader, allowColon: true);
            bool declaration = IsLiteral(name, "xml");
            if (!declaration && (IsLiteral(name, "XML") || IsXmlIgnoringCase(name)))
                throw Error(start, "The processing instruction target 'xml' is reserved.");
            if (declaration && start != context.BomLength) throw Error(start, "The XML declaration must be first.");
            long beforeSpace = reader.Position;
            reader.SkipWhitespace();
            long valueStart = reader.Position;
            if (declaration)
            {
                if (beforeSpace == reader.Position) throw Error(start, "The XML declaration requires a version.");
                Declaration();
            }
            else
            {
                if (!reader.StartsWith("?>") && reader.Position == beforeSpace)
                    throw Error(reader.Position, "The processing instruction requires separating whitespace.");
                while (!reader.StartsWith("?>"))
                {
                    if (reader.Peek() < 0) throw Error(start, "The processing instruction is not closed.");
                    ReadXmlCharacter(reader);
                }
            }
            long valueEnd = reader.Position;
            Take("?>");
            Leaf(StructureKind.XmlProcessingInstruction, start, name.Start, name.End, valueStart, valueEnd);
        }

        private void Declaration()
        {
            DeclarationValue("version", "1.0");
            int stage = 0;
            while (true)
            {
                long beforeSpace = reader.Position;
                reader.SkipWhitespace();
                if (reader.StartsWith("?>")) return;
                if (beforeSpace == reader.Position) throw Error(reader.Position, "Declaration attributes require separating whitespace.");
                if (stage == 0 && reader.StartsWith("encoding")) { DeclarationValue("encoding", null); stage = 1; }
                else if (stage < 2 && reader.StartsWith("standalone")) { DeclarationValue("standalone", "yes", "no"); stage = 2; }
                else throw Error(reader.Position, "Unexpected XML declaration attribute.");
            }
        }

        private void DeclarationValue(string expectedName, string? expectedValue, string? alternative = null)
        {
            long start = reader.Position;
            var name = ReadName(reader, allowColon: false);
            if (!IsLiteral(name, expectedName)) throw Error(start, "Unexpected XML declaration attribute.");
            reader.SkipWhitespace();
            Expect(reader, '=');
            reader.SkipWhitespace();
            int quote = reader.Read();
            if (quote is not ('\'' or '"')) throw Error(start, "The declaration value requires quotes.");
            long valueStart = reader.Position;
            int length = 0;
            while (reader.Peek() != quote)
            {
                int c = reader.Peek();
                if (c < 0 || (length == 0 ? !AsciiLetter(c) : !(AsciiLetter(c) || c is >= '0' and <= '9' or '.' or '_' or '-')))
                {
                    // The version starts with a digit; its exact value is checked below.
                    if (expectedName != "version" || c is not ('.' or >= '0' and <= '9'))
                        throw Error(reader.Position, "Invalid XML declaration value.");
                }
                ReadXmlCharacter(reader);
                length++;
            }
            long valueEnd = reader.Position;
            if (length == 0 || (expectedValue != null && !ValueIsLiteral(valueStart, valueEnd, expectedValue) &&
                               (alternative == null || !ValueIsLiteral(valueStart, valueEnd, alternative))))
                throw Error(valueStart, expectedName == "version" ? "Unsupported XML version; only XML 1.0 is supported." : "Unsupported XML declaration value.");
            reader.Read();
        }

        private void Leaf(StructureKind kind, long start, long nameStart, long nameEnd, long valueStart, long valueEnd)
        {
            long id = Add(kind, start, nameStart, nameEnd, valueStart);
            context.Sink.CompleteNode(id, valueStart, valueEnd, reader.Position, 0);
        }

        private void Take(string ascii)
        {
            foreach (char c in ascii) Expect(reader, c);
        }

        private static bool AsciiLetter(int c) => c is >= 'A' and <= 'Z' or >= 'a' and <= 'z';
        private static void Expect(StructureCharReader source, char c)
        {
            long start = source.Position;
            if (!source.Consume(c)) throw Error(source, start, $"Expected '{c}'.");
        }

        private StructureSyntaxException Error(long start, string message, StructureIssueKind kind = StructureIssueKind.Syntax) => Error(reader, start, message, kind);
        private static StructureSyntaxException Error(StructureCharReader source, long start, string message, StructureIssueKind kind = StructureIssueKind.Syntax) =>
            new(kind, start, Math.Min(source.Length, Math.Max(start + 1, source.Position)), message);

        private sealed class Element(long id, QName name, int scopeStart)
        {
            public long Id { get; } = id;
            public QName Name { get; } = name;
            public int ScopeStart { get; } = scopeStart;
            public long ValueStart;
            public long Children;
        }
    }
}
