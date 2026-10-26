using Mule.Engine.Core;

namespace Mule.Engine.Rules;

/// <summary>calcAndDrawScore ($6CDB): money, land, goods, total, colony rating and ranking.</summary>
public static class Scoring
{
    /// <summary>
    /// Computes every player's score from the cached map counts (initVarsFromMaps), the colony
    /// total, the rating in round 12, and re-ranks ordersPlayer. The original works in Atari BCD
    /// floats; the values are whole numbers far below their 10-digit precision, so ints are exact.
    /// </summary>
    public static void CalcScore(GameState g, IRandomSource rng)
    {
        Array.Clear(g.TypesPlayersScore);
        g.TotalScore = 0;
        for (int p = 3; p >= 0; p--)
        {
            int money = g.PlayersCash[p];
            int land = g.PlayersNbLands[p] * 500;
            for (int outfit = 4; outfit >= 1; outfit--)
                land += g.ExpsPlayersNb[(outfit - 1) * 4 + p] * g.OutfitsPrice[outfit];
            int goods = g.PlayersNbExps[p] * g.GoodsPrice[Good.Mule];
            for (int good = 4; good >= 0; good--)
                goods += g.GoodsPlayersNb[good * 4 + p] * g.GoodsPrice[good];
            int total = money + land + goods;
            g.TypesPlayersScore[GameState.ScoreMoney * 4 + p] = money;
            g.TypesPlayersScore[GameState.ScoreLand * 4 + p] = land;
            g.TypesPlayersScore[GameState.ScoreGoods * 4 + p] = goods;
            g.TypesPlayersScore[GameState.ScoreTotal * 4 + p] = total;
            g.TotalScore += total;
        }
        if (g.NumRound == 12)
        {
            // (total - 10000) / 20000, converted with the OS float-to-integer routine (FPI), which
            // rounds to nearest: that makes it floor(total / 20000). Negative gives an FPI error -> 0.
            int r = g.TotalScore / 20000;
            g.SuccessScore = g.TotalScore < 10000 ? (byte)0 : (byte)Math.Min(r, 255);
        }
        Rank(g, rng);
    }

    /// <summary>
    /// Ranking ($6E48): repeatedly picks the best unranked total, scanning players 3 down to 0; an equal
    /// total takes the spot on a coin flip, so ties favour low player numbers (original quirk, kept).
    /// Fix (Marcus, 2026-10-25): the original started each search from a best score of 0, so a player
    /// worth exactly 0 could be left unranked and $FF written to the turn order. Here the first unranked
    /// player always becomes the candidate, which only differs from the original for a total of 0.
    /// </summary>
    public static void Rank(GameState g, IRandomSource rng)
    {
        Span<bool> sorted = stackalloc bool[4];
        for (int order = 0; order < 4; order++)
        {
            int best = -1, bestPlayer = -1;
            for (int p = 3; p >= 0; p--)
            {
                if (sorted[p]) continue;
                int t = g.TypesPlayersScore[GameState.ScoreTotal * 4 + p];
                if (bestPlayer < 0 && t == 0) { best = t; bestPlayer = p; continue; } // the fix
                if (t < best) continue;
                if (t == best && !rng.Negative()) continue;
                best = t; bestPlayer = p;
            }
            sorted[bestPlayer] = true;
            g.OrdersPlayer[order] = (byte)bestPlayer;
        }
    }

    /// <summary>The colony rating message index (addMessageSuccess $8D43 caps it at 6).</summary>
    public static int RatingIndex(GameState g) => Math.Min((int)g.SuccessScore, 6);
}
