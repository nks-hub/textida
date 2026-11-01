namespace NksHub.Textida.Core.Documents;

/// <summary>
/// Puts a finished file at a place the user may not write (PZ.4.2). <see cref="Document.ProtectedWriter"/> hands it the file the save wrote in the temporary folder, the target and the SHA-256 of the
/// bytes (hex); an implementation checks the hash before it writes, so the bytes that arrive are the ones the document produced. It throws <see cref="IOException"/> or <see cref="UnauthorizedAccessException"/> when it cannot.
/// </summary>
public interface IProtectedWriter
{
    void Install(string tempPath, string target, string sha256);
}
