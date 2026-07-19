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
| ranger_hood | Ranger Hood | assets/models/equip/Male_Ranger_Head_Hood.gltf | ranger_leather | 🟢 |
| ranger_tunic | Ranger Tunic | assets/models/equip/Male_Ranger_Body.gltf | ranger_leather | 🟢 |
| ranger_trousers | Ranger Trousers | assets/models/equip/Male_Ranger_Legs.gltf | ranger_leather | 🟢 |
| ranger_boots | Ranger Boots | assets/models/equip/Male_Ranger_Feet_Boots.gltf | ranger_leather | 🟢 |
| ranger_bracers | Ranger Bracers | assets/models/equip/Male_Ranger_Arms.gltf | ranger_leather | 🟢 |
| ranger_pauldrons | Ranger Pauldrons | assets/models/equip/Male_Ranger_Acc_Pauldron.gltf | ranger_leather | 🟢 |

Everything else in the items doc's tables (§2–§6) ships without a modeled
asset yet — those item ids render animation-only until a row is added here
(and to asset-manifest.json) as models are sourced.
