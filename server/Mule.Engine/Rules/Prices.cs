using Mule.Engine.Core;

namespace Mule.Engine.Rules;

/// <summary>Price calculation before the goods auctions, and mule building ($3134-$33DD).</summary>
public static class Prices
{
    /// <summary>calcGoodsPriceFromNow ($330C): food, energy, smithore and crystite prices.</summary>
    public static void CalcGoodsPriceFromNow(GameState g, IRandomSource rng)
    {
        CalcFoodPriceFromNow(g);
        CalcEnergyPriceFromNow(g);
        g.GoodsFuturNb[2] = (byte)((g.GoodsStoreNb[Good.Smithore] >> 1) + g.GoodsStoreNb[Good.Mule]);
        byte req = CalcMuleReq(g, rng);
        CalcSmithoreAndCrystitePriceFromWhen(g, rng, req);
    }

    /// <summary>calcFoodPriceFromNow ($3134).</summary>
    public static void CalcFoodPriceFromNow(GameState g)
    {
        g.GoodsFuturNb[0] = g.GoodsStoreNb[Good.Food];
        CalcFoodPriceFromWhen(g);
    }

    /// <summary>calcFoodPriceFromWhen ($313A).</summary>
    public static void CalcFoodPriceFromWhen(GameState g)
    {
        for (int p = 3; p >= 0; p--)
        {
            int held = g.GoodsPlayersNb[Good.Food * 4 + p] - g.FoodReq;
            byte a = held < 0 ? (byte)0 : (byte)held;
            a >>= 1;
            int sum = a + g.GoodsPlayersProd[Good.Food * 4 + p];
            // The original adds the running total with the carry of the first addition.
            int carry = sum > 255 ? 1 : 0;
            sum = (byte)sum + g.GoodsFuturNb[0] + carry;
            g.GoodsFuturNb[0] = sum > 255 ? (byte)255 : (byte)sum;
        }
        byte futurReq = g.FoodReq;
        if ((g.NumRound & 3) == 0) futurReq++;
        g.GoodsFuturReq[0] = (byte)(futurReq << 2);
        CalcGoodPriceSupplyDemand(g, 0);
    }

    /// <summary>calcEnergyPriceFromNow ($3170).</summary>
    public static void CalcEnergyPriceFromNow(GameState g)
    {
        g.GoodsFuturNb[1] = g.GoodsStoreNb[Good.Energy];
        CalcEnergyPriceFromWhen(g);
    }

    /// <summary>calcEnergyPriceFromWhen ($3176). Also sets playersGoodReq to each player's energy need.</summary>
    public static void CalcEnergyPriceFromWhen(GameState g)
    {
        g.GoodsFuturReq[1] = 0;
        for (int p = 3; p >= 0; p--)
        {
            byte need = Production.CalcEnergyReq(g, p);
            g.PlayersGoodReq[p] = need;
            g.GoodsFuturReq[1] = (byte)(need + g.GoodsFuturReq[1]);
            int held = g.GoodsPlayersNb[Good.Energy * 4 + p] - need;
            byte x = held < 0 ? (byte)0 : (byte)held;
            byte a = (byte)(x - (x >> 2));
            int sum = a + g.GoodsPlayersProd[Good.Energy * 4 + p];
            int carry = sum > 255 ? 1 : 0;
            sum = (byte)sum + g.GoodsFuturNb[1] + carry;
            g.GoodsFuturNb[1] = sum > 255 ? (byte)255 : (byte)sum;
        }
        g.GoodsFuturReq[1] = (byte)(g.GoodsFuturReq[1] + 4);
        CalcGoodPriceSupplyDemand(g, 2);
    }

    /// <summary>
    /// calcGoodPriceSupplyDemand ($31C1) + limitGoodPrice ($3249). <paramref name="good2"/> is 0 food,
    /// 2 energy, 4 smithore (the original's word offset). Ratio r = 8*demand/supply kept in one byte.
    /// </summary>
    public static void CalcGoodPriceSupplyDemand(GameState g, int good2)
    {
        int y = good2 >> 1;
        int num = g.GoodsFuturReq[y] << 3;  // 11-bit
        int div = g.GoodsFuturNb[y] == 0 ? 1 : g.GoodsFuturNb[y];
        int q = num / div;
        byte ratio = (byte)q;
        if (good2 == 4)
        {
            if (ratio >= 24) ratio = 24;
            if (ratio < 2) ratio = 2;
        }
        int gi = good2 >> 1;
        ushort price = g.GoodsPrice[gi];
        ushort prod = (ushort)(price * ratio);
        ushort t = (ushort)(prod >> 4);
        ushort term = (ushort)(t + (t >> 1));
        price = (ushort)((price >> 2) + term);
        g.GoodsPrice[gi] = price;
        LimitGoodPrice(g, good2);
    }

