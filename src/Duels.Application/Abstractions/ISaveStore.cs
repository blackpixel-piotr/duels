namespace Duels.Application.Abstractions;

/// <summary>String-keyed JSON blob storage for save data — game-agnostic on
/// purpose (no SaveData/schema knowledge here; that lives in Duels.Web where
/// SaveData is defined). Swap point: replace <c>IndexedDbSaveStore</c> with a
/// remote API client when backend accounts arrive (M8).</summary>
public interface ISaveStore
{
    Task<string?> LoadAsync(string key);
    Task SaveAsync(string key, string json);
    Task DeleteAsync(string key);
}
