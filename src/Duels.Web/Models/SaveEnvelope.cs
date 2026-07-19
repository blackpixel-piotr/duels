namespace Duels.Web.Models;

/// <summary>Versioned wrapper around <see cref="SaveData"/> — the seam future
/// save-shape migrations key off. Bump <see cref="SchemaVersion"/> and add a
/// migration step in GameService when SaveData's shape changes again.</summary>
public sealed record SaveEnvelope(int SchemaVersion, SaveData Data);
