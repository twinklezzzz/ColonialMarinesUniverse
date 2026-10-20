using System.Linq;
using Content.Shared.CMU14.CorporateRelations;

namespace Content.Server.CMU14.CorporateRelations;

public sealed partial class CMUCorporateRelationsSystem
{
    public const int MaxLoan = 10000;
    public const int MaxInterestPercent = 100;
    public const int Installments = 3;

    /// <summary>
    ///     What the colony already owes WY when the round starts, so it can't play dirty straight away.
    /// </summary>
    public const int StartingDebt = 1500;

    private readonly Dictionary<string, Loan> _loans = new();

    private sealed class Loan
    {
        public required string Borrower;
        public CMULoanStage Stage;
        public int Amount;
        public int InterestPercent;
        public float Owed;
        public int InstallmentsLeft;
        public bool StartingDebt;
    }

    private void InitializeLoans()
    {
        SubscribeLocalEvent<CMUContractsConsoleComponent, CMUContractsRequestLoanBuiMsg>(OnRequestLoan);
        SubscribeLocalEvent<CMUContractsConsoleComponent, CMUContractsCounterLoanBuiMsg>(OnCounterLoan);
        SubscribeLocalEvent<CMUContractsConsoleComponent, CMUContractsAnswerLoanBuiMsg>(OnAnswerLoan);
        SubscribeLocalEvent<CMUContractsConsoleComponent, CMUContractsCallInLoanBuiMsg>(OnCallInLoan);
    }

    private void StartLoans()
    {
        _loans[ColonyBorrower] = new Loan
        {
            Borrower = ColonyBorrower,
            Stage = CMULoanStage.Active,
            Amount = StartingDebt,
            Owed = StartingDebt,
            InstallmentsLeft = Installments,
            StartingDebt = true,
        };
    }

    /// <summary>
    ///     What <paramref name="borrower"/> still owes WY, or zero.
    /// </summary>
    public float GetOwed(string borrower)
    {
        return _loans.TryGetValue(borrower, out var loan) && loan.Stage == CMULoanStage.Active ? loan.Owed : 0f;
    }

    private CMULoanInfo ToInfo(Loan loan)
    {
        return new CMULoanInfo(
            loan.Borrower,
            BorrowerName(loan.Borrower),
            loan.Stage,
            loan.Amount,
            loan.InterestPercent,
            (int) MathF.Ceiling(loan.Owed),
            loan.InstallmentsLeft,
            loan.StartingDebt);
    }

    private void CollectInstallments()
    {
        foreach (var loan in _loans.Values.ToList())
        {
            if (loan.Stage != CMULoanStage.Active)
                continue;

            // Instalments are forced: if the borrower can't cover one, it's taken anyway, into the red.
            // Otherwise a borrower could dodge every payment by keeping its budget just short.
            var installment = MathF.Min(loan.Owed, MathF.Ceiling(loan.Owed / Math.Max(1, loan.InstallmentsLeft)));
            if (GetBorrowerBudget(loan.Borrower) < installment)
                Announce("cmu-contracts-announce-loan-forced", ("borrower", BorrowerName(loan.Borrower)), ("amount", (int) installment));

            Repay(loan, installment);
            loan.InstallmentsLeft = Math.Max(1, loan.InstallmentsLeft - 1);
        }
    }

    private void Repay(Loan loan, float amount)
    {
        AdjustBorrowerBudget(loan.Borrower, -amount);
        _corporate.AddToCorporateBudget(amount);
        loan.Owed -= amount;

        if (loan.Owed > 0.5f)
            return;

        _loans.Remove(loan.Borrower);
        Announce("cmu-contracts-announce-loan-repaid", ("borrower", BorrowerName(loan.Borrower)));
    }

    private void OnRequestLoan(Entity<CMUContractsConsoleComponent> ent, ref CMUContractsRequestLoanBuiMsg args)
    {
        if (GetBorrower(ent) is not { } borrower)
            return;

        if (_withdrawn)
        {
            Refuse(ent, args.Actor, "cmu-contracts-refuse-withdrawn");
            return;
        }

        if (_loans.ContainsKey(borrower))
        {
            Refuse(ent, args.Actor, "cmu-contracts-refuse-loan-exists");
            return;
        }

        var amount = Math.Clamp(args.Amount, 1, MaxLoan);
        _loans[borrower] = new Loan
        {
            Borrower = borrower,
            Stage = CMULoanStage.AwaitingCorporate,
            Amount = amount,
        };

        Announce("cmu-contracts-announce-loan-requested", ("borrower", BorrowerName(borrower)), ("amount", amount));
        UpdateAllUi();
    }

