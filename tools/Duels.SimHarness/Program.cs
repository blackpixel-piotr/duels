using Duels.SimHarness;

// ── Duels headless combat sim harness ("the dev arena") ─────────────────────
//
//   dotnet run --project tools/Duels.SimHarness -- [bossId] [brain] [--ticks N] [--seed S]
//
//   bossId : maggot_king | hive_matron | mirrorhide | bloodtithe   (default hive_matron)
//   brain  : idle | naive | perfect                                (default perfect)
//   --ticks: max ticks to simulate before giving up                (default 400)
//   --seed : omit for a deterministic always-hit run; pass an int for a seeded run
//
// Prints a tick-by-tick trace of the whole fight and a one-line verdict — no
// browser, no Blazor, no getting-to-the-boss. Runs in milliseconds.

string boss = "hive_matron";
string brainName = "perfect";
int maxTicks = 400;
int? seed = null;

var positional = new List<string>();
for (int i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--ticks": maxTicks = int.Parse(args[++i]); break;
        case "--seed": seed = int.Parse(args[++i]); break;
        default: positional.Add(args[i]); break;
    }
}
if (positional.Count > 0) boss = positional[0];
if (positional.Count > 1) brainName = positional[1];

IPlayerBrain brain = brainName switch
{
    "idle" => new IdleBrain(),
    "naive" => new NaiveRangedBrain(),
    "melee" => new MeleeWeaveBrain(),
    "perfect" => new PerfectHiveMatronBrain(),
    _ => throw new ArgumentException($"unknown brain '{brainName}' (idle|naive|melee|perfect)"),
};

var rng = seed is { } s ? (Duels.Domain.Interfaces.IRandomProvider)new SeededRandom(s) : new AlwaysHitRandom();

var world = new SimWorld(boss, rng);
var trace = new TraceRecorder(world.State);

Console.WriteLine($"═══ {boss}  vs  {brain.Name}  " +
    $"(rng={(seed is { } sv ? $"seed {sv}" : "always-hit")}, boss {world.Npc.MaxHp} HP, arena {world.State.ArenaRadius * 2 + 1}²) ═══\n");

var ctx = new SimContext(world.State);
int bossMaxHp = world.Npc.MaxHp;
int finalBossHp = bossMaxHp;
for (int tick = 0; tick < maxTicks && !world.FightOver; tick++)
{
    brain.Decide(ctx);
    await world.TickAsync();
    trace.Capture();
    finalBossHp = world.State.ActiveNpc?.CurrentHp ?? 0; // nulled once the duel ends
}

Console.WriteLine(trace.Render());

Console.WriteLine();
Console.WriteLine($"── {trace.Outcome} after {trace.Ticks} ticks ({trace.Ticks * 0.6:0.0}s) ──");
Console.WriteLine($"   player HP: lowest {trace.MinPlayerHp}/100");
Console.WriteLine($"   boss HP:   {finalBossHp}/{bossMaxHp}");