    /// <summary>limitGoodPrice ($3249): cap at 250, then raise to the minimum in goodsPriceLimit.</summary>
    public static void LimitGoodPrice(GameState g, int good2)
    {
        int gi = good2 >> 1;
        if (g.GoodsPrice[gi] >= 250) g.GoodsPrice[gi] = 250;
        // The minimum compares the low byte only (the price is now under 256).
        if ((byte)g.GoodsPrice[gi] < (byte)g.GoodsPriceLimit[gi])
            g.GoodsPrice[gi] = (ushort)((g.GoodsPrice[gi] & 0xFF00) | (byte)g.GoodsPriceLimit[gi]);
    }

    /// <summary>calcSmithoreAndCrystitePriceFromWhen ($3320).</summary>
    public static void CalcSmithoreAndCrystitePriceFromWhen(GameState g, IRandomSource rng, byte muleReq)
    {
        g.GoodsFuturReq[2] = muleReq;
        g.GoodsPriceLimit[2] = 50;
        CalcGoodPriceSupplyDemand(g, 4);

        byte y = g.Level;
        if (y == 1) y++;
        byte d = rng.Binomial(0, y);
        byte a = 0;
        for (int i = 0; i < 6; i++) a = (byte)(a + d);
        // The original adds d six times to an accumulator that already holds d: 7 * d.
        a = (byte)(a + d);
        byte price = (byte)g.GoodsPrice[2];
        if ((a & 0x80) != 0)
        {
            int s = a + price;
            if (s > 255) g.GoodsPrice[2] = (ushort)((g.GoodsPrice[2] & 0xFF00) | (byte)s);
            else
            {
                // Underflow (unreachable in play): the original falls through and adds the price again.
                int s2 = (byte)s + price;
                byte r = s2 > 255 ? (byte)255 : (byte)s2;
                g.GoodsPrice[2] = (ushort)((g.GoodsPrice[2] & 0xFF00) | r);
            }
        }
        else
        {
            int s = a + price;
            byte r = s > 255 ? (byte)255 : (byte)s;
            g.GoodsPrice[2] = (ushort)((g.GoodsPrice[2] & 0xFF00) | r);
        }
        g.GoodsPriceLimit[2] = 20;
        LimitGoodPrice(g, 4);

        g.GoodsPrice[3] = (ushort)(rng.Below(100) + 50);
    }

    /// <summary>calcMuleReq ($3379): min(free plots, 4) + owned plots without a mule, capped at 8.
    /// Calls randomFreePlot, which reads one random number.</summary>
    public static byte CalcMuleReq(GameState g, IRandomSource rng)
    {
        RandomFreePlot(g, rng, out int free);
        int x = free >= 4 ? 4 : free;
        for (int y = 44; y >= 0; y--)
            if ((g.PlotsPlayer[y] & 0x80) == 0 && g.PlotsExp[y] == Exp.None) x++;
        return x >= 8 ? (byte)8 : (byte)x;
    }

    /// <summary>randomFreePlot ($4C6B): a uniform random unowned plot other than the store, or $FF.
    /// Fills plotsTemp with the free plot list.</summary>
    public static byte RandomFreePlot(GameState g, IRandomSource rng, out int free)
    {
        free = 0;
        for (int y = 0; y < 45; y++)
            if ((g.PlotsPlayer[y] & 0x80) != 0 && y != GameState.StorePlot)
                g.PlotsTemp[free++] = (byte)y;
        if (free == 0) return 0xFF;
        return g.PlotsTemp[rng.Below((byte)free)];
    }

    /// <summary>buildAndCalcPriceMule ($339A): build min(14 - stock, smithore / 2) mules, then
    /// mule price = floor(smithore price / 5) * 10.</summary>
    public static void BuildAndCalcPriceMule(GameState g)
    {
        int buildable = g.GoodsStoreNb[Good.Smithore] >> 1;
        if (buildable != 0)
        {
            int missing = (byte)(14 - g.GoodsStoreNb[Good.Mule]);
            if (missing != 0)
            {
                int n = missing < buildable ? missing : buildable;
                g.GoodsStoreNb[Good.Smithore] = (byte)(g.GoodsStoreNb[Good.Smithore] - 2 * n);
                g.GoodsStoreNb[Good.Mule] = (byte)(g.GoodsStoreNb[Good.Mule] + n);
            }
        }
        CalcMulePrice(g);
    }

    /// <summary>calcMulePrice ($33C7).</summary>
    public static void CalcMulePrice(GameState g) => g.MulePrice = (ushort)(g.GoodsPrice[2] / 5 * 10);
}
