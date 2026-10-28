namespace NksHub.Textida.App.Editing;

/// <summary>How a document is shown (P3.6.11, P9.3.4). The mode belongs to the document, so every tab of it shows the same one; the switcher bar of the tab changes it.</summary>
internal enum ViewMode
{
    /// <summary>The text editor; for a Markdown file this is its source.</summary>
    Text,

    /// <summary>The bytes in the hex view.</summary>
    Hex,

    /// <summary>A picture file drawn as a picture, view only.</summary>
    Image,

    /// <summary>A Markdown file drawn as a page that fills the tab.</summary>
    Read,

    /// <summary>A Markdown file in the editor with the drawn page beside it.</summary>
    Edit,
}
