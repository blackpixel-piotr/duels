using Duels.Application.Abstractions;
using Duels.Domain.Entities;
using Duels.Infrastructure.Definitions;

namespace Duels.Infrastructure.Persistence;

/// <summary>Loads invocations.json — empty until M4 populates it (schema +
/// pipeline only for M0, per the implementation brief).</summary>
public sealed class DefinitionInvocationRepository : IInvocationRepository
{
    private readonly Dictionary<string, InvocationDefinition> _invocations;

    public DefinitionInvocationRepository() : this(DefinitionLoader.Load<List<InvocationDefinition>>("invocations.json"))
    {
    }

    internal DefinitionInvocationRepository(List<InvocationDefinition> definitions)
    {
        _invocations = new Dictionary<string, InvocationDefinition>();
        foreach (var inv in definitions)
        {
            if (!_invocations.TryAdd(inv.Id, inv))
                throw new InvalidOperationException($"invocations.json: duplicate invocation id '{inv.Id}'.");
        }
    }

    public InvocationDefinition? Get(string invocationId) => _invocations.GetValueOrDefault(invocationId);
    public IReadOnlyList<InvocationDefinition> GetAll() => _invocations.Values.ToList();
}
