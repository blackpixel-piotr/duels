# Duels — Implementation Brief (handoff to Claude Code)

Stack: **Blazor + C# (game logic) + Three.js (rendering)**. Existing: working combat, base UI (to be remade per the UI bible), cached-only save data, boss framework with a simple Maggot King.

This brief sequences the work and reserves the decisions Claude Code must not make alone. Detailed technical planning happens in-repo, one milestone at a time, against the actual code.

**Design docs (authoritative, in /design):** decisions · boss bible · UI bible · invocations · economy · items. Claude Code implements what they say; any deviation or ambiguity gets flagged back as a design question, never silently resolved.

---

## Working Agreement for Claude Code

1. One milestone per engagement. First produce a technical plan against the existing code; implement only after the plan is reviewed.
2. Never invent mechanics, numbers, or names — they come from the docs or they're flagged.
3. All content is **data, not code**: items, bosses, invocations, drop tables, and prices live in definition files (JSON or C# records) mirroring the doc tables 1:1, keyed by item_id/boss_id. `asset-map.md` stays current with every content addition.
4. Boss mechanics build on shared systems (telegraph framework, Perfect Dodge, punish windows, DoT, style rotation) — never as one-off code inside a single boss class. The Boss Bible's "Global Combat Grammar" section is a systems list, not flavor text.
5. Playable after every milestone. Each has a named playtest question; the milestone isn't done until that question can be answered on a phone.

---

## Visual Design Scope (this pass)

Functionality and layout complete; visual polish deferred to a later dedicated design pass (external tooling). Rules:

- **Mandatory now (gameplay, not aesthetics):** UI bible §2–3 functional specs — layouts, thumb zones, tap target sizes, the color doctrine (telegraph/style colors are game-readable information), telegraph white-flash, button states, input buffering, forecast widget.
- **Deferred (skin):** §1 visual language — inked frames, halftone textures, comic fonts, motion/smear animations, the diegetic hub scene (plain menu list for now), loot fanfare polish.
- **Architecture requirement:** all styling through design tokens (CSS variables + semantic classes), no scattered inline styles — the future reskin must be a token/stylesheet swap, never a restructure.
- Interim look: neutral, coherent, doctrine colors correct. Ugly is acceptable; unreadable is not.

## Reserved Decisions (human calls — decide before or during M0, not Claude Code's)

- **Tick authority & interop boundary.** All game logic ticks in C#; Three.js is a dumb renderer receiving one batched state snapshot per frame. No per-property JS interop calls inside the combat loop — interop chatter is the likely perf killer on this stack. (Recommended as stated; confirm.)
- **Persistence.** Recommend local-first (IndexedDB via a small persistence service) for launch; backend accounts arrive only when leaderboards do (M8). Confirm before M0 replaces the cache.
- **Distribution.** Browser/PWA vs store wrapper (e.g. Capacitor) — product decision, affects input/fullscreen/perf testing targets. Decide by M1's playtest.
- **Leaderboard integrity scope** (client-authoritative game = trust problem). Decide at M8, not before.

---

## Milestones

### M0 — Foundations (small, unglamorous, unblocks everything)
Formal tick scheduler (single authoritative 0.6s tick, input buffering per UI bible §2); definition-file pipeline for items/bosses/invocations; persistence service replacing the cache; asset manifest loader (Quaternius paths per items doc §7).
**Excludes:** any visible feature.
**Playtest question:** does the existing combat still run identically on the new tick core?

### M1 — Vertical Slice (the milestone that matters)
New HUD per UI bible §3 (prayers left / weapons right, flask belt, orbs, boss plate, telegraphs with white-flash final tick); RS3-style action bar + minimal Loadout Editor (§4); flask belt system; **Maggot King rebuilt to full Boss Bible choreography** (rotation script, eruptions, pools, swarms, Rot Burst with scorch-tile inversion, P2, Perfect Dodge, punish window); death→instant retry; basic victory/loot screen.
**Dev loadout (required for the playtest):** ingest only the needed item rows via the M0 definition pipeline and grant gear from a debug menu — a T1 preset (fresh-player experience) and a T2 preset (farmer experience), each with 2–3 bar weapons, T-appropriate armour, and Health + Prayer flasks. No shop, no bank, no drops — those stay in M2. The fight is tuned against at-tier gear; testing on anything else produces false signals.
**Excludes:** bank, shop, other bosses, invocations, minigame.
**Playtest question:** is fighting Maggot King on a phone *fun* — flicking, dodging, sipping under pressure? If no, stop and tune before building anything else.

### M2 — Progression Spine
Bank (UI bible §7), equipment paper-doll + Three.js preview (§8.1), gold shop, item tables ingested from the items doc, drop tables + kill gold (economy §3–5), regular duels as gold on-ramp.
**Playtest question:** does the first hour's purchase cadence match the economy targets?

### M3 — Combat Grammar as Systems + Bosses 2–4
Extract/complete shared systems (working agreement #4), then Hive Matron, Mirrorhide, Bloodtithe per the bible. Boss roster screen + pre-fight screen (without invocations panel).
**Playtest question:** does each boss feel like a different game? (That was the whole point of the roster.)

### M4 — Invocations & Raid Level
Invocation panel, RL math, loot/gold scaling, rare gating + bad-luck protection, mastery-gated unlocks, collection log (basic).
**Playtest question:** do players voluntarily raise RL for the rewards?

### M5 — Bosses 5–7
Gale Roc (flight events, delayed dive), The Unblinking (beam/LoS/pillar systems), Millstone Golem (boulders, player-built walls). These carry the heaviest new systems (aerial state, LoS occlusion, dynamic obstacles) — expect this to be the longest boss milestone.

### M6 — Grand Duelist + Onboarding
The mirror-match systems (visible boss prayer/spec bar, spec clash, route choice arenas), FTUE progressive disclosure (UI bible §12), settings + accessibility (§11).

### M7 — The Gauntlet (minigame)
Run engine (draw pool, reward picks, sip persistence, run-scoped bar), run-global invocations, Laurels + Emporium, run summary/death screens.
**Playtest question:** is a full run 10–15 minutes with instant re-queue appetite?

### M8 — Release Pass
Profile, leaderboards (+ backend + integrity decision), toasts, perf/pass on low-end phones (5.5" readability test per UI bible §13), store/PWA packaging, tint doctrine sweep across all assets.

---

## CLAUDE.md pointer (drop into repo root)

> Design authority lives in /design/*.md. Read the relevant doc before planning any feature. Content is data-driven per the implementation brief. Current milestone: M__. Do not implement ahead of the current milestone. Flag design ambiguities as questions; never resolve them silently.
