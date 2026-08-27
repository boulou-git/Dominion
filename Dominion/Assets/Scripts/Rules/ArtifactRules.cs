using System;

/// <summary>Ownership transfer for unique extension Artefacts.</summary>
public static class ArtifactRules
{
    public static bool TryTake(GameStateSnapshot state, PlayerStateSnapshot newOwner, string definitionId,
        int sourceCardInstanceId, GameEventBus eventBus, out int artifactInstanceId, out string error)
    {
        artifactInstanceId = 0;
        error = string.Empty;
        if (state == null || newOwner == null || string.IsNullOrWhiteSpace(definitionId))
        { error = "Taking an Artefact requires a state, player and definition."; return false; }

        CardInstance artifact = state.CardInstances != null
            ? state.CardInstances.Find(card => card != null &&
                string.Equals(card.DefinitionId, definitionId, StringComparison.OrdinalIgnoreCase))
            : null;
        if (artifact == null) { error = "Artefact was not found: " + definitionId; return false; }

        if (state.UnownedArtifacts != null) state.UnownedArtifacts.Remove(artifact.InstanceId);
        if (state.Players != null)
            foreach (PlayerStateSnapshot player in state.Players)
                if (player != null && player.Artifacts != null)
                    player.Artifacts.Remove(artifact.InstanceId);

        if (newOwner.Artifacts == null) newOwner.Artifacts = new System.Collections.Generic.List<int>();
        newOwner.Artifacts.Add(artifact.InstanceId);
        artifact.OwnerPlayerId = newOwner.PlayerId;
        artifactInstanceId = artifact.InstanceId;
        eventBus?.Publish(GameEvent.ArtifactGained(newOwner.PlayerId, artifact.InstanceId,
            artifact.DefinitionId, sourceCardInstanceId));
        return true;
    }

    public static bool Controls(GameStateSnapshot state, PlayerStateSnapshot player, string definitionId)
    {
        if (state == null || player == null || player.Artifacts == null || state.CardInstances == null) return false;
        foreach (int id in player.Artifacts)
        {
            CardInstance instance = state.CardInstances.Find(card => card != null && card.InstanceId == id);
            if (instance != null && string.Equals(instance.DefinitionId, definitionId, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }
}
