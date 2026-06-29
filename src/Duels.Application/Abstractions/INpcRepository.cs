using Duels.Domain.Entities;

namespace Duels.Application.Abstractions;

public interface INpcRepository
{
    NpcTemplate? GetTemplate(string npcId);
    IReadOnlyList<NpcTemplate> GetAll();
}
