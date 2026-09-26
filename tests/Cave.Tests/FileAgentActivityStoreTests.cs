using System.Text.Json;
using Cave.Application;
using Cave.Domain;
using Cave.Infrastructure.Activity;
using Cave.Infrastructure.Conversation;

namespace Cave.Tests;

/// <summary>
/// Verifies the repository-local activity stream and its semantic projection.
/// </summary>
public sealed class FileAgentActivityStoreTests
{
    /// <summary>
    /// Verifies that a newer temporary fork cannot replace the exact main-task binding.
    /// </summary>
    [Fact]
    public async Task ResolveAsyncExcludesMarkedEphemeralSession()
    {
        var workspaceRoot = CreateWorkspace();
        var observedAt = new DateTimeOffset(2026, 8, 23, 13, 0, 0, TimeSpan.Zero);
        try
        {
            var directory = FileAgentActivityStore.GetActivityDirectory(workspaceRoot);
            Directory.CreateDirectory(directory);
            foreach (var item in new[]
                     {
                         new { EventId = "main", SessionId = "main-session", OccurredAt = observedAt },
                         new { EventId = "memo", SessionId = "memo-session", OccurredAt = observedAt.AddSeconds(1) },
                     })
            {
                var payload = new
                {
                    schemaVersion = 1,
                    eventId = item.EventId,
                    kind = "SessionStart",
                    occurredAtUtc = item.OccurredAt,
                    sessionId = item.SessionId,
                    turnId = (string?)null,
                    workspaceRoot,
                    toolName = (string?)null,
                    isMutation = false,
                    phase = "Thinking",
                    paths = Array.Empty<string>(),
                    summary = "Thinking",
                };
                await File.WriteAllTextAsync(
                    Path.Combine(directory, $"{item.OccurredAt:yyyyMMddHHmmssfffffff}-{item.EventId}.json"),
                    JsonSerializer.Serialize(payload));
            }

            await EphemeralConversationSession.MarkAsync(
                workspaceRoot,
                "memo-session",
                "main-session",
                observedAt.AddSeconds(1),
                CancellationToken.None);
            var store = new FileAgentActivityStore(new FixedTimeProvider(observedAt.AddSeconds(2)));

            var binding = await store.ResolveAsync(workspaceRoot, CancellationToken.None);
            var overlay = await store.ReadAsync(workspaceRoot, CreateGraph(), CancellationToken.None);

            Assert.NotNull(binding);
            Assert.Equal("main-session", binding.SessionId);
            Assert.DoesNotContain(overlay.Agents, item => item.AgentId.Contains("memo-session", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(workspaceRoot, recursive: true);
        }
    }

    /// <summary>
    /// Verifies that exact declared project scope remains distinguishable from observed hook activity.
    /// </summary>
    [Fact]
    public async Task AppendScopeMapsExactProjectAsDeclaredEvidence()
    {
        var workspaceRoot = CreateWorkspace();
        var now = new DateTimeOffset(2026, 8, 17, 12, 0, 0, TimeSpan.Zero);
        try
        {
            var store = new FileAgentActivityStore(new FixedTimeProvider(now));
            await store.AppendScopeAsync(
                workspaceRoot,
                new AgentScopeDeclaration(
                    "agent-main",
                    "implementation",
                    IsSubagent: false,
                    AgentWorkState.Active,
                    "Implement live activity",
                    ["Cave.Application"],
                    [],
                    [],
                    []),
                CancellationToken.None);

            var overlay = await store.ReadAsync(workspaceRoot, CreateGraph(), CancellationToken.None);

            var agent = Assert.Single(overlay.Agents);
            Assert.Equal(AgentWorkState.Active, agent.State);
            Assert.Equal(AgentActivityPhase.Working, agent.Phase);
            Assert.True(agent.HasDeclaredScope);
            Assert.False(agent.HasObservedActivity);
            var node = Assert.Single(overlay.Nodes);
            Assert.Equal("project:application", node.NodeId);
            Assert.Equal(AgentActivityEvidenceKind.Declared, node.Evidence);
        }
        finally
        {
            Directory.Delete(workspaceRoot, recursive: true);
        }
    }

    /// <summary>
    /// Verifies that main-agent scope is attached to the exact hook-observed task instead of
    /// materializing a second synthetic main agent.
    /// </summary>
    [Fact]
    public async Task AppendScopeBindsMainDeclarationToObservedTask()
    {
        var workspaceRoot = CreateWorkspace();
        var observedAt = new DateTimeOffset(2026, 8, 23, 20, 0, 0, TimeSpan.Zero);
        try
        {
            var directory = FileAgentActivityStore.GetActivityDirectory(workspaceRoot);
            Directory.CreateDirectory(directory);
            await File.WriteAllTextAsync(
                Path.Combine(directory, "202608232000000000000-prompt.json"),
                JsonSerializer.Serialize(new
                {
                    schemaVersion = 1,
                    eventId = "prompt",
                    kind = "UserPromptSubmit",
                    occurredAtUtc = observedAt,
                    sessionId = "task-1",
                    turnId = "turn-1",
                    isSubagent = false,
                    workspaceRoot,
                    paths = Array.Empty<string>(),
                    summary = "New instruction received",
                }));

            var store = new FileAgentActivityStore(
                new FixedTimeProvider(observedAt.AddSeconds(1)));
            await store.AppendScopeAsync(
                workspaceRoot,
                new AgentScopeDeclaration(
                    "main",
                    "main",
                    IsSubagent: false,
                    AgentWorkState.Active,
                    "Implementing the timeline",
                    ["Cave.Application"],
                    [],
                    [],
                    []),
                CancellationToken.None);

            var overlay = await store.ReadAsync(workspaceRoot, CreateGraph(), CancellationToken.None);

            var agent = Assert.Single(overlay.Agents);
            Assert.Equal("session:task-1", agent.AgentId);
            Assert.True(agent.HasObservedActivity);
            Assert.True(agent.HasDeclaredScope);
            Assert.Equal("Implementing the timeline", agent.Summary);
            Assert.Contains(overlay.Nodes, node => node.AgentId == agent.AgentId
                && node.Evidence == AgentActivityEvidenceKind.Declared);
        }
        finally
        {
            Directory.Delete(workspaceRoot, recursive: true);
        }
    }

    /// <summary>
    /// Verifies compatibility with fresh main declarations written before scope events carried
    /// the hook-bound session id.
    /// </summary>
    [Fact]
    public async Task LegacyMainDeclarationCoalescesWithObservedTask()
    {
        var workspaceRoot = CreateWorkspace();
        var observedAt = new DateTimeOffset(2026, 8, 23, 20, 0, 0, TimeSpan.Zero);
        try
        {
            var directory = FileAgentActivityStore.GetActivityDirectory(workspaceRoot);
            Directory.CreateDirectory(directory);
            var events = new object[]
            {
                new
                {
                    schemaVersion = 1,
                    eventId = "prompt",
                    kind = "UserPromptSubmit",
                    occurredAtUtc = observedAt,
                    sessionId = "task-1",
                    turnId = "turn-1",
                    agentId = (string?)null,
                    agentType = "main",
                    isSubagent = false,
                    workspaceRoot,
                    paths = Array.Empty<string>(),
                    summary = "New instruction received",
                    state = (string?)null,
                    scope = (object?)null,
                },
                new
                {
                    schemaVersion = 1,
                    eventId = "legacy-scope",
                    kind = "ScopeDeclared",
                    occurredAtUtc = observedAt.AddSeconds(1),
                    sessionId = (string?)null,
                    turnId = (string?)null,
                    agentId = "main",
                    agentType = "main",
                    isSubagent = false,
                    workspaceRoot,
                    paths = Array.Empty<string>(),
                    summary = "Implementing the timeline",
                    state = "Active",
                    scope = new
                    {
                        projects = new[] { "Cave.Application" },
                        namespaces = Array.Empty<string>(),
                        classes = Array.Empty<string>(),
                        files = Array.Empty<string>(),
                    },
                },
                new
                {
                    schemaVersion = 1,
                    eventId = "tool",
                    kind = "PostToolUse",
                    occurredAtUtc = observedAt.AddSeconds(2),
                    sessionId = "task-1",
                    turnId = "turn-1",
                    agentId = (string?)null,
                    agentType = "main",
                    isSubagent = false,
                    workspaceRoot,
                    paths = new[] { "src/Cave.Application/ArchitectureSnapshotService.cs" },
                    summary = "Reading architecture",
                    state = (string?)null,
                    scope = (object?)null,
                },
            };
            for (var index = 0; index < events.Length; index++)
            {
                await File.WriteAllTextAsync(
                    Path.Combine(directory, $"2026082320000{index}0000000-{index}.json"),
                    JsonSerializer.Serialize(events[index]));
            }

            var store = new FileAgentActivityStore(
                new FixedTimeProvider(observedAt.AddSeconds(3)));
            var overlay = await store.ReadAsync(workspaceRoot, CreateGraph(), CancellationToken.None);

            var agent = Assert.Single(overlay.Agents);
            Assert.Equal("session:task-1", agent.AgentId);
            Assert.True(agent.HasObservedActivity);
            Assert.True(agent.HasDeclaredScope);
            Assert.Equal(AgentWorkState.Active, agent.State);
        }
        finally
        {
            Directory.Delete(workspaceRoot, recursive: true);
        }
    }

    /// <summary>
    /// Verifies that privacy-minimized PostToolUse paths create objective node and recent-edit evidence.
    /// </summary>
    [Fact]
    public async Task PostToolUseMapsObservedPathAndRecentEdit()
    {
        var workspaceRoot = CreateWorkspace();
        var observedAt = new DateTimeOffset(2026, 8, 17, 12, 0, 0, TimeSpan.Zero);
        try
        {
            var directory = FileAgentActivityStore.GetActivityDirectory(workspaceRoot);
            Directory.CreateDirectory(directory);
            var payload = new
            {
                schemaVersion = 1,
                eventId = "event-1",
                kind = "PostToolUse",
                occurredAtUtc = observedAt,
                sessionId = "session-1",
                turnId = "turn-1",
                workspaceRoot,
                toolName = "apply_patch",
                isMutation = true,
                phase = "Editing",
                paths = new[] { "src/Cave.Application/ArchitectureSnapshotService.cs" },
                summary = "apply_patch: src/Cave.Application/ArchitectureSnapshotService.cs",
            };
            await File.WriteAllTextAsync(
                Path.Combine(directory, "202608171200000000000-event-1.json"),
                JsonSerializer.Serialize(payload));
            var readPayload = new
            {
                schemaVersion = 1,
                eventId = "event-2",
                kind = "PostToolUse",
                occurredAtUtc = observedAt.AddMilliseconds(500),
                sessionId = "session-1",
                turnId = "turn-1",
                workspaceRoot,
                toolName = "read_file",
                isMutation = false,
                phase = "Reading",
                paths = new[] { "src/Cave.Application/ArchitectureSnapshotService.cs" },
                summary = "read_file: src/Cave.Application/ArchitectureSnapshotService.cs",
            };
            await File.WriteAllTextAsync(
                Path.Combine(directory, "202608171200005000000-event-2.json"),
                JsonSerializer.Serialize(readPayload));

            var store = new FileAgentActivityStore(
                new FixedTimeProvider(observedAt.AddSeconds(1)));
            var overlay = await store.ReadAsync(workspaceRoot, CreateGraph(), CancellationToken.None);

            var agent = Assert.Single(overlay.Agents);
            Assert.Equal("session:session-1", agent.AgentId);
            Assert.True(agent.HasObservedActivity);
            Assert.False(agent.HasDeclaredScope);
            Assert.Equal(AgentWorkState.Active, agent.State);
            Assert.Equal(AgentActivityPhase.Reading, agent.Phase);
            Assert.Equal(AgentActivityEvidenceKind.Observed, Assert.Single(overlay.Nodes).Evidence);
            Assert.Equal("src/Cave.Application/ArchitectureSnapshotService.cs", Assert.Single(overlay.RecentEdits).FilePath);
        }
        finally
        {
            Directory.Delete(workspaceRoot, recursive: true);
        }
    }

    /// <summary>
    /// Verifies that activity still maps to a project when CodeGraph omits the edited symbol kind.
    /// </summary>
    [Fact]
    public async Task PostToolUseFallsBackToContainingProjectForUnprojectedSource()
    {
        var workspaceRoot = CreateWorkspace();
        var observedAt = new DateTimeOffset(2026, 8, 17, 12, 0, 0, TimeSpan.Zero);
        try
        {
            var directory = FileAgentActivityStore.GetActivityDirectory(workspaceRoot);
            Directory.CreateDirectory(directory);
            var payload = new
            {
                schemaVersion = 1,
                eventId = "typescript-function",
                kind = "PostToolUse",
                occurredAtUtc = observedAt,
                sessionId = "session-ui",
                turnId = "turn-1",
                workspaceRoot,
                toolName = "apply_patch",
                isMutation = true,
                paths = new[] { "src/Cave.Ui/src/api.ts" },
                summary = "apply_patch: src/Cave.Ui/src/api.ts",
            };
            await File.WriteAllTextAsync(
                Path.Combine(directory, "202608171200000000000-typescript-function.json"),
                JsonSerializer.Serialize(payload));
            var graph = ArchitectureGraph.Create(
                [
                    new ArchitectureNode(
                        "project:ui",
                        ArchitectureNodeKind.Project,
                        "cave-ui",
                        null,
                        "src/Cave.Ui",
                        "TypeScript package",
                        "project",
                        ["typescript"],
                        []),
                ],
                []);

            var store = new FileAgentActivityStore(new FixedTimeProvider(observedAt));
            var overlay = await store.ReadAsync(workspaceRoot, graph, CancellationToken.None);

            Assert.Equal("project:ui", Assert.Single(overlay.Nodes).NodeId);
            Assert.Empty(overlay.UnmappedPaths);
            Assert.Equal("src/Cave.Ui/src/api.ts", Assert.Single(overlay.RecentEdits).FilePath);
        }
        finally
        {
            Directory.Delete(workspaceRoot, recursive: true);
        }
    }

    /// <summary>
    /// Verifies that an abandoned active declaration becomes idle instead of pulsing forever.
    /// </summary>
    [Fact]
    public async Task ActiveDeclarationExpiresWithoutFreshEvidence()
    {
        var workspaceRoot = CreateWorkspace();
        var declaredAt = new DateTimeOffset(2026, 8, 17, 12, 0, 0, TimeSpan.Zero);
        try
        {
            var directory = FileAgentActivityStore.GetActivityDirectory(workspaceRoot);
            Directory.CreateDirectory(directory);
            var payload = new
            {
                schemaVersion = 1,
                eventId = "scope-active",
                kind = "ScopeDeclared",
                occurredAtUtc = declaredAt,
                agentId = "agent-sub",
                agentType = "implementation",
                isSubagent = true,
                workspaceRoot,
                paths = Array.Empty<string>(),
                summary = "Implementing activity indicators",
                state = "Active",
                scope = new
                {
                    projects = new[] { "Cave.Application" },
                    namespaces = Array.Empty<string>(),
                    classes = Array.Empty<string>(),
                    files = Array.Empty<string>(),
                },
            };
            await File.WriteAllTextAsync(
                Path.Combine(directory, "202608171200000000000-scope-active.json"),
                JsonSerializer.Serialize(payload));

            var store = new FileAgentActivityStore(
                new FixedTimeProvider(declaredAt.AddMinutes(5).AddSeconds(1)));
            var overlay = await store.ReadAsync(workspaceRoot, CreateGraph(), CancellationToken.None);

            var agent = Assert.Single(overlay.Agents);
            Assert.Equal(AgentWorkState.Idle, agent.State);
            Assert.Equal(AgentActivityEvidenceKind.Declared, Assert.Single(overlay.Nodes).Evidence);
        }
        finally
        {
            Directory.Delete(workspaceRoot, recursive: true);
        }
    }

    /// <summary>
    /// Verifies that long reasoning gaps remain live while a turn still holds its bounded lease.
    /// </summary>
    [Fact]
    public async Task ActiveAgentSurvivesASeveralMinuteThinkingGap()
    {
        var workspaceRoot = CreateWorkspace();
        var observedAt = new DateTimeOffset(2026, 8, 17, 12, 0, 0, TimeSpan.Zero);
        try
        {
            var directory = FileAgentActivityStore.GetActivityDirectory(workspaceRoot);
            Directory.CreateDirectory(directory);
            await File.WriteAllTextAsync(
                Path.Combine(directory, "202608171200000000000-thinking.json"),
                JsonSerializer.Serialize(new
                {
                    schemaVersion = 1,
                    eventId = "thinking",
                    kind = "UserPromptSubmit",
                    occurredAtUtc = observedAt,
                    sessionId = "session-thinking",
                    turnId = "turn-1",
                    workspaceRoot,
                    phase = "Thinking",
                    paths = Array.Empty<string>(),
                    summary = "New instruction received",
                }));

            var store = new FileAgentActivityStore(
                new FixedTimeProvider(observedAt.AddMinutes(4)));
            var overlay = await store.ReadAsync(workspaceRoot, CreateGraph(), CancellationToken.None);

            Assert.Equal(AgentWorkState.Active, Assert.Single(overlay.Agents).State);
            Assert.Equal(AgentActivitySourceStatus.Ready, overlay.SourceStatus);
        }
        finally
        {
            Directory.Delete(workspaceRoot, recursive: true);
        }
    }

    /// <summary>
    /// Verifies that a terminal event wins a concurrent timestamp tie.
    /// </summary>
    [Theory]
    [InlineData("Stop")]
    [InlineData("Interrupt")]
    public async Task TurnTerminalEventWinsTimestampTieWithActiveToolEvent(string terminalKind)
    {
        var workspaceRoot = CreateWorkspace();
        var observedAt = new DateTimeOffset(2026, 8, 17, 12, 0, 0, TimeSpan.Zero);
        try
        {
            var directory = FileAgentActivityStore.GetActivityDirectory(workspaceRoot);
            Directory.CreateDirectory(directory);
            var active = new
            {
                schemaVersion = 1,
                eventId = "z-active",
                kind = "PostToolUse",
                occurredAtUtc = observedAt,
                sessionId = "session-tie",
                turnId = "turn-1",
                workspaceRoot,
                toolName = "Bash",
                phase = "Reading",
                paths = Array.Empty<string>(),
                summary = "Reading workspace evidence",
            };
            var stopped = new
            {
                schemaVersion = 1,
                eventId = "a-stop",
                kind = terminalKind,
                occurredAtUtc = observedAt,
                sessionId = "session-tie",
                turnId = "turn-1",
                workspaceRoot,
                paths = Array.Empty<string>(),
                summary = "Waiting for the next turn",
            };
            await File.WriteAllTextAsync(
                Path.Combine(directory, "202608171200000000000-a-stop.json"),
                JsonSerializer.Serialize(stopped));
            await File.WriteAllTextAsync(
                Path.Combine(directory, "202608171200000000000-z-active.json"),
                JsonSerializer.Serialize(active));

            var store = new FileAgentActivityStore(new FixedTimeProvider(observedAt));
            var overlay = await store.ReadAsync(workspaceRoot, CreateGraph(), CancellationToken.None);

            Assert.Equal(AgentWorkState.Idle, Assert.Single(overlay.Agents).State);
            var binding = await store.ResolveAsync(workspaceRoot, CancellationToken.None);
            Assert.NotNull(binding);
            Assert.Equal("session-tie", binding.SessionId);
            Assert.False(binding.IsActive);
        }
        finally
        {
            Directory.Delete(workspaceRoot, recursive: true);
        }
    }

    /// <summary>
    /// Verifies that newer observed lifecycle evidence overrides an older active declaration.
    /// </summary>
    [Fact]
    public async Task SubagentStopOverridesEarlierActiveDeclaration()
    {
        var workspaceRoot = CreateWorkspace();
        var declaredAt = new DateTimeOffset(2026, 8, 17, 12, 0, 0, TimeSpan.Zero);
        try
        {
            var directory = FileAgentActivityStore.GetActivityDirectory(workspaceRoot);
            Directory.CreateDirectory(directory);
            var declaration = new
            {
                schemaVersion = 1,
                eventId = "scope-active",
                kind = "ScopeDeclared",
                occurredAtUtc = declaredAt,
                agentId = "agent-sub",
                agentType = "implementation",
                isSubagent = true,
                workspaceRoot,
                paths = Array.Empty<string>(),
                summary = "Implementing activity indicators",
                state = "Active",
                scope = new
                {
                    projects = new[] { "Cave.Application" },
                    namespaces = Array.Empty<string>(),
                    classes = Array.Empty<string>(),
                    files = Array.Empty<string>(),
                },
            };
            var stopped = new
            {
                schemaVersion = 1,
                eventId = "subagent-stop",
                kind = "SubagentStop",
                occurredAtUtc = declaredAt.AddSeconds(30),
                agentId = "agent-sub",
                agentType = "implementation",
                isSubagent = true,
                workspaceRoot,
                paths = Array.Empty<string>(),
                summary = "Subagent completed",
            };
            await File.WriteAllTextAsync(
                Path.Combine(directory, "202608171200000000000-scope-active.json"),
                JsonSerializer.Serialize(declaration));
            await File.WriteAllTextAsync(
                Path.Combine(directory, "202608171200300000000-subagent-stop.json"),
                JsonSerializer.Serialize(stopped));

            var store = new FileAgentActivityStore(
                new FixedTimeProvider(declaredAt.AddSeconds(31)));
            var overlay = await store.ReadAsync(workspaceRoot, CreateGraph(), CancellationToken.None);

            var agent = Assert.Single(overlay.Agents);
            Assert.Equal(AgentWorkState.Completed, agent.State);
            Assert.Equal(declaredAt.AddSeconds(30), agent.UpdatedAtUtc);
        }
        finally
        {
            Directory.Delete(workspaceRoot, recursive: true);
        }
    }

    /// <summary>
    /// Verifies that a new prompt exposes only a stable instruction boundary and never requires prompt content.
    /// </summary>
    [Fact]
    public async Task UserPromptSubmitPublishesInstructionMarker()
    {
        var workspaceRoot = CreateWorkspace();
        var observedAt = new DateTimeOffset(2026, 8, 17, 12, 5, 0, TimeSpan.Zero);
        try
        {
            var directory = FileAgentActivityStore.GetActivityDirectory(workspaceRoot);
            Directory.CreateDirectory(directory);
            var payload = new
            {
                schemaVersion = 1,
                eventId = "instruction-event",
                kind = "UserPromptSubmit",
                occurredAtUtc = observedAt,
                sessionId = "session-1",
                turnId = "turn-2",
                workspaceRoot,
                paths = Array.Empty<string>(),
                summary = "New instruction received",
                phase = "Thinking",
            };
            await File.WriteAllTextAsync(
                Path.Combine(directory, "202608171205000000000-instruction-event.json"),
                JsonSerializer.Serialize(payload));

            var store = new FileAgentActivityStore(new FixedTimeProvider(observedAt));
            var overlay = await store.ReadAsync(workspaceRoot, CreateGraph(), CancellationToken.None);

            Assert.NotNull(overlay.LatestInstruction);
            Assert.Equal("session-1:turn-2", overlay.LatestInstruction.Id);
            Assert.Equal(observedAt, overlay.LatestInstruction.ObservedAtUtc);
            var agent = Assert.Single(overlay.Agents);
            Assert.Equal("New instruction received", agent.Summary);
            Assert.Equal(AgentActivityPhase.Thinking, agent.Phase);
        }
        finally
        {
            Directory.Delete(workspaceRoot, recursive: true);
        }
    }

    /// <summary>
    /// Verifies that the browser bridge receives the exact main-task identity and respects lifecycle ownership.
    /// </summary>
    [Theory]
    [InlineData("Stop")]
    [InlineData("Interrupt")]
    public async Task ResolveAsyncTracksExactMainTaskUntilObservedTerminal(string terminalKind)
    {
        var workspaceRoot = CreateWorkspace();
        var observedAt = new DateTimeOffset(2026, 8, 23, 9, 0, 0, TimeSpan.Zero);
        try
        {
            var directory = FileAgentActivityStore.GetActivityDirectory(workspaceRoot);
            Directory.CreateDirectory(directory);
            await File.WriteAllTextAsync(
                Path.Combine(directory, "202608230900000000000-prompt.json"),
                JsonSerializer.Serialize(new
                {
                    schemaVersion = 1,
                    eventId = "prompt",
                    kind = "UserPromptSubmit",
                    occurredAtUtc = observedAt,
                    sessionId = "01exact-task",
                    turnId = "turn-active",
                    isSubagent = false,
                    workspaceRoot,
                    paths = Array.Empty<string>(),
                }));

            var store = new FileAgentActivityStore(new FixedTimeProvider(observedAt.AddSeconds(1)));
            var active = await store.ResolveAsync(workspaceRoot, CancellationToken.None);

            Assert.NotNull(active);
            Assert.Equal("01exact-task", active.SessionId);
            Assert.Equal("turn-active", active.TurnId);
            Assert.True(active.IsActive);

            var quietLongRunningStore = new FileAgentActivityStore(
                new FixedTimeProvider(observedAt.AddMinutes(30)));
            var quietLongRunning = await quietLongRunningStore.ResolveAsync(
                workspaceRoot,
                CancellationToken.None);

            Assert.NotNull(quietLongRunning);
            Assert.True(quietLongRunning.IsActive);

            var stoppedAt = observedAt.AddSeconds(2);
            await File.WriteAllTextAsync(
                Path.Combine(directory, "202608230900020000000-stop.json"),
                JsonSerializer.Serialize(new
                {
                    schemaVersion = 1,
                    eventId = "stop",
                    kind = terminalKind,
                    occurredAtUtc = stoppedAt,
                    sessionId = "01exact-task",
                    turnId = "turn-active",
                    isSubagent = false,
                    workspaceRoot,
                    paths = Array.Empty<string>(),
                }));

            var idleStore = new FileAgentActivityStore(new FixedTimeProvider(stoppedAt.AddSeconds(1)));
            var idle = await idleStore.ResolveAsync(workspaceRoot, CancellationToken.None);

            Assert.NotNull(idle);
            Assert.Equal("01exact-task", idle.SessionId);
            Assert.Equal("turn-active", idle.TurnId);
            Assert.False(idle.IsActive);
        }
        finally
        {
            Directory.Delete(workspaceRoot, recursive: true);
        }
    }

    private static string CreateWorkspace()
    {
        var path = Path.Combine(Path.GetTempPath(), "cave-activity-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static ArchitectureGraph CreateGraph() => ArchitectureGraph.Create(
        [
            new ArchitectureNode(
                "project:application",
                ArchitectureNodeKind.Project,
                "Cave.Application",
                null,
                "Cave.Application",
                null,
                "core",
                [],
                [new SourceLocation("src/Cave.Application/ArchitectureSnapshotService.cs", 1, 70)]),
        ],
        []);

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
