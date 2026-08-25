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

        // Future migrations belong here, one explicit step at a time:
        // if (state.SchemaVersion == 1) UpgradeV1ToV2(state);

        return state.SchemaVersion == GameStateSnapshot.CurrentSchemaVersion;
    }
}
