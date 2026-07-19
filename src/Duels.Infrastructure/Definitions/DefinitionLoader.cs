using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Duels.Infrastructure.Definitions;

/// <summary>Loads content definition files (items/npcs/invocations) embedded as
/// resources in this assembly. Definitions are data (working agreement #3) —
/// this is the one place that knows how they're packaged and parsed; a bad
/// file fails loudly at startup rather than silently loading partial content.</summary>
internal static class DefinitionLoader
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static T Load<T>(string fileName)
    {
        var assembly = typeof(DefinitionLoader).Assembly;
        var resourceName = assembly.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith("." + fileName, StringComparison.Ordinal));

        if (resourceName is null)
            throw new InvalidOperationException(
                $"Definition file '{fileName}' was not found as an embedded resource in {assembly.GetName().Name}.");

        using var stream = assembly.GetManifestResourceStream(resourceName)!;
        try
        {
            return JsonSerializer.Deserialize<T>(stream, Options)
                ?? throw new InvalidOperationException($"Definition file '{fileName}' deserialized to null.");
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"Definition file '{fileName}' is malformed: {ex.Message}", ex);
        }
    }
}
