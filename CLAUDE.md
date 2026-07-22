# Duels — CLAUDE.md

Mobile-first (landscape), real-time 1v1 boss-fighting game. Stack: Blazor + C# (game logic) + Three.js (rendering).

## Design authority
- All design lives in /design/*.md — read the implementation brief (/design/duels-implementation-brief.md) before planning any feature, and the relevant design doc before implementing it.
- **M1 is complete.** M2 has not been started or authorized — do not implement M2+ scope (bank, shop, other bosses, invocations, minigame, drops) until told to start it.
- All content (items, bosses, invocations, drops, prices) is data-driven per the brief — definition files mirroring the doc tables, never hardcoded.
- Boss mechanics build on shared systems (telegraph framework, Perfect Dodge, punish windows, DoT, style rotation) — never one-off code in a boss class.
- Never invent mechanics, numbers, or names. Flag design ambiguities as questions; never resolve them silently.
- Milestone workflow: plan first, write the plan to /design/plans/<milestone>-plan.md, implement only after the plan is reviewed.
- During and after implementation, write findings to /design/plans/<milestone>-findings.md as they happen — anywhere reality diverged from the plan's assumptions (a rule that turned out moot, a mechanism that landed in a different layer than sketched, a resolved open question), plus any verification caveat (e.g. tests/build not actually run). This is a separate file from the plan, always: the plan is what was intended, the findings file is what happened. Do this for every milestone, not just when asked.
- After a step's implementation is complete (a milestone, or a discrete piece of one), fast-forward merge the working branch into `claude/text-duel-game-3t4vkf` and push, so changes are testable there without waiting for the whole milestone or a PR. Do this after every completed step, not just when asked.
- Styling: design tokens (CSS variables + semantic classes) only, no scattered inline styles. Interim look is neutral; doctrine colors (UI bible) must be correct.

## Locked architecture invariant
All game logic ticks in C#; Three.js is a dumb renderer receiving one batched
state snapshot per frame. No per-property JS interop inside the combat loop.
No gameplay decisions in JS.

C# simulation → state snapshot → Three renderer

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