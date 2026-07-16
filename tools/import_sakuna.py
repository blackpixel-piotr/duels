#!/usr/bin/env python3
"""Convert Sakuna VOX source into a rig-compatible player asset.

Reads the primary full-body model from resources/models/Sakuna/Sukuna Character VOX.vox,
re-encodes voxels into the v2 player part banding (12 parts, band=16), writes:
  - src/Duels.Web/wwwroot/assets/player_sakuna.vox
  - src/Duels.Web/wwwroot/assets/rigs.json (characters.player_sakuna)
"""

from __future__ import annotations

import copy
import json
import os
import struct
from collections import OrderedDict

ROOT = os.path.join(os.path.dirname(__file__), "..")
ASSETS = os.path.join(ROOT, "src", "Duels.Web", "wwwroot", "assets")
SOURCE = os.path.join(ROOT, "resources", "models", "Sakuna", "Sukuna Character VOX.vox")
OUT_VOX = os.path.join(ASSETS, "player_sakuna.vox")
RIGS_JSON = os.path.join(ASSETS, "rigs.json")
PLAYER_VOX = os.path.join(ASSETS, "player.vox")

BAND = 16
TARGET_PLAYER_HEIGHT = 58  # keep Sakuna close to existing player world fit

# v2 part indices (must match voxel.js + gen_models.py)
P_TORSO = 0
P_HEAD = 1
P_RARMU = 2
P_LARMU = 3
P_RTHIGH = 4
P_LTHIGH = 5
P_RARML = 6
P_LARML = 7
P_RSHIN = 8
P_LSHIN = 9
P_RFOOT = 10
P_LFOOT = 11


def _default_palette() -> list[tuple[int, int, int]]:
    """MagicaVoxel default palette (same ordering as voxel.js)."""
    p = [(0, 0, 0)]
    v = [255, 204, 153, 102, 51, 0]
    for r in v:
        for g in v:
            for b in v:
                p.append((r, g, b))
    ramp = [238, 221, 187, 170, 136, 119, 85, 68, 34, 17]
    for e in ramp:
        p.append((e, 0, 0))
    for e in ramp:
        p.append((0, e, 0))
    for e in ramp:
        p.append((0, 0, e))
    for e in ramp[:9]:
        p.append((e, e, e))
    return p[:256]


def read_models(path: str):
    """Return (models, palette) from VOX file.

    models: list of {"voxels":[{fx,fy,fz,ci}], "size":(x,y,z)} in file order.
    """
    with open(path, "rb") as f:
        buf = f.read()
    if buf[:4] != b"VOX ":
        raise ValueError(f"not a VOX file: {path}")

    palette = None
    models = []
    pending_size = None
    pos = 8

    while pos + 12 <= len(buf):
        cid = buf[pos : pos + 4]
        content_size, children_size = struct.unpack("<II", buf[pos + 4 : pos + 12])
        start = pos + 12
        if cid == b"SIZE":
            pending_size = struct.unpack("<3i", buf[start : start + 12])
        elif cid == b"XYZI":
            n = struct.unpack("<I", buf[start : start + 4])[0]
            vox = []
            for i in range(n):
                o = start + 4 + i * 4
                fx, fy, fz, ci = struct.unpack("<4B", buf[o : o + 4])
                vox.append({"fx": fx, "fy": fy, "fz": fz, "ci": ci})
            models.append({"voxels": vox, "size": pending_size})
        elif cid == b"RGBA":
            palette = [(0, 0, 0)]
            for i in range(255):
                o = start + i * 4
                r, g, b, _a = struct.unpack("<4B", buf[o : o + 4])
                palette.append((r, g, b))

        # MAIN: descend into children, same logic as runtime parser
        pos = start + content_size if cid == b"MAIN" else start + content_size + children_size

    if not models:
        raise ValueError(f"no XYZI chunk found: {path}")
    if palette is None:
        palette = _default_palette()
    return models, palette


