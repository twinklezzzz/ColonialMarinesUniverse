using Content.Shared.CMU14.CorporateWork;
using Robust.Client.UserInterface;

namespace Content.Client.CMU14.CorporateWork;

public sealed class CMUMarketsTerminalBui(EntityUid owner, Enum uiKey) : BoundUserInterface(owner, uiKey)
{
    private CMUMarketsTerminalWindow? _window;

    protected override void Open()
    {
        base.Open();
        _window = this.CreateWindow<CMUMarketsTerminalWindow>();
        _window.OnTrade += (commodity, units, buy) => SendMessage(new CMUMarketsTradeBuiMsg(commodity, units, buy));
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        if (_window == null || state is not CMUMarketsTerminalBuiState s)
            return;

        _window.UpdateState(s);
    }
}
