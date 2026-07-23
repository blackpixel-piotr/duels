using Duels.Application.Abstractions;

namespace Duels.Application.Commands;

/// <summary>Backlog resolution batch 1 §4 (cold start): <paramref name="ChosenStyle"/>
/// is the FTUE "choose your style" pick — Melee/Ranged/Magic — granting the
/// matching free T1 weapon, equipped and bound to bar slot 0.</summary>
public sealed record StartGameCommand(string PlayerId, string PlayerName, string ChosenStyle) : IGameCommand;