def choose_primary_model(models):
    """Pick the full-body source model from a multi-model Sakuna VOX.

    Selection strategy:
    1) prefer model with largest voxel count (captures complete mesh),
    2) tie-break with larger runtime-mapped height.
    """
    def score(m):
        vox = m["voxels"]
        ys = [v["fz"] for v in vox]  # runtime y <- file z
        h = (max(ys) - min(ys) + 1) if ys else 0
        return (len(vox), h)

    return max(models, key=score)


def normalize_height(voxels, target_height: int):
    """Stretch source voxels vertically to a stable player-like height.

    Source VOX is authored as multiple fragments/frames with short Y spans.
    Matching the canonical player height keeps world/NPC fit coherent.
    """
    if not voxels:
        return voxels
    rvox = [to_renderer_coords(v) for v in voxels]
    min_y = min(v["y"] for v in rvox)
    max_y = max(v["y"] for v in rvox)
    h = max_y - min_y + 1
    sy = target_height / max(1, h)

    out = []
    for rv in rvox:
        x = int(round(rv["x"]))
        y = int(round((rv["y"] - min_y) * sy))
        z = int(round(rv["z"]))
        # back to VOX file coords (inverse of parse mapping)
        out.append({"fx": x, "fy": z, "fz": y, "ci": rv["ci"]})
    return out


def to_renderer_coords(v):
    # parseVox mapping: renderer y <- file z, renderer z <- file y
    return {"x": v["fx"], "y": v["fz"], "z": v["fy"], "ci": v["ci"]}


def classify_part(xc, yn, w):
    """Heuristic v2 part classification in centered/grounded space."""
    side_right = xc >= 0
    arm_zone = yn >= 0.45 and yn <= 0.82 and abs(xc) >= max(1.0, w * 0.22)
    if yn >= 0.82:
        return P_HEAD
    if yn <= 0.10:
        return P_RFOOT if side_right else P_LFOOT
    if yn <= 0.30:
        return P_RSHIN if side_right else P_LSHIN
    if yn <= 0.52:
        return P_RTHIGH if side_right else P_LTHIGH
    if arm_zone:
        return (P_RARMU if side_right else P_LARMU) if yn >= 0.62 else (P_RARML if side_right else P_LARML)
    return P_TORSO


def write_single_vox(path: str, voxels: list[dict], palette_band: list[tuple[int, int, int]], band: int):
    """Write one-model VOX file from file-space voxels already carrying final ci."""
    min_x = min(v["fx"] for v in voxels)
    min_y = min(v["fy"] for v in voxels)
    min_z = min(v["fz"] for v in voxels)
    max_x = max(v["fx"] for v in voxels)
    max_y = max(v["fy"] for v in voxels)
    max_z = max(v["fz"] for v in voxels)

    xyzi = struct.pack("<I", len(voxels))
    for v in voxels:
        x = v["fx"] - min_x
        y = v["fy"] - min_y
        z = v["fz"] - min_z
        xyzi += struct.pack("<4B", x, y, z, v["ci"])

    size = struct.pack("<3i", max_x - min_x + 1, max_y - min_y + 1, max_z - min_z + 1)
    rgba = b""
    for i in range(255):
        src = i % band
        r, g, b = palette_band[src] if src < len(palette_band) else (0, 0, 0)
        rgba += struct.pack("<4B", r, g, b, 255)
    rgba += struct.pack("<4B", 0, 0, 0, 255)

    def chunk(cid: bytes, content: bytes, children: bytes = b""):
        return cid + struct.pack("<II", len(content), len(children)) + content + children

    body = chunk(b"SIZE", size) + chunk(b"XYZI", xyzi) + chunk(b"RGBA", rgba)
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with open(path, "wb") as f:
        f.write(b"VOX " + struct.pack("<I", 150) + chunk(b"MAIN", b"", body))


