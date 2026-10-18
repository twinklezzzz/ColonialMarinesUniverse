using Content.Server.CMU14.Ambassador;
using Content.Server.CMU14.CorporateRelations;
using Content.Server.Containers;
using Content.Shared.CMU14.ColonyEconomy;
using Content.Shared.Containers;
using Content.Shared.Stacks;
using Content.Shared.Tag;
using Robust.Shared.Containers;

namespace Content.Server.CMU14.ColonyEconomy;

public sealed partial class SubmissionStorageSystem : EntitySystem
{
    [Dependency] private ColonyBudgetSystem _colonyBudget = default!;
    [Dependency] private AmbassadorConsoleSystem _ambassador = default!;
    [Dependency] private CorporateConsoleSystem _corporateConsole = default!;
    [Dependency] private CMUCorporateRelationsSystem _relations = default!;
 
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<SubmissionStorageComponent, EntInsertedIntoContainerMessage>(OnEntityInserted);
    }



    private void OnEntityInserted(EntityUid uid,
        SubmissionStorageComponent storage,
        EntInsertedIntoContainerMessage args)
    {
        if (!TryComp(uid, out SubmissionStorageComponent? submission))
            return;

        if (!TryComp<TagComponent>(args.Entity, out var tags))
            return;
        if (submission.Rewards is null)
            return;

        float sum = 0f;
        int num = 0;
        foreach (var tag in tags.Tags)
        {
            if (submission.Rewards.TryGetValue(tag, out var val))
            {
                sum += val;
                num++;
            }
        }
        // can never be too careful
        if (num == 0)
            num = 1;

        // e.g. $10 + $15 != $25 instead it equals $12.5
        float amount = sum / num;

        var mult = _ambassador.GetSubmissionMultiplier() * _relations.ColonyPayoutMultiplier;
        // A withdrawn WY no longer takes its cut.
        var tariff = _relations.Withdrawn ? 0f : _corporateConsole.GetTariff();
        //var amount = submission.Rewards.TryGetValue()

        float reward;
        TryComp<StackComponent>(args.Entity, out var stack);
        if (stack != null)
            reward = amount * stack.Count * mult;
        else
            reward = amount * mult;

        _relations.OnDelivery(args.Entity, stack?.Count ?? 1, reward);
        PredictedQueueDel(args.Entity);

        // Split: tariff % goes to corporate budget, remainder to colony budget
        var tariffAmount = reward * tariff;
        var colonyAmount = reward - tariffAmount;

        _colonyBudget.AddToBudget(colonyAmount);
        if (tariffAmount > 0f)
            _corporateConsole.AddToCorporateBudget(tariffAmount);
    }
}
