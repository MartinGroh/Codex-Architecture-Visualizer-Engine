namespace Cave.Infrastructure.Demo;

internal static class DemoSnapshotHandoff
{
    internal const string Text = """
        The Atlas preview uses one workspace snapshot. I traced the handoff from the semantic index to the browser and found that the projection change belongs in `GraphProjector`. The API can keep serving the same snapshot to the first page load and the live event stream. The local preview is ready once the reconnect check below passes. This walkthrough uses the synthetic Atlas workspace.

        ## Workspace snapshot handoff

        `SemanticIndexAdapter` translates provider records into `ArchitectureGraph`. `GraphProjector` applies the selected projection policy to produce a view of that graph. `SnapshotEndpoints` delivers the result over HTTP and streams later revisions. `ArchitectureCanvas` keeps the operator's view state, including the selected class and zoom level. Those values can survive refreshes because a class keeps its identity when unrelated evidence changes.

        The risky point is the transition from the initial HTTP response to the event stream. If the viewer gets revision 41 over HTTP and then receives 42 from the stream, it can replace the displayed state in one operation. If it sees 41 again after rendering 42, it keeps 42. Reconnection starts by reading the current snapshot before subscribing again. This gives the viewer a fresh point of reference when it has missed several updates.

        ### Evidence at each boundary

        Start with `SnapshotEndpoints` when the page stalls. Compare the workspace key and snapshot revision at each step, then use the table to narrow down the mismatch.

        | Boundary | Authoritative evidence | Representative file or symbol | What the reviewer should check | Recovery signal |
        | --- | --- | --- | --- | --- |
        | Semantic ingestion | Stable symbol identity and source location | `src/Atlas.Infrastructure/Semantics/SemanticIndexAdapter.cs` | A namespace move preserves the assembly context of the affected type | The updated symbol can be found by its qualified name |
        | Application projection | Selected policy and visible endpoints | `src/Atlas.Application/Projection/GraphProjector.cs` | The aggregated edge uses the visible ancestors of the same underlying dependency | Opening the branch shows the detailed dependency again |
        | Initial page load | Workspace key and snapshot revision | `src/Atlas.Api/SnapshotEndpoints.cs` | The selected workspace returns a complete snapshot before the client subscribes to events | The canvas has rendered a known revision |
        | Live update | Newer revision for the selected workspace | `src/Atlas.Web/ArchitectureCanvas.tsx` | A duplicate event leaves the current selection and zoom alone | The next accepted revision appears once |
        | Change overlay | Resolved baseline and hunk location | `src/Atlas.Infrastructure/VersionControl/GitDeltaProvider.cs` | A changed file maps to the correct class or retains project-level evidence | Changes shows its counts against the expected baseline |

        ### Verify the review path

        1. Open the Atlas preview and select Projects. Expand `Atlas.Application`, then select `GraphProjector`. Record the selected workspace key, snapshot revision and Git baseline before continuing.
        2. Switch to Classes and return to Projects. Check the incoming dependency from `SnapshotEndpoints` at both levels. At project level the endpoint and projector appear under their owning projects and the route still points from consumer to provider.
        3. With `GraphProjector` selected, let the fixture publish another revision for an unrelated project. The visible detail stays anchored to `GraphProjector`. If the selection disappears, compare the underlying node ID before investigating layout.
        4. Close the event stream and reopen it. The client reads the current snapshot first. Verify that replaying the last event from before the disconnect leaves the current revision in place.

        An illustrative event trace shows why the original read after disconnect matters. The client can jump from revision 41 to revision 43 because each event contains a complete snapshot.

        ```text
        14:06:08.211 snapshot received workspace=atlas-preview revision=41
        14:06:08.284 selection restored node=class:GraphProjector level=Projects
        14:06:09.031 event-stream closed workspace=atlas-preview lastAcceptedRevision=41
        14:06:09.487 snapshot received workspace=atlas-preview revision=43 evidence=src/Atlas.Application/Projection/GraphProjector.cs
        14:06:09.491 duplicate event ignored workspace=atlas-preview revision=41 currentRevision=43
        14:06:11.602 live update rendered workspace=atlas-preview revision=44 selectedNode=class:GraphProjector
        ```

        ## How updates reach the viewer

        Arrows show the order in which evidence is translated, projected and delivered in the Atlas preview.

        ```mermaid
        flowchart LR
            Index[SemanticIndexAdapter] --> Graph[ArchitectureGraph]
            Graph --> Projector[GraphProjector]
            Git[GitDeltaProvider] --> Projector
            Projector --> Snapshot[Workspace snapshot]
            Snapshot --> Api[SnapshotEndpoints]
            Api -->|Initial response| Viewer[ArchitectureCanvas]
            Api -->|Live revisions| Viewer
        ```

        The static relations within `ArchitectureGraph` answer a different question. They point from the code that uses a dependency to the code that supplies it. The preview's Changes view should still trace file edits back to the resolved Git baseline. The diagram above shows how a snapshot reaches the viewer and can be checked independently of those relations.

        ### Interpret partial evidence

        - If the semantic index is catching up, preserve the last usable structure and label its freshness. Check the latest indexed source location before reporting that a symbol disappeared.
        - If Git cannot resolve the requested baseline, leave the change count unavailable. A count of zero would tell a reviewer that the comparison succeeded and found an unchanged file.
        - If the selected node genuinely leaves the graph, move the selection to its surviving project. Keep that explanation local to the selection so the operator can continue inspecting other projects.
        - If the viewer receives a snapshot for a different workspace during a switch, match it against the new workspace key before applying it. A revision alone carries no meaning across workspaces.

        ## Acceptance and follow-up

        I would accept the projection change when the same synthetic dependency can be followed at Projects and Classes, a new revision leaves the selected class anchored, and a reconnect keeps the selected workspace current. Record the observed revision before the disconnect and the revision after the fresh read so the next reviewer can tell whether the recovery was exercised.

        The preview covers one workspace with both a changed file and an unchanged dependency. The next review should repeat the reconnect case with a project switch in flight, then record both workspace keys in its evidence. That catches a late event from the old workspace even when its revision is numerically higher.

        **Ready for the next local preview** once the reconnect trace has been captured.
        """;
}