def model_bounds_renderer(path: str):
    models, _palette = read_models(path)
    vox = models[0]["voxels"]  # generated assets here are single-model
    rvox = [to_renderer_coords(v) for v in vox]
    xs = [v["x"] for v in rvox]
    ys = [v["y"] for v in rvox]
    zs = [v["z"] for v in rvox]
    return {
        "min_x": min(xs),
        "max_x": max(xs),
        "min_y": min(ys),
        "max_y": max(ys),
        "min_z": min(zs),
        "max_z": max(zs),
    }


def build():
    models, src_palette = read_models(SOURCE)
    primary = choose_primary_model(models)
    src_vox = normalize_height(primary["voxels"], TARGET_PLAYER_HEIGHT)
    rvox = [to_renderer_coords(v) for v in src_vox]

    min_x = min(v["x"] for v in rvox)
    max_x = max(v["x"] for v in rvox)
    min_y = min(v["y"] for v in rvox)
    max_y = max(v["y"] for v in rvox)
    min_z = min(v["z"] for v in rvox)
    max_z = max(v["z"] for v in rvox)
    cx = (min_x + max_x) / 2.0
    height = max_y - min_y + 1
    width = max_x - min_x + 1

    # Stable palette mapping from source ci -> local color slot [0..15)
    ci_to_c0 = OrderedDict()
    band_palette = []
    for v in rvox:
        ci = v["ci"]
        if ci not in ci_to_c0:
            c0 = len(ci_to_c0)
            if c0 >= BAND:
                raise ValueError("source uses too many colors for band=16")
            ci_to_c0[ci] = c0
            band_palette.append(src_palette[ci] if ci < len(src_palette) else (255, 0, 255))

    # Re-encode with v2 part bands.
    out_vox = []
    for src, rv in zip(src_vox, rvox):
        y_ground = rv["y"] - min_y
        yn = y_ground / max(1, height - 1)
        xc = rv["x"] - cx
        part = classify_part(xc, yn, width)
        c0 = ci_to_c0[rv["ci"]]
        ci_out = part * BAND + c0 + 1
        out_vox.append({"fx": src["fx"], "fy": src["fy"], "fz": src["fz"], "ci": ci_out, "part": part})

    write_single_vox(OUT_VOX, out_vox, band_palette, BAND)

    # Create a Sakuna rig by scaling the existing player rig to new model bounds.
    with open(RIGS_JSON, "r", encoding="utf-8") as f:
        rigs = json.load(f)
    base = rigs.get("characters", {}).get("player")
    if not base:
        raise ValueError("characters.player rig not found in rigs.json")

    old_b = model_bounds_renderer(PLAYER_VOX)
    new_b = model_bounds_renderer(OUT_VOX)
    old_w = (old_b["max_x"] - old_b["min_x"] + 1) or 1
    old_h = (old_b["max_y"] - old_b["min_y"] + 1) or 1
    old_d = (old_b["max_z"] - old_b["min_z"] + 1) or 1
    new_w = (new_b["max_x"] - new_b["min_x"] + 1) or 1
    new_h = (new_b["max_y"] - new_b["min_y"] + 1) or 1
    new_d = (new_b["max_z"] - new_b["min_z"] + 1) or 1
    sx, sy, sz = new_w / old_w, new_h / old_h, new_d / old_d

    def scale_point(p):
        if not p:
            return p
        return [round(p[0] * sx, 3), round(p[1] * sy, 3), round(p[2] * sz, 3)]

    sakuna = copy.deepcopy(base)
    sakuna["band"] = BAND
    if "parts" in sakuna:
        for part in sakuna["parts"]:
            part["pivot"] = scale_point(part.get("pivot"))
    sakuna["hand"] = scale_point(sakuna.get("hand"))
    rigs.setdefault("characters", {})["player_sakuna"] = sakuna

    with open(RIGS_JSON, "w", encoding="utf-8") as f:
        json.dump(rigs, f, indent=1)
        f.write("\n")

    print("wrote", OUT_VOX)
    print("updated", RIGS_JSON, "with characters.player_sakuna")
    print("source_models", len(models), "selected_voxels", len(src_vox), "colors", len(ci_to_c0), "height", height, "width", width)


if __name__ == "__main__":
    build()
