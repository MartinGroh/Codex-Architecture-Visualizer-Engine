using Cave.Application;
using Cave.Domain;

namespace Cave.Tests;

internal static class TestTelemetry
{
    internal static AgentTelemetryService Create(AgentTelemetryState? state = null) =>
        new(new MemoryStore(state ?? new([], null)), new EmptySource(), new EmptyActivity(), TimeProvider.System);

    internal sealed class MemoryStore(AgentTelemetryState state) : IAgentTelemetryStore
    {
        internal AgentTelemetryState State = state;
        public Task<AgentTelemetryState> ReadAsync(CancellationToken cancellationToken) => Task.FromResult(State);
        public Task ObserveAsync(IReadOnlyList<AgentTelemetryObservation> observations, CancellationToken cancellationToken)
        {
            var records = State.Records.ToDictionary(record => record.ThreadId, StringComparer.Ordinal);
            foreach (var observation in observations)
                records[observation.Sample.ThreadId] = AgentTelemetryAccounting.Apply(records.GetValueOrDefault(observation.Sample.ThreadId), observation);
            State = new(records.Values.ToArray(), null);
            return Task.CompletedTask;
        }
    }

    private sealed class EmptySource : IAgentTelemetrySource
    {
        public Task<IReadOnlyList<AgentTelemetrySample>> ReadAsync(IReadOnlyList<string> ids, CancellationToken token) =>
            throw new InvalidOperationException("Presentation must not query native telemetry.");
    }
    private sealed class EmptyActivity : IAgentActivityStore
    {
        public Task<AgentActivityOverlay> ReadAsync(string root, ArchitectureGraph graph, CancellationToken token) => Task.FromResult(AgentActivityOverlay.Empty);
        public Task AppendScopeAsync(string root, AgentScopeDeclaration declaration, CancellationToken token) => throw new NotSupportedException();
    }
}
