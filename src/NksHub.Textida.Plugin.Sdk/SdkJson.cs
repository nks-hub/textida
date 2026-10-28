using System.Text.Json.Serialization;

namespace NksHub.Textida.PluginSdk;

internal sealed record DocumentRef(string Document);
internal sealed record RevisionParams(string Document, long Revision);
internal sealed record DocumentInfoResult(long Revision, long ByteLength, string Codec, int BomLength, string? Path);
internal sealed record SelectionItem(long Anchor, long Caret);
internal sealed record SelectionsResult(long Revision, List<SelectionItem> Selections);
internal sealed record ReadTextParams(string Document, long Revision, long Start, long End);
internal sealed record ReadTextResult(string Text, List<int> Invalid);
internal sealed record LineEndingFacts(string Dominant);
internal sealed record FactsResult(LineEndingFacts LineEndings);
internal sealed record PositionParams(string Document, long Revision, long Offset);
internal sealed record PositionResult(long Line, long Column);
internal sealed record ApplyParams(string Document, long Revision, string BatchId, List<RangeEdit> Edits);
internal sealed record ProposeParams(string Document, long Revision, string Title, List<RangeEdit> Edits);
internal sealed record NoticeParams(string Level, string Message);
internal sealed record TaskStartParams(string Title, bool Cancellable);
internal sealed record TaskStartResult(string Task);
internal sealed record TaskProgressParams(string Task, double Fraction, string? Message);
internal sealed record TaskEndParams(string Task);

/// <summary>The parameters and results of the editor calls the SDK makes for a plugin, with source-generated metadata (no reflection).</summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(DocumentRef))]
[JsonSerializable(typeof(RevisionParams))]
[JsonSerializable(typeof(DocumentInfoResult))]
[JsonSerializable(typeof(SelectionsResult))]
[JsonSerializable(typeof(ReadTextParams))]
[JsonSerializable(typeof(ReadTextResult))]
[JsonSerializable(typeof(FactsResult))]
[JsonSerializable(typeof(PositionParams))]
[JsonSerializable(typeof(PositionResult))]
[JsonSerializable(typeof(ApplyParams))]
[JsonSerializable(typeof(ProposeParams))]
[JsonSerializable(typeof(NoticeParams))]
[JsonSerializable(typeof(TaskStartParams))]
[JsonSerializable(typeof(TaskStartResult))]
[JsonSerializable(typeof(TaskProgressParams))]
[JsonSerializable(typeof(TaskEndParams))]
internal sealed partial class SdkJson : JsonSerializerContext;
