using Duels.Application.Abstractions;

namespace Duels.Application.Commands;

public sealed record PrayerCommand(string PlayerId, string PrayerName) : IGameCommand;
