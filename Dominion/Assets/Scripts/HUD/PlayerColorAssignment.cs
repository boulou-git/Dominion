using System;
using System.Collections.Generic;

/// <summary>
/// Produces the same palette slot on every client without storing presentation data
/// in the replicated GameState. Hashing makes the initial choice feel varied, while
/// deterministic collision resolution keeps player colors distinct when possible.
/// </summary>
public static class PlayerColorAssignment
{
    public static int ResolvePaletteIndex(GameStateSnapshot state, string playerId, int paletteSize)
    {
        if (paletteSize <= 0 || string.IsNullOrEmpty(playerId))
            return -1;

        List<string> playerIds = CollectSortedPlayerIds(state);
        if (!playerIds.Contains(playerId))
            return (int)(StableHash(playerId) % (uint)paletteSize);

        HashSet<int> occupied = new HashSet<int>();
        foreach (string id in playerIds)
        {
            int index = (int)(StableHash(id) % (uint)paletteSize);
            if (occupied.Count < paletteSize)
            {
                while (occupied.Contains(index))
                    index = (index + 1) % paletteSize;
                occupied.Add(index);
            }

            if (string.Equals(id, playerId, StringComparison.Ordinal))
                return index;
        }

        return -1;
    }

    private static List<string> CollectSortedPlayerIds(GameStateSnapshot state)
    {
        List<string> ids = new List<string>();
        if (state != null && state.Players != null)
        {
            foreach (PlayerStateSnapshot player in state.Players)
            {
                if (player == null || string.IsNullOrEmpty(player.PlayerId) || ids.Contains(player.PlayerId))
                    continue;
                ids.Add(player.PlayerId);
            }
        }
        ids.Sort(StringComparer.Ordinal);
        return ids;
    }

    private static uint StableHash(string value)
    {
        unchecked
        {
            const uint offset = 2166136261;
            const uint prime = 16777619;
            uint hash = offset;
            for (int i = 0; i < value.Length; i++)
            {
                hash ^= value[i];
                hash *= prime;
            }
            return hash;
        }
    }
}
