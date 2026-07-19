using Duels.Application.Abstractions;
using Microsoft.JSInterop;

namespace Duels.Infrastructure.Persistence;

/// <summary>ISaveStore backed by the browser's IndexedDB (persistence.js) —
/// replaces the old direct localStorage calls. Local-first per the
/// implementation brief's reserved persistence decision; a remote client
/// swaps in here, unchanged, when backend accounts arrive (M8).</summary>
public sealed class IndexedDbSaveStore : ISaveStore
{
    private readonly IJSRuntime _js;

    public IndexedDbSaveStore(IJSRuntime js) => _js = js;

    public async Task<string?> LoadAsync(string key) =>
        await _js.InvokeAsync<string?>("idbGet", key);

    public async Task SaveAsync(string key, string json) =>
        await _js.InvokeVoidAsync("idbSet", key, json);

    public async Task DeleteAsync(string key) =>
        await _js.InvokeVoidAsync("idbDelete", key);
}
