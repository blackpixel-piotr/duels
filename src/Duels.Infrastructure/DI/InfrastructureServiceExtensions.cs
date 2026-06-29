using Duels.Application.Abstractions;
using Duels.Application.Commands;
using Duels.Application.Handlers;
using Duels.Application.Parsing;
using Duels.Application.Services;
using Duels.Domain.Interfaces;
using Duels.Domain.Services;
using Duels.Infrastructure.Messaging;
using Duels.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace Duels.Infrastructure.DI;

public static class InfrastructureServiceExtensions
{
    public static IServiceCollection AddDuelsInfrastructure(this IServiceCollection services)
    {
        // Domain services
        services.AddSingleton<IRandomProvider, Random.SystemRandomProvider>();
        services.AddSingleton<ICombatCalculator, CombatCalculator>();

        // Infrastructure — repositories (singleton so state lives for the browser session)
        services.AddSingleton<IGameStateRepository, InMemoryGameStateRepository>();
        services.AddSingleton<IPlayerRepository, InMemoryPlayerRepository>();
        services.AddSingleton<INpcRepository, InMemoryNpcRepository>();
        services.AddSingleton<IItemRepository, InMemoryItemRepository>();

        // Messaging — swap LocalCommandQueue for KafkaCommandQueue here only
        services.AddSingleton<IEventBus, InMemoryEventBus>();
        services.AddSingleton<ICommandDispatcher, LocalCommandQueue>();

        // Application services
        services.AddSingleton<ItemUnlockService>();
        services.AddSingleton<CommandParser>();

        // Command handlers
        services.AddSingleton<ICommandHandler<StartGameCommand>, StartGameHandler>();
        services.AddSingleton<ICommandHandler<StartDuelCommand>, StartDuelHandler>();
        services.AddSingleton<ICommandHandler<AttackCommand>, AttackHandler>();
        services.AddSingleton<ICommandHandler<EquipItemCommand>, EquipItemHandler>();
        services.AddSingleton<ICommandHandler<UnequipItemCommand>, UnequipItemHandler>();
        services.AddSingleton<ICommandHandler<InspectCommand>, InspectHandler>();
        services.AddSingleton<ICommandHandler<ListNpcsCommand>, ListNpcsHandler>();
        services.AddSingleton<ICommandHandler<HelpCommand>, HelpHandler>();

        return services;
    }
}
