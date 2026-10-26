namespace NksHub.Textida.Core.Documents;

/// <summary>The external backing file of a snapshot changed or became unreadable.</summary>
public sealed class SnapshotInvalidatedException : IOException
{
    public SnapshotInvalidatedException() : base("The snapshot backing file changed outside the document.") { }

    public SnapshotInvalidatedException(string message, Exception innerException) : base(message, innerException) { }
}
