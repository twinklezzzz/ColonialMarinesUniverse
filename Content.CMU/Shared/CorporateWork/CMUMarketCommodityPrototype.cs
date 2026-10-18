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

    [DataField(required: true)]
    public int BasePrice;

    /// <summary>
    ///     Largest random move per market tick, as a fraction of the price.
    /// </summary>
    [DataField]
    public float Volatility = 0.05f;
}
