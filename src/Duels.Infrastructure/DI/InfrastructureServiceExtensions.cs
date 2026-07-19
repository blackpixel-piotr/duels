using Duels.Application.Abstractions;
using Duels.Application.Commands;
using Duels.Application.Handlers;
using Duels.Application.Services;
using Duels.Domain.Interfaces;
using Duels.Domain.Services;
using Duels.Infrastructure.Messaging;
using Duels.Infrastructure.Persistence;
using Duels.Infrastructure.Timing;
using Microsoft.Extensions.DependencyInjection;

namespace Duels.Infrastructure.DI;

public static class InfrastructureServiceExtensions
{
    public static IServiceCollection AddDuelsInfrastructure(this IServiceCollection services)
    {
        // Domain services
        services.AddSingleton<IRandomProvider, Random.SystemRandomProvider>();
        services.AddSingleton<IDamageModel, DamageModel>();

        // Infrastructure — repositories (singleton so state lives for the browser session)
        services.AddSingleton<IGameStateRepository, InMemoryGameStateRepository>();
        services.AddSingleton<IPlayerRepository, InMemoryPlayerRepository>();
        services.AddSingleton<IItemRepository, DefinitionItemRepository>();
        services.AddSingleton<INpcRepository, DefinitionNpcRepository>();
        services.AddSingleton<IInvocationRepository, DefinitionInvocationRepository>();
        services.AddSingleton<ISaveStore, IndexedDbSaveStore>();

        // Messaging — swap LocalCommandQueue for KafkaCommandQueue here only
        services.AddSingleton<IEventBus, InMemoryEventBus>();
        services.AddSingleton<ICommandDispatcher, LocalCommandQueue>();

        // Application services
        services.AddSingleton<ITickSource, TickScheduler>();
        services.AddSingleton<GameTickService>();

        // Command handlers
        services.AddSingleton<ICommandHandler<StartGameCommand>, StartGameHandler>();
        services.AddSingleton<ICommandHandler<StartDuelCommand>, StartDuelHandler>();
        services.AddSingleton<ICommandHandler<AttackCommand>, AttackHandler>();
        services.AddSingleton<ICommandHandler<EquipItemCommand>, EquipItemHandler>();
        services.AddSingleton<ICommandHandler<UnequipItemCommand>, UnequipItemHandler>();
        services.AddSingleton<ICommandHandler<WeaponShortcutCommand>, WeaponShortcutHandler>();
        services.AddSingleton<ICommandHandler<PrayerCommand>, PrayerHandler>();
        services.AddSingleton<ICommandHandler<SetStyleCommand>, SetStyleHandler>();
        services.AddSingleton<ICommandHandler<MoveToCommand>, MoveToHandler>();
        services.AddSingleton<ICommandHandler<EngageCommand>, EngageHandler>();
        services.AddSingleton<ICommandHandler<FreezeEnemyCommand>, FreezeEnemyHandler>();
        services.AddSingleton<ICommandHandler<SipFlaskCommand>, SipFlaskHandler>();
        services.AddSingleton<ICommandHandler<SetTargetCommand>, SetTargetHandler>();
        services.AddSingleton<ICommandHandler<BindWeaponSlotCommand>, BindWeaponSlotHandler>();
        services.AddSingleton<ICommandHandler<BindFlaskSlotCommand>, BindFlaskSlotHandler>();
        services.AddSingleton<ICommandHandler<GrantDevLoadoutCommand>, GrantDevLoadoutHandler>();

        return services;
    }
}
