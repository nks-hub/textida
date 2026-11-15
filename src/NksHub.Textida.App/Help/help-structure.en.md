## [view.structure] JSON/XML structure
commands: nks.view.structure
The panel shows the nodes and values of the active JSON, JSONC or XML document. Browsing does not rewrite the file. The panel also reports node counts and errors.
1. Open the document and choose View, Panels, Show JSON/XML Structure, or run the same command from the command palette.
2. Expand and collapse nodes with the mouse or arrow keys. Selecting a node selects its range in the original text; Go to source reveals that selection again.
3. Copy path copies the path to the node, and Copy source copies its original text. An oversized selection is not copied; the panel reports the limit.
4. To search, choose Name, Value, Path or JSONPath/XPath and enter an expression. Find or Enter also starts the search manually. Results have Previous page and Next page controls; Show tree returns to the ordinary tree.
5. Edit value changes a scalar JSON value or an XML attribute or text. An XML element can be edited only when it has a single text value without nested elements. Editing is disabled for read-only files.
6. Cancel stops the panel's work. Refresh reads the tree of the current document again.
- Supported JSONPath subset: members, indexes, wildcards, recursive descent and scalar equality filters.
- Supported XPath subset: child and descendant paths, attributes, namespaces, node()/text()/comment(), positions and equality predicates. Enter namespaces as prefix=URI, one per line. The reader accepts XML 1.0; DTD is disabled.
