using Duels.Domain.Entities;

namespace Duels.Application.Abstractions;

public interface IInvocationRepository
{
    InvocationDefinition? Get(string invocationId);
    IReadOnlyList<InvocationDefinition> GetAll();
}
