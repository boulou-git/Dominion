using System;
using System.Collections.Generic;
using System.Linq;

public sealed class RankedPlayerScore
{
    public int Rank { get; }
    public PlayerScoreResult Score { get; }
    public int TurnsTaken { get; }
    public bool IsTied { get; }

    public RankedPlayerScore(int rank, PlayerScoreResult score, int turnsTaken, bool isTied)
    {
        Rank = rank;
        Score = score;
        TurnsTaken = turnsTaken;
        IsTied = isTied;
    }
}

/// <summary>
/// Official Dominion ranking rule: most points wins; among equal point totals,
/// the player who took fewer turns ranks higher. Equal points and equal turns is a tie.
/// </summary>
public static class FinalRankingRules
{
    private sealed class Candidate
    {
        public PlayerScoreResult Score;
        public int TurnsTaken;
        public int Seat;
    }

    public static List<RankedPlayerScore> Calculate(GameStateSnapshot state)
    {
        List<RankedPlayerScore> result = new List<RankedPlayerScore>();
        if (state == null || state.Players == null)
            return result;

        Dictionary<string, PlayerScoreResult> scores = ScoringRules.CalculateAll(state)
            .ToDictionary(score => score.PlayerId, score => score, StringComparer.Ordinal);

        List<Candidate> ordered = new List<Candidate>();
        for (int i = 0; i < state.Players.Count; i++)
        {
            PlayerStateSnapshot player = state.Players[i];
            if (player == null || !scores.TryGetValue(player.PlayerId, out PlayerScoreResult score))
                continue;
            ordered.Add(new Candidate { Score = score, TurnsTaken = CalculateTurnsTaken(state, i), Seat = i });
        }

        ordered = ordered
            .OrderByDescending(candidate => candidate.Score.VictoryPoints)
            .ThenBy(candidate => candidate.TurnsTaken)
            .ThenBy(candidate => candidate.Seat)
            .ToList();

        for (int i = 0; i < ordered.Count; i++)
        {
            Candidate current = ordered[i];
            int rank = i + 1;
            if (i > 0)
            {
                Candidate previous = ordered[i - 1];
                if (SameResult(current, previous))
                    rank = result[i - 1].Rank;
            }

            bool tied = (i > 0 && SameResult(current, ordered[i - 1])) ||
                        (i + 1 < ordered.Count && SameResult(current, ordered[i + 1]));
            result.Add(new RankedPlayerScore(rank, current.Score, current.TurnsTaken, tied));
        }

        return result;
    }


    private static int CalculateTurnsTaken(GameStateSnapshot state, int playerIndex)
    {
        if (state == null || state.Players == null || state.Players.Count == 0 || playerIndex < 0)
            return 0;

        int playerCount = state.Players.Count;
        int completedTurns = Math.Max(0, state.IsGameOver && state.EndedTurnNumber > 0 ? state.EndedTurnNumber : state.TurnNumber);
        if (completedTurns == 0)
            return 0;

        int currentIndex = state.Players.FindIndex(player => player != null && player.PlayerId == state.ActivePlayerId);
        if (currentIndex < 0)
            currentIndex = (completedTurns - 1) % playerCount;

        int startIndex = Mod(currentIndex - (completedTurns - 1), playerCount);
        int offset = Mod(playerIndex - startIndex, playerCount);
        int fullRounds = completedTurns / playerCount;
        int remainder = completedTurns % playerCount;
        return fullRounds + (offset < remainder ? 1 : 0);
    }

    private static int Mod(int value, int modulus)
    {
        int result = value % modulus;
        return result < 0 ? result + modulus : result;
    }

    private static bool SameResult(Candidate a, Candidate b)
    {
        return a != null && b != null &&
               a.Score.VictoryPoints == b.Score.VictoryPoints &&
               a.TurnsTaken == b.TurnsTaken;
    }
}
