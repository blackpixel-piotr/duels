namespace Duels.Domain.Entities;

public sealed class Quest
{
    public string Id { get; }
    public string Title { get; }
    public string Description { get; }
    public IReadOnlyList<QuestObjective> Objectives { get; }
    public QuestReward Reward { get; }

    public Quest(string id, string title, string description, IReadOnlyList<QuestObjective> objectives, QuestReward reward)
    {
        Id = id;
        Title = title;
        Description = description;
        Objectives = objectives;
        Reward = reward;
    }
}

public sealed record QuestObjective(string Id, string Description, int RequiredCount);
public sealed record QuestReward(int Gold, int AttackXp, int StrengthXp, int DefenceXp, IReadOnlyList<string> ItemIds);
