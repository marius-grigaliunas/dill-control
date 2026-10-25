using System.Collections.Generic;
using Godot;
using DillControl.Core;

namespace DillControl.Mission;

/// <summary>
/// The player's run, across scene changes: the session's seed, the contracts
/// the ship's computer offers, and the one accepted. An autoload because it
/// has to outlive the hub to reach the contract level and outlive that to come
/// back (<c>docs/decisions/0021</c>).
///
/// State only. It raises no signals and is not a second event bus: a scene
/// reads it when it starts and writes it when the player decides.
/// </summary>
public partial class Session : Node
{
    public const string PoolsPath = "res://resources/mission/contract_pools_default.tres";

    public static Session Instance { get; private set; }

    /// <summary>What contracts are made of. Loaded once.</summary>
    public ContractPools Pools { get; private set; }

    /// <summary>The seed the offers are made from. New each session unless a test sets it.</summary>
    public ulong SessionSeed { get; private set; }

    /// <summary>The contracts on offer, made on first ask.</summary>
    public IReadOnlyList<ContractSpec> Offers => _offers ??= MakeOffers();

    /// <summary>The contract being flown, or null in the hub.</summary>
    public ContractSpec Accepted { get; private set; }

    private List<ContractSpec> _offers;

    /// <summary>One line of the contract's bill: what was spent, how many,
    /// at what price each (docs/decisions/0046). Owned mechs settle in the
    /// garage, hired ones at the contract's end; the screens come with the
    /// economy.</summary>
    public readonly record struct LedgerLine(string What, string For, int Units, float UnitPrice)
    {
        public float Total => Units * UnitPrice;
    }

    /// <summary>What the contract has cost so far: mech repairs, parts by pod.</summary>
    public IReadOnlyList<LedgerLine> Ledger => _ledger;

    private readonly List<LedgerLine> _ledger = new();

    /// <summary>Enters a cost in the ledger; units of the same thing for the
    /// same owner run on one line.</summary>
    public void Bill(string what, string forWhom, int units, float unitPrice)
    {
        if (units <= 0)
        {
            return;
        }
        for (int i = _ledger.Count - 1; i >= 0; i--)
        {
            LedgerLine line = _ledger[i];
            if (line.What == what && line.For == forWhom && Mathf.IsEqualApprox(line.UnitPrice, unitPrice))
            {
                _ledger[i] = line with { Units = line.Units + units };
                return;
            }
        }
        _ledger.Add(new LedgerLine(what, forWhom, units, unitPrice));
    }

    public override void _EnterTree()
    {
        Instance = this;
        Pools = GD.Load<ContractPools>(PoolsPath);
        if (Pools == null)
        {
            GD.PushError($"Session: no contract pools at {PoolsPath}.");
        }
        SessionSeed = (ulong)Time.GetUnixTimeFromSystem() ^ Time.GetTicksUsec();
    }

    public override void _ExitTree()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    /// <summary>The seed a fresh set of offers is made from, for a test that wants the same three twice.</summary>
    public void SetSessionSeed(ulong seed)
    {
        SessionSeed = seed;
        _offers = null;
    }

    /// <summary>The player took a contract. The contract level reads it when it loads.</summary>
    public void Accept(ContractSpec contract)
    {
        Accepted = contract;
        _ledger.Clear();
    }

    /// <summary>Back in the hub with nothing taken; a new set of offers.</summary>
    public void Return()
    {
        Accepted = null;
        SessionSeed = SeededRandom.Hash(SessionSeed, "next");
        _offers = null;
    }

    private List<ContractSpec> MakeOffers() =>
        Pools != null ? ContractGenerator.Offers(Pools, SessionSeed, Pools.OffersShown) : new List<ContractSpec>();
}