    private void OnCounterLoan(Entity<CMUContractsConsoleComponent> ent, ref CMUContractsCounterLoanBuiMsg args)
    {
        if (!TryGetNegotiation(ent, args.Borrower, out var loan, out var corporate))
            return;

        loan.Amount = Math.Clamp(args.Amount, 1, MaxLoan);
        loan.InterestPercent = Math.Clamp(args.InterestPercent, 0, MaxInterestPercent);
        loan.Stage = corporate ? CMULoanStage.AwaitingBorrower : CMULoanStage.AwaitingCorporate;
        UpdateAllUi();
    }

    private void OnAnswerLoan(Entity<CMUContractsConsoleComponent> ent, ref CMUContractsAnswerLoanBuiMsg args)
    {
        if (!TryGetNegotiation(ent, args.Borrower, out var loan, out _))
            return;

        if (!args.Accept)
        {
            _loans.Remove(loan.Borrower);
            Announce("cmu-contracts-announce-loan-declined", ("borrower", BorrowerName(loan.Borrower)));
            UpdateAllUi();
            return;
        }

        if (_corporate.GetCorporateBudget() < loan.Amount)
        {
            Refuse(ent, args.Actor, "cmu-contracts-refuse-corporate-funds");
            return;
        }

        _corporate.AddToCorporateBudget(-loan.Amount);
        AdjustBorrowerBudget(loan.Borrower, loan.Amount);
        loan.Stage = CMULoanStage.Active;
        loan.Owed = MathF.Ceiling(loan.Amount * (1 + loan.InterestPercent / 100f));
        loan.InstallmentsLeft = Installments;

        Announce("cmu-contracts-announce-loan-agreed",
            ("borrower", BorrowerName(loan.Borrower)),
            ("amount", loan.Amount),
            ("interest", loan.InterestPercent));
        UpdateAllUi();
    }

    /// <summary>
    ///     Finds a loan still being negotiated that it is this console's turn to answer.
    /// </summary>
    private bool TryGetNegotiation(Entity<CMUContractsConsoleComponent> ent, string requested, out Loan loan, out bool corporate)
    {
        corporate = ent.Comp.Side == CMUContractsSide.Corporate;
        var borrower = corporate ? requested : GetBorrower(ent);
        loan = default!;

        if (borrower == null || !_loans.TryGetValue(borrower, out var found) || found.Stage == CMULoanStage.Active)
            return false;

        // A side can only answer when the other side made the last move.
        var turn = corporate ? CMULoanStage.AwaitingCorporate : CMULoanStage.AwaitingBorrower;
        if (found.Stage != turn)
            return false;

        loan = found;
        return true;
    }

    private void OnCallInLoan(Entity<CMUContractsConsoleComponent> ent, ref CMUContractsCallInLoanBuiMsg args)
    {
        if (ent.Comp.Side != CMUContractsSide.Corporate ||
            !_loans.TryGetValue(args.Borrower, out var loan) ||
            loan.Stage != CMULoanStage.Active)
        {
            return;
        }

        // Called in means collected now, in full, whether or not the borrower can cover it.
        var owed = (int) MathF.Ceiling(loan.Owed);
        var key = GetBorrowerBudget(loan.Borrower) >= loan.Owed ? "cmu-contracts-announce-loan-called-paid" : "cmu-contracts-announce-loan-called-short";
        Announce(key, ("borrower", BorrowerName(loan.Borrower)), ("owed", owed));
        Repay(loan, loan.Owed);

        UpdateAllUi();
    }

    /// <summary>
    ///     Collects every outstanding loan in full on the spot, even from borrowers who can't cover
    ///     it: their budget goes negative. Loans still being negotiated are dropped.
    /// </summary>
    private void CollectAllLoans()
    {
        foreach (var loan in _loans.Values.ToList())
        {
            if (loan.Stage != CMULoanStage.Active)
            {
                _loans.Remove(loan.Borrower);
                continue;
            }

            Repay(loan, loan.Owed);
        }
    }
}
