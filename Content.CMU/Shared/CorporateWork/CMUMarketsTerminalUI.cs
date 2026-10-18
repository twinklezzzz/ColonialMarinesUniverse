using Robust.Shared.Serialization;

namespace Content.Shared.CMU14.CorporateWork;

[Serializable, NetSerializable]
public enum CMUMarketsTerminalUi : byte
{
    Key,
}

[Serializable, NetSerializable]
public readonly record struct CMUMarketQuote(string Id, string Name, int Price, int PreviousPrice, int Held, int AverageCost);

[Serializable, NetSerializable]
public readonly record struct CMUMarketNews(TimeSpan At, string Headline);

[Serializable, NetSerializable]
public sealed class CMUMarketsTerminalBuiState(
    List<CMUMarketQuote> quotes,
    List<CMUMarketNews> news,
    int corporateBudget,
    int holdingsValue,
    int holdingsCap,
    float feePercent,
    float dividendPercent,
    TimeSpan nextTickAt) : BoundUserInterfaceState
{
    public readonly List<CMUMarketQuote> Quotes = quotes;
    public readonly List<CMUMarketNews> News = news;
    public readonly int CorporateBudget = corporateBudget;
    public readonly int HoldingsValue = holdingsValue;
    public readonly int HoldingsCap = holdingsCap;
    public readonly float FeePercent = feePercent;
    public readonly float DividendPercent = dividendPercent;
    public readonly TimeSpan NextTickAt = nextTickAt;
}

[Serializable, NetSerializable]
public sealed class CMUMarketsTradeBuiMsg(string commodity, int units, bool buy) : BoundUserInterfaceMessage
{
    public readonly string Commodity = commodity;
    public readonly int Units = units;
    public readonly bool Buy = buy;
}
