/// <summary>
/// Central entry point for upgrading serialized authoritative snapshots.
/// Never scatter schema-version fixes through networking or gameplay code.
/// </summary>
public static class GameStateSnapshotMigration
{
    public static bool TryUpgradeToCurrent(GameStateSnapshot state, out string error)
    {
        error = string.Empty;
        if (state == null)
        {
            error = "Game state is null.";
            return false;
        }

        if (state.SchemaVersion < 0)
        {
            error = "Game-state schema version cannot be negative.";
            return false;
        }

        if (state.SchemaVersion > GameStateSnapshot.CurrentSchemaVersion)
        {
            error = "Game-state schema version " + state.SchemaVersion +
                    " is newer than supported version " + GameStateSnapshot.CurrentSchemaVersion + ".";
            return false;
        }

        // Snapshots created before schema versioning was introduced deserialize this
        // field as 0. Their shape is the schema-1 shape, so the migration is lossless.
        if (state.SchemaVersion == 0)
            state.SchemaVersion = 1;

        if (state.SchemaVersion == 1)
            UpgradeV1ToV2(state);
        if (state.SchemaVersion == 2)
            UpgradeV2ToV3(state);
        if (state.SchemaVersion == 3)
            UpgradeV3ToV4(state);

        return state.SchemaVersion == GameStateSnapshot.CurrentSchemaVersion;
    }

    private static void UpgradeV1ToV2(GameStateSnapshot state)
    {
        if (state.SpecialPiles == null) state.SpecialPiles = new System.Collections.Generic.List<SpecialPileSnapshot>();
        if (state.UnownedArtifacts == null) state.UnownedArtifacts = new System.Collections.Generic.List<int>();
        if (state.SetAsideCards == null) state.SetAsideCards = new System.Collections.Generic.List<SetAsideCardSnapshot>();
        if (state.Players != null)
            foreach (PlayerStateSnapshot player in state.Players)
                if (player != null && player.Artifacts == null)
                    player.Artifacts = new System.Collections.Generic.List<int>();
        state.SchemaVersion = 2;
    }

    private static void UpgradeV2ToV3(GameStateSnapshot state)
    {
        if (state.Players != null)
            foreach (PlayerStateSnapshot player in state.Players)
                if (player != null && player.ResolvedDurationCards == null)
                    player.ResolvedDurationCards = new System.Collections.Generic.List<int>();
        state.SchemaVersion = 3;
    }

    private static void UpgradeV3ToV4(GameStateSnapshot state)
    {
        // Reserve creation has always appended the ten Kingdom piles after the
        // seven base piles, so old snapshots can recover this metadata losslessly.
        if (state.SupplyPiles != null)
            for (int index = 7; index < state.SupplyPiles.Count; index++)
                if (state.SupplyPiles[index] != null)
                    state.SupplyPiles[index].IsKingdom = true;
        state.SchemaVersion = 4;
    }
}
