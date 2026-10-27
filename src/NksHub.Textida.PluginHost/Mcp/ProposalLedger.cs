using NksHub.Textida.PluginHost.Documents;

namespace NksHub.Textida.PluginHost.Mcp;

/// <summary>How a proposal an assistant made ended up. <see cref="Stale"/>: the text it touched changed before the user decided, so it could not be applied.</summary>
public enum ProposalStatus { Pending, Applied, Rejected, Stale, Failed }

public sealed record ProposalEntry(string Id, Guid Document, long Revision, int Edits, string? Title, ProposalStatus Status, long? AppliedRevision);

/// <summary>What the user decided about a proposal; <paramref name="Revision"/> is the document revision after it was applied.</summary>
public readonly record struct ReviewOutcome(ProposalStatus Status, long? Revision = null);

/// <summary>Shows a proposal to the user (as a diff) and reports the decision.</summary>
public interface IProposalReviewer
{
    ValueTask<ReviewOutcome> ReviewAsync(EditProposal proposal, CancellationToken cancellation);
}

/// <summary>Bounded record of the proposals of the assistant; the oldest entry goes first.</summary>
public sealed class ProposalLedger(int capacity)
{
    private readonly object _gate = new();
    private readonly Dictionary<string, ProposalEntry> _entries = [];
    private readonly Queue<string> _order = new();
    private readonly int _capacity = capacity > 0 ? capacity : throw new ArgumentOutOfRangeException(nameof(capacity));

    public string Add(EditProposal proposal)
    {
        var id = Guid.NewGuid().ToString("N");
        lock (_gate)
        {
            _entries[id] = new ProposalEntry(id, proposal.DocumentId, proposal.Revision, proposal.Edits.Count, proposal.Title, ProposalStatus.Pending, null);
            _order.Enqueue(id);
            while (_order.Count > _capacity) _entries.Remove(_order.Dequeue());
        }
        return id;
    }

    public void Resolve(string id, ReviewOutcome outcome)
    {
        lock (_gate)
        {
            if (_entries.TryGetValue(id, out var entry)) _entries[id] = entry with { Status = outcome.Status, AppliedRevision = outcome.Revision };
        }
    }

    public ProposalEntry? Get(string id)
    {
        lock (_gate) return _entries.GetValueOrDefault(id);
    }

    /// <summary>Proposals the user has not decided on yet.</summary>
    public int PendingCount
    {
        get { lock (_gate) return _entries.Values.Count(e => e.Status == ProposalStatus.Pending); }
    }
}

/// <summary>
/// The <see cref="IEditProposals"/> behind the document API: a proposal of the assistant is recorded in the ledger and handed to the reviewer, which answers when the user has decided;
/// a proposal of any other plugin goes to <paramref name="other"/>.
/// </summary>
public sealed class McpProposals(ProposalLedger ledger, IProposalReviewer reviewer, IEditProposals? other = null) : IEditProposals
{
    public ValueTask<ProposalDecision> SubmitAsync(EditProposal proposal, CancellationToken cancellation)
    {
        if (proposal.PluginId != McpServer.ClientId)
            return other?.SubmitAsync(proposal, cancellation) ?? throw new InvalidOperationException("No service takes the proposals of this plugin.");
        var id = ledger.Add(proposal);
        _ = DecideAsync(id, proposal);
        return ValueTask.FromResult(new ProposalDecision(id, ProposalOutcome.Pending, 0, proposal.Edits.Count));
    }

    private async Task DecideAsync(string id, EditProposal proposal)
    {
        try { ledger.Resolve(id, await reviewer.ReviewAsync(proposal, CancellationToken.None).ConfigureAwait(false)); }
        catch (Exception) { ledger.Resolve(id, new ReviewOutcome(ProposalStatus.Failed)); }
    }
}
