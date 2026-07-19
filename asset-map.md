# Duels — Asset Manifest

Human-readable mirror of `src/Duels.Web/wwwroot/data/asset-manifest.json` (the
file the battle renderer actually loads — see `toon.js`'s `loadAssetManifest`).
One row per item ID; art replaces rows one at a time with zero data changes,
per the items doc §7 convention. Kept in sync by
`tests/Duels.Infrastructure.Tests/AssetMapSyncTests.cs` — update both files
together when adding a modeled item.

**Flags:** 🟢 confirmed/near-certain in the target packs · 🟡 common archetype,
likely findable · 🔴 exotic, ships with a fallback archetype instead.

| item_id | display_name | model_ref | tint/material | flag |
|---|---|---|---|---|
| steel_sword | Steel Sword | assets/models/equip/sword.glb | tier1_iron | 🟢 |
| wpn_melee_t1 | Rustcleaver | assets/models/equip/sword.glb | placeholder_reskin | 🟡 |
| wpn_ranged_t1 | Poacher's Bow | assets/models/equip/sword.glb | placeholder_reskin | 🟡 |
| wpn_magic_t1 | Cinder Wand | assets/models/equip/sword.glb | placeholder_reskin | 🟡 |
| wpn_melee_t2 | Splitter | assets/models/equip/sword.glb | placeholder_reskin | 🟡 |
| wpn_ranged_t2 | Bolt Thrower | assets/models/equip/sword.glb | placeholder_reskin | 🟡 |
| wpn_magic_t2 | Hexknot Staff | assets/models/equip/sword.glb | placeholder_reskin | 🟡 |
| ranger_hood | Ranger Hood | assets/models/equip/Male_Ranger_Head_Hood.gltf | ranger_leather | 🟢 |
| ranger_tunic | Ranger Tunic | assets/models/equip/Male_Ranger_Body.gltf | ranger_leather | 🟢 |
| ranger_trousers | Ranger Trousers | assets/models/equip/Male_Ranger_Legs.gltf | ranger_leather | 🟢 |
| ranger_boots | Ranger Boots | assets/models/equip/Male_Ranger_Feet_Boots.gltf | ranger_leather | 🟢 |
| ranger_bracers | Ranger Bracers | assets/models/equip/Male_Ranger_Arms.gltf | ranger_leather | 🟢 |
| ranger_pauldrons | Ranger Pauldrons | assets/models/equip/Male_Ranger_Acc_Pauldron.gltf | ranger_leather | 🟢 |
| arm_warbound_helmet_t1 | Warbound Helmet (T1) | assets/models/equip/Male_Ranger_Head_Hood.gltf | warbound_placeholder | 🟡 |
| arm_warbound_helmet_t2 | Warbound Helmet (T2) | assets/models/equip/Male_Ranger_Head_Hood.gltf | warbound_placeholder | 🟡 |
| arm_warbound_body_t1 | Warbound Body (T1) | assets/models/equip/Male_Ranger_Body.gltf | warbound_placeholder | 🟡 |
| arm_warbound_body_t2 | Warbound Body (T2) | assets/models/equip/Male_Ranger_Body.gltf | warbound_placeholder | 🟡 |
| arm_warbound_legs_t1 | Warbound Legs (T1) | assets/models/equip/Male_Ranger_Legs.gltf | warbound_placeholder | 🟡 |
| arm_warbound_legs_t2 | Warbound Legs (T2) | assets/models/equip/Male_Ranger_Legs.gltf | warbound_placeholder | 🟡 |
| arm_warbound_boots_t1 | Warbound Boots (T1) | assets/models/equip/Male_Ranger_Feet_Boots.gltf | warbound_placeholder | 🟡 |
| arm_warbound_boots_t2 | Warbound Boots (T2) | assets/models/equip/Male_Ranger_Feet_Boots.gltf | warbound_placeholder | 🟡 |
| arm_warbound_gloves_t1 | Warbound Gloves (T1) | assets/models/equip/Male_Ranger_Arms.gltf | warbound_placeholder | 🟡 |
| arm_warbound_gloves_t2 | Warbound Gloves (T2) | assets/models/equip/Male_Ranger_Arms.gltf | warbound_placeholder | 🟡 |
| arm_warbound_cape_t1 | Warbound Cape (T1) | assets/models/equip/Male_Ranger_Acc_Pauldron.gltf | warbound_placeholder | 🟡 |
| arm_warbound_cape_t2 | Warbound Cape (T2) | assets/models/equip/Male_Ranger_Acc_Pauldron.gltf | warbound_placeholder | 🟡 |
| arm_stalker_helmet_t1 | Stalker Helmet (T1) | assets/models/equip/Male_Ranger_Head_Hood.gltf | stalker_placeholder | 🟡 |
| arm_stalker_helmet_t2 | Stalker Helmet (T2) | assets/models/equip/Male_Ranger_Head_Hood.gltf | stalker_placeholder | 🟡 |
| arm_stalker_body_t1 | Stalker Body (T1) | assets/models/equip/Male_Ranger_Body.gltf | stalker_placeholder | 🟡 |
| arm_stalker_body_t2 | Stalker Body (T2) | assets/models/equip/Male_Ranger_Body.gltf | stalker_placeholder | 🟡 |
| arm_stalker_legs_t1 | Stalker Legs (T1) | assets/models/equip/Male_Ranger_Legs.gltf | stalker_placeholder | 🟡 |
| arm_stalker_legs_t2 | Stalker Legs (T2) | assets/models/equip/Male_Ranger_Legs.gltf | stalker_placeholder | 🟡 |
| arm_stalker_boots_t1 | Stalker Boots (T1) | assets/models/equip/Male_Ranger_Feet_Boots.gltf | stalker_placeholder | 🟡 |
| arm_stalker_boots_t2 | Stalker Boots (T2) | assets/models/equip/Male_Ranger_Feet_Boots.gltf | stalker_placeholder | 🟡 |
| arm_stalker_gloves_t1 | Stalker Gloves (T1) | assets/models/equip/Male_Ranger_Arms.gltf | stalker_placeholder | 🟡 |
| arm_stalker_gloves_t2 | Stalker Gloves (T2) | assets/models/equip/Male_Ranger_Arms.gltf | stalker_placeholder | 🟡 |
| arm_stalker_cape_t1 | Stalker Cape (T1) | assets/models/equip/Male_Ranger_Acc_Pauldron.gltf | stalker_placeholder | 🟡 |
| arm_stalker_cape_t2 | Stalker Cape (T2) | assets/models/equip/Male_Ranger_Acc_Pauldron.gltf | stalker_placeholder | 🟡 |
| arm_occult_helmet_t1 | Occult Helmet (T1) | assets/models/equip/Male_Ranger_Head_Hood.gltf | occult_placeholder | 🟡 |
| arm_occult_helmet_t2 | Occult Helmet (T2) | assets/models/equip/Male_Ranger_Head_Hood.gltf | occult_placeholder | 🟡 |
| arm_occult_body_t1 | Occult Body (T1) | assets/models/equip/Male_Ranger_Body.gltf | occult_placeholder | 🟡 |
| arm_occult_body_t2 | Occult Body (T2) | assets/models/equip/Male_Ranger_Body.gltf | occult_placeholder | 🟡 |
| arm_occult_legs_t1 | Occult Legs (T1) | assets/models/equip/Male_Ranger_Legs.gltf | occult_placeholder | 🟡 |
| arm_occult_legs_t2 | Occult Legs (T2) | assets/models/equip/Male_Ranger_Legs.gltf | occult_placeholder | 🟡 |
| arm_occult_boots_t1 | Occult Boots (T1) | assets/models/equip/Male_Ranger_Feet_Boots.gltf | occult_placeholder | 🟡 |
| arm_occult_boots_t2 | Occult Boots (T2) | assets/models/equip/Male_Ranger_Feet_Boots.gltf | occult_placeholder | 🟡 |
| arm_occult_gloves_t1 | Occult Gloves (T1) | assets/models/equip/Male_Ranger_Arms.gltf | occult_placeholder | 🟡 |
| arm_occult_gloves_t2 | Occult Gloves (T2) | assets/models/equip/Male_Ranger_Arms.gltf | occult_placeholder | 🟡 |
| arm_occult_cape_t1 | Occult Cape (T1) | assets/models/equip/Male_Ranger_Acc_Pauldron.gltf | occult_placeholder | 🟡 |
| arm_occult_cape_t2 | Occult Cape (T2) | assets/models/equip/Male_Ranger_Acc_Pauldron.gltf | occult_placeholder | 🟡 |

M1 placeholder note: the 6 doc weapons (`wpn_*`) all reuse `sword.glb`, and
the 36 doc armour pieces (`arm_*`) reuse the 6 ranger outfit pieces by slot
(ignoring line/tier) — this sandbox has no internet access to source the
Quaternius packs the items doc names (§7's shopping list), so a re-skinned
placeholder beats an invisible item. Swap these rows for real per-line/tier
models as they're sourced; zero data-shape changes needed elsewhere.

Everything else in the items doc's tables (§3–§4, boss uniques/rares — M2+)
ships without a modeled asset yet — those item ids render animation-only
until a row is added here (and to asset-manifest.json) as models are sourced.
