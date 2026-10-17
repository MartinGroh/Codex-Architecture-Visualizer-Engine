using Cave.Application;
using Cave.Domain;
using Cave.Infrastructure.Workspaces;

namespace Cave.Infrastructure.Demo;

/// <summary>Supplies explicit synthetic model, effort and mixed-coverage statistics without reading Codex.</summary>
public sealed class DemoAgentTelemetry(TimeProvider timeProvider, string workspaceRoot) : IAgentTelemetrySource, IAgentTelemetryStore
{
    private readonly DateTimeOffset _started = timeProvider.GetUtcNow().AddDays(-3);

    /// <inheritdoc />
    public Task<AgentTelemetryState> ReadAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var now = timeProvider.GetUtcNow();
        var workspace = MachineWorkspaceCatalogStore.CreateWorkspaceId(workspaceRoot);
        var ids = new[] { "demo-main", "demo-reviewer", "demo-docs", "demo-builder" };
        var models = new[] { "gpt-6.1-sol", "gpt-6-astra", "gpt-6-luna", "gpt-5.6-terra" };
        var efforts = new[] { "xhigh", "high", "medium", "low" };
        var records = ids.Select((id, index) => new AgentTelemetryRecord(id, index == 0 ? null : ids[0],
            [workspace], index != 0, _started, now, now, true, new(models[index], efforts[index]),
            index == 2 ? null : [new(models[index], efforts[index], 120_000 + index * 11_000, 80_000,
                24_000 + index * 2_000, 144_000 + index * 13_000)], index != 2,
            600_000 + index * 120_000, 8 + index, true,
            [new(models[index], efforts[index], 420_000 - index * 60_000)], index == 2 ? "Synthetic token breakdown unavailable." : null)).ToArray();
        return Task.FromResult(new AgentTelemetryState(records, null) { IsSynthetic = true });
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<AgentTelemetrySample>> ReadAsync(IReadOnlyList<string> threadIds, CancellationToken cancellationToken)
    {
        var state = await ReadAsync(cancellationToken).ConfigureAwait(false);
        return state.Records.Where(record => threadIds.Contains(record.ThreadId, StringComparer.Ordinal))
            .Select(record => new AgentTelemetrySample(record.ThreadId, record.ParentThreadId, record.Configuration,
                record.Usage, record.CompletedDurationMs, record.CompletedTurnCount, true,
                record.LastSampleAtUtc, record.Error)).ToArray();
    }

    /// <inheritdoc />
    public Task ObserveAsync(IReadOnlyList<AgentTelemetryObservation> observations, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask; // Explicit demo data is synthetic and never persisted into live statistics.
    }
}
