namespace DockiUp.API.Nodes
{
    /// <summary>What a node reports about itself when it dials in and registers. Id is the node's
    /// stable id (a GUID, as string over the wire) so the control plane can persist it and let
    /// projects reference the node across reconnects.</summary>
    public record NodeRegistrationDto(string Id, string Name, string MachineName, string Os, string DockerVersion);

    /// <summary>A node as the control-plane UI sees it. Online is derived from the live SignalR
    /// registry, the rest from the persisted Node row. Pending is true for a node that's been created
    /// but has never connected (empty runtime details).</summary>
    public record NodeDto(
        Guid Id,
        string Name,
        string MachineName,
        string Os,
        string DockerVersion,
        bool Online,
        bool Pending,
        DateTimeOffset FirstSeenAt,
        DateTimeOffset LastSeenAt);

    /// <summary>Result of a control-plane -> node round-trip ping.</summary>
    public record NodePingResultDto(string Reply, int RoundTripMs);

    /// <summary>A not-yet-saved node: a freshly generated token + the control-plane URL the node
    /// should dial. The UI builds the node compose from these and only persists on save.</summary>
    public record NodeDraftDto(string SuggestedName, string Token, string ServerUrl);

    /// <summary>Persist a node from the Add-node modal. The token is the one shown in the draft;
    /// only its hash is stored.</summary>
    public record CreateNodeRequest(string Name, string Token);
}
