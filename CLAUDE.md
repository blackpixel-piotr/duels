# Duels — CLAUDE.md

Mobile-first (landscape), real-time 1v1 boss-fighting game. Stack: Blazor + C# (game logic) + Three.js (rendering).

## Design authority
- All design lives in /design/*.md — read the implementation brief (/design/duels-implementation-brief.md) before planning any feature, and the relevant design doc before implementing it.
- **M1 and M2 (Progression Spine) are complete.** M2 shipped item table completion (T1-T4 shop weapons/armour, Maggot King's rare), Bank, Gold Shop, the equipment stat sheet + compare flow, and the Three.js equipment preview (see design/plans/m2-findings.md for what's still deferred: Rotfang, Maggot King's Common/Uncommon loot rows, T3/T4 special-attack mechanics, shop buyback). M3+ (other bosses, invocations, minigame) has not been started or authorized — do not implement ahead of the current milestone.
- All content (items, bosses, invocations, drops, prices) is data-driven per the brief — definition files mirroring the doc tables, never hardcoded.
- Boss mechanics build on shared systems (telegraph framework, Perfect Dodge, punish windows, DoT, style rotation) — never one-off code in a boss class.
- Never invent mechanics, numbers, or names. Flag design ambiguities as questions; never resolve them silently.
- Any numeric constant not sourced from a design doc must be marked provisional (code comment `// PROVISIONAL: <reason>`) and listed in both the milestone findings file and /design/plans/backlog.md. Unflagged invented constants are defects.
- Milestone workflow: plan first, write the plan to /design/plans/<milestone>-plan.md, implement only after the plan is reviewed.
- During and after implementation, write findings to /design/plans/<milestone>-findings.md as they happen — anywhere reality diverged from the plan's assumptions (a rule that turned out moot, a mechanism that landed in a different layer than sketched, a resolved open question), plus any verification caveat (e.g. tests/build not actually run). This is a separate file from the plan, always: the plan is what was intended, the findings file is what happened. Do this for every milestone, not just when asked.
- **Backlog discipline**: /design/plans/backlog.md is the durable, cross-milestone index of everything missing, deferred, blocked, or flagged-but-unconfirmed — a findings file records what happened during one milestone and then that milestone closes, so nothing left inside it gets picked up automatically. Anything a milestone's plan or findings identifies as not implemented, not fully wired, content-blocked, or an assumption standing in for a real answer (deferred features, unwired mechanics, content gaps, design questions, technical debt, cosmetic/renderer gaps) must be added to backlog.md in the same pass — not left to be rediscovered later. When an item is picked up, move it to backlog.md's "Resolved" section with a pointer to what closed it (never just delete it). Before starting any new milestone or standalone task, check backlog.md for items it might unblock or that are now in scope.
- After a step's implementation is complete (a milestone, or a discrete piece of one), fast-forward merge the working branch into `claude/text-duel-game-3t4vkf` and push, so changes are testable there without waiting for the whole milestone or a PR. Do this after every completed step, not just when asked.
- Styling: design tokens (CSS variables + semantic classes) only, no scattered inline styles. Interim look is neutral; doctrine colors (UI bible) must be correct.

## Locked architecture invariant
All game logic ticks in C#; Three.js is a dumb renderer receiving one batched
state snapshot per frame. No per-property JS interop inside the combat loop.
No gameplay decisions in JS.

C# simulation → state snapshot → Three renderer

Three.js is the only rendering technology in the project — combat,
previews, and any future visual surface. No secondary renderers.

- Telegraphs are always visual (outline/rim glow in doctrine color, windup pose,
  projectile color) plus audio — never text popups. The HUD forecast icon is a
  secondary echo, never the primary tell.

## Codebase navigation (knowledge graph)
This project has a knowledge graph at graphify-out/ with god nodes, community structure, and cross-file relationships.

Rules:
* For codebase questions, first run `graphify query "<question>"` when graphify-out/graph.json exists. Use `graphify path "<A>" "<B>"` for relationships and `graphify explain "<concept>"` for focused concepts. These return a scoped subgraph, usually much smaller than GRAPH_REPORT.md or raw grep output.
* If graphify-out/wiki/index.md exists, use it for broad navigation instead of raw source browsing.
* Read graphify-out/GRAPH_REPORT.md only for broad architecture review or when query/path/explain do not surface enough context.
* After modifying code, run `graphify update .` to keep the graph current (AST-only, no API cost).