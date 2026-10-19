using Robust.Shared.Prototypes;

namespace Content.Shared.CMU14.CorporateWork;

/// <summary>
///     Something traded on the corporate markets terminal.
/// </summary>
[Prototype("cmuMarketCommodity")]
public sealed partial class CMUMarketCommodityPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField(required: true)]
    public LocId Name;

    /// <summary>
    ///     Ticker shown on the board, e.g. PHR.
    /// </summary>
    [DataField(required: true)]
    public string Symbol = string.Empty;

    [DataField(required: true)]
    public int BasePrice;

    /// <summary>
    ///     Largest random move per market tick, as a fraction of the price.
    /// </summary>
    [DataField]
    public float Volatility = 0.05f;

    /// <summary>
    ///     How much money it takes to move the price. A trade worth this much moves it 100%,
    ///     before the per-trade cap. Lower means a thinner, jumpier market.
    /// </summary>
    [DataField]
    public float Liquidity = 40000f;
}
