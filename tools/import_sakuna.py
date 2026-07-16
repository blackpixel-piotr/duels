#!/usr/bin/env python3
"""Convert the Sakuna VOX source into a rig-compatible player asset.

The source (resources/models/Sakuna/Sukuna Character VOX.vox) is a rigged
MagicaVoxel scene: 17 sub-models — one per body part — positioned by an
nTRN/nGRP/nSHP graph whose named transform nodes (Hip, Left_Thigh,
Right_Forearm, Head, ...) sit exactly at the anatomical joints. This script
walks that graph, bakes every sub-model into one voxel grid, re-encodes the
colors into v2 part banding, and emits the joint positions as rig pivots:
  - src/Duels.Web/wwwroot/assets/player_sakuna.vox
  - src/Duels.Web/wwwroot/assets/rigs.json (characters.player_sakuna)

Run:  python3 tools/import_sakuna.py
"""

from __future__ import annotations

import json
import os
import struct

ROOT = os.path.join(os.path.dirname(__file__), "..")
ASSETS = os.path.join(ROOT, "src", "Duels.Web", "wwwroot", "assets")
SOURCE = os.path.join(ROOT, "resources", "models", "Sakuna", "Sukuna Character VOX.vox")
OUT_VOX = os.path.join(ASSETS, "player_sakuna.vox")
RIGS_JSON = os.path.join(ASSETS, "rigs.json")

# 19 distinct source colors — band 16 can't hold them (12 parts * 20 + 1 <= 255)
BAND = 20

# v2 part indices (must match voxel.js VOX_OPTS + gen_models.py)
P_TORSO, P_HEAD = 0, 1
P_RARMU, P_LARMU, P_RTHIGH, P_LTHIGH = 2, 3, 4, 5
P_RARML, P_LARML, P_RSHIN, P_LSHIN = 6, 7, 8, 9
P_RFOOT, P_LFOOT = 10, 11

# Deepest named ancestor decides the part. Order within a limb chain matters
# only in that deeper nodes override shallower ones while walking down.
NAME_TO_PART = {
    "Head": P_HEAD,
    "Right_Arm": P_RARMU, "Right_Forearm": P_RARML, "Right_Hand": P_RARML,
    "Left_Arm": P_LARMU, "Left_Forearm": P_LARML, "Left_Hand": P_LARML,
    "Right_Thigh": P_RTHIGH, "Right_Leg": P_RSHIN, "Right_Foot": P_RFOOT,
    "Left_Thigh": P_LTHIGH, "Left_Leg": P_LSHIN, "Left_Foot": P_LFOOT,
}


# ── VOX parsing ────────────────────────────────────────────────────────────

def _read_dict(buf, o):
    n = struct.unpack("<i", buf[o:o + 4])[0]; o += 4
    d = {}
    for _ in range(n):
        kl = struct.unpack("<i", buf[o:o + 4])[0]; o += 4
        k = buf[o:o + kl].decode(); o += kl
        vl = struct.unpack("<i", buf[o:o + 4])[0]; o += 4
        d[k] = buf[o:o + vl].decode(); o += vl
    return d, o


def read_scene(path):
    """Parse models + scene graph. Returns (models, palette, nodes)."""
    with open(path, "rb") as f:
        buf = f.read()
    if buf[:4] != b"VOX ":
        raise ValueError(f"not a VOX file: {path}")

    models = []          # [{size:(x,y,z), voxels:[(x,y,z,ci)]}]
    palette = None
    nodes = {}           # id -> {kind, ...}
    pending_size = None
    pos = 8
    while pos + 12 <= len(buf):
        cid = buf[pos:pos + 4]
        cs, ks = struct.unpack("<II", buf[pos + 4:pos + 12])
        start = pos + 12
        if cid == b"SIZE":
            pending_size = struct.unpack("<3i", buf[start:start + 12])
        elif cid == b"XYZI":
            n = struct.unpack("<I", buf[start:start + 4])[0]
            vox = [struct.unpack("<4B", buf[start + 4 + i * 4:start + 8 + i * 4])
                   for i in range(n)]
            models.append({"size": pending_size, "voxels": vox})
        elif cid == b"RGBA":
            palette = [(0, 0, 0)]
            for i in range(255):
                r, g, b, _a = struct.unpack("<4B", buf[start + i * 4:start + i * 4 + 4])
                palette.append((r, g, b))
        elif cid == b"nTRN":
            o = start
            nid = struct.unpack("<i", buf[o:o + 4])[0]; o += 4
            attr, o = _read_dict(buf, o)
            child = struct.unpack("<i", buf[o:o + 4])[0]; o += 4
            o += 8  # reserved + layer
            nf = struct.unpack("<i", buf[o:o + 4])[0]; o += 4
            frame, o = _read_dict(buf, o)  # frame 0 only
            nodes[nid] = {"kind": "trn", "name": attr.get("_name"),
                          "child": child,
                          "t": tuple(int(s) for s in frame.get("_t", "0 0 0").split()),
                          "r": int(frame.get("_r", "4"))}
        elif cid == b"nGRP":
            o = start
            nid = struct.unpack("<i", buf[o:o + 4])[0]; o += 4
            _attr, o = _read_dict(buf, o)
            n = struct.unpack("<i", buf[o:o + 4])[0]; o += 4
            kids = list(struct.unpack(f"<{n}i", buf[o:o + 4 * n]))
            nodes[nid] = {"kind": "grp", "children": kids}
        elif cid == b"nSHP":
            o = start
            nid = struct.unpack("<i", buf[o:o + 4])[0]; o += 4
            _attr, o = _read_dict(buf, o)
            n = struct.unpack("<i", buf[o:o + 4])[0]; o += 4
            mid = struct.unpack("<i", buf[o:o + 4])[0]  # first model of frame 0
            nodes[nid] = {"kind": "shp", "model": mid}
        pos = start + cs if cid == b"MAIN" else start + cs + ks

    if not models:
        raise ValueError(f"no XYZI chunks: {path}")
    return models, palette, nodes


def rot_matrix(byte):
    """Decode a MagicaVoxel _r rotation byte into a 3x3 int matrix."""
    i0 = byte & 3
    i1 = (byte >> 2) & 3
    i2 = ({0, 1, 2} - {i0, i1}).pop()
    s0 = -1 if byte & 16 else 1
    s1 = -1 if byte & 32 else 1
    s2 = -1 if byte & 64 else 1
    m = [[0, 0, 0], [0, 0, 0], [0, 0, 0]]
    m[0][i0] = s0
    m[1][i1] = s1
    m[2][i2] = s2
    return m


def mat_vec(m, v):
    return tuple(m[i][0] * v[0] + m[i][1] * v[1] + m[i][2] * v[2] for i in range(3))


def mat_mat(a, b):
    return [[sum(a[i][k] * b[k][j] for k in range(3)) for j in range(3)] for i in range(3)]


def flatten_scene(models, nodes):
    """Walk the graph from root nTRN (id 0). Returns (voxels, joints):
    voxels = [(wx, wy, wz, ci, part)] in file space,
    joints = {name: (wx, wy, wz)} world position of every named nTRN."""
    out = []
    joints = {}

    def walk(nid, R, T, part, depth_names):
        node = nodes[nid]
        if node["kind"] == "trn":
            R2 = mat_mat(R, rot_matrix(node["r"]))
            T2 = tuple(T[i] + mat_vec(R, node["t"])[i] for i in range(3))
            name = node["name"]
            p = part
            if name:
                # first occurrence wins (outer/inner duplicates like 'Hip')
                joints.setdefault(name, T2)
                p = NAME_TO_PART.get(name, part)
            walk(node["child"], R2, T2, p, depth_names + ([name] if name else []))
        elif node["kind"] == "grp":
            for kid in node["children"]:
                walk(kid, R, T, part, depth_names)
        elif node["kind"] == "shp":
            m = models[node["model"]]
            sx, sy, sz = m["size"]
            piv = (sx // 2, sy // 2, sz // 2)
            for x, y, z, ci in m["voxels"]:
                lx, ly, lz = x - piv[0], y - piv[1], z - piv[2]
                wx, wy, wz = mat_vec(R, (lx, ly, lz))
                out.append((wx + T[0], wy + T[1], wz + T[2], ci, part))

    walk(0, [[1, 0, 0], [0, 1, 0], [0, 0, 1]], (0, 0, 0), P_TORSO, [])
    return out, joints


# ── Output ─────────────────────────────────────────────────────────────────

def write_single_vox(path, voxels, band_palette, band):
    """voxels: [(fx, fy, fz, ci_out)] already banded, any integer coords."""
    min_x = min(v[0] for v in voxels)
    min_y = min(v[1] for v in voxels)
    min_z = min(v[2] for v in voxels)
    max_x = max(v[0] for v in voxels)
    max_y = max(v[1] for v in voxels)
    max_z = max(v[2] for v in voxels)

    xyzi = struct.pack("<I", len(voxels))
    for fx, fy, fz, ci in voxels:
        xyzi += struct.pack("<4B", fx - min_x, fy - min_y, fz - min_z, ci)

    size = struct.pack("<3i", max_x - min_x + 1, max_y - min_y + 1, max_z - min_z + 1)
    rgba = b""
    for i in range(255):
        src = i % band
        r, g, b = band_palette[src] if src < len(band_palette) else (255, 0, 255)
        rgba += struct.pack("<4B", r, g, b, 255)
    rgba += struct.pack("<4B", 0, 0, 0, 255)

    def chunk(cid, content, children=b""):
        return cid + struct.pack("<II", len(content), len(children)) + content + children

    body = chunk(b"SIZE", size) + chunk(b"XYZI", xyzi) + chunk(b"RGBA", rgba)
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with open(path, "wb") as f:
        f.write(b"VOX " + struct.pack("<I", 150) + chunk(b"MAIN", b"", body))


def silhouette(voxels, axis_a, axis_b, flip_b=True, max_w=70):
    """ASCII projection of world voxels onto (axis_a → column, axis_b → row)."""
    a_vals = [v[axis_a] for v in voxels]
    b_vals = [v[axis_b] for v in voxels]
    a0, a1 = min(a_vals), max(a_vals)
    b0, b1 = min(b_vals), max(b_vals)
    grid = [[" "] * (a1 - a0 + 1) for _ in range(b1 - b0 + 1)]
    for v in voxels:
        grid[v[axis_b] - b0][v[axis_a] - a0] = "#"
    rows = grid[::-1] if flip_b else grid
    return "\n".join("".join(r) for r in rows)


# ── Restyle: Sukuna → OSRS-default look ────────────────────────────────────
# Short brown hair (spikes trimmed, black undercut recolored), plain t-shirt
# over the bare torso (erases the body markings/belly face), green trousers,
# skin forearms, brown boots. Palette slots 230+ carry the new colors.
HAIR_CI, HAIR_D_CI = 230, 231
SHIRT_CI, SHIRT_D_CI = 232, 233
PANTS_CI, PANTS_D_CI = 234, 235
BOOT_CI, SOLE_CI = 236, 237
NEW_COLORS = {
    HAIR_CI: (106, 72, 40), HAIR_D_CI: (78, 52, 30),
    SHIRT_CI: (198, 189, 167), SHIRT_D_CI: (164, 154, 132),
    PANTS_CI: (74, 108, 56), PANTS_D_CI: (54, 82, 42),
    BOOT_CI: (94, 70, 46), SOLE_CI: (52, 44, 38),
}
PINK = {7, 9}          # bright hair
PINK_D = {8}           # hair shade
BLACKISH = {11, 12, 13, 14, 15, 16, 17, 255}
SKIN_LIGHT = 5


def restyle(world, palette, min_y):
    for ci, rgb in NEW_COLORS.items():
        palette[ci] = rgb
    out = []
    for x, y, z, ci, p in world:
        ry = z - min_y  # renderer height (z is file-up)
        if p == P_HEAD:
            hairish = ci in PINK or ci in PINK_D or (ci in BLACKISH and ry >= 48)
            if hairish and ry > 58:
                continue                       # trim the spikes: short hair
            if ci in PINK: ci = HAIR_CI
            elif ci in PINK_D: ci = HAIR_D_CI
            elif hairish: ci = HAIR_D_CI       # black undercut → dark brown
        elif p == P_TORSO:
            if ry < 39:                        # (39+ = neck, stays skin)
                if ry <= 25:                   # hips → trouser waist
                    ci = PANTS_CI if ci == SKIN_LIGHT else PANTS_D_CI
                else:                          # t-shirt over the bare torso
                    ci = SHIRT_CI if ci == SKIN_LIGHT else SHIRT_D_CI
        elif p in (P_RARMU, P_LARMU):
            if ry >= 34:                       # short sleeve
                ci = SHIRT_CI if ci == SKIN_LIGHT else SHIRT_D_CI
            elif ci in BLACKISH:               # arm bands → bare skin
                ci = SKIN_LIGHT
        elif p in (P_RARML, P_LARML):
            if ci in BLACKISH: ci = SKIN_LIGHT # wrist marks → bare skin
        elif p in (P_RTHIGH, P_LTHIGH, P_RSHIN, P_LSHIN):
            ci = PANTS_CI if ci == 16 else PANTS_D_CI
        elif p in (P_RFOOT, P_LFOOT):
            ci = BOOT_CI if ci == 10 else SOLE_CI
        out.append((x, y, z, ci, p))
    return out


def build():
    models, palette, nodes = read_scene(SOURCE)
    world, joints = flatten_scene(models, nodes)
    world = restyle(world, palette, min(v[2] for v in world))

    # Color slots: source ci -> [0, BAND)
    ci_to_c0 = {}
    band_palette = []
    for _x, _y, _z, ci, _p in world:
        if ci not in ci_to_c0:
            if len(ci_to_c0) >= BAND:
                raise ValueError(f"source uses more than {BAND} colors")
            ci_to_c0[ci] = len(ci_to_c0)
            band_palette.append(palette[ci] if palette and ci < len(palette) else (255, 0, 255))

    out_vox = [(x, y, z, part * BAND + ci_to_c0[ci] + 1) for x, y, z, ci, part in world]
    write_single_vox(OUT_VOX, out_vox, band_palette, BAND)

    # ── Rig: renderer coords are x=file x, y=file z, z=file y; prepModel
    # centers x/z on the bounding-box middle and grounds y on minY.
    xs = [v[0] for v in world]
    ys = [v[2] for v in world]   # renderer y = file z
    zs = [v[1] for v in world]   # renderer z = file y
    cx = (min(xs) + max(xs)) / 2.0
    cz = (min(zs) + max(zs)) / 2.0
    min_y = min(ys)

    # Pivots come from the assembled PART GEOMETRY, not the scene's node
    # origins — this file's transform nodes do not sit at the anatomical
    # joints (the "Right_Arm" node lands below the whole arm), so rotating
    # about them tears limbs off the body. The joint is where two parts
    # meet: top of the lower part / boundary midpoint, centered on the
    # limb's own bounding box.
    bbox = {}
    for x, y, z, _ci, part in world:
        rx, ry, rz = x - cx, z - min_y, y - cz
        b = bbox.setdefault(part, [1e9, -1e9, 1e9, -1e9, 1e9, -1e9])
        b[0] = min(b[0], rx); b[1] = max(b[1], rx)
        b[2] = min(b[2], ry); b[3] = max(b[3], ry)
        b[4] = min(b[4], rz); b[5] = max(b[5], rz)

    def cxz(p):  # part bbox center in x/z
        b = bbox[p]
        return (b[0] + b[1]) / 2.0, (b[4] + b[5]) / 2.0

    def joint(upper_part, lower_part):
        """Pivot where lower_part hangs off upper_part: the seam y, at the
        lower part's horizontal center."""
        y = (bbox[upper_part][2] + bbox[lower_part][3]) / 2.0
        x, z = cxz(lower_part)
        return [round(x, 2), round(y, 2), round(z, 2)]

    def top_of(part, drop=1.0):
        x, z = cxz(part)
        return [round(x, 2), round(bbox[part][3] - drop, 2), round(z, 2)]

    neck = joint(P_HEAD, P_TORSO)              # head rotates where it meets the torso
    r_sho, l_sho = top_of(P_RARMU), top_of(P_LARMU)
    r_elb, l_elb = joint(P_RARMU, P_RARML), joint(P_LARMU, P_LARML)
    r_hip, l_hip = top_of(P_RTHIGH, 0.0), top_of(P_LTHIGH, 0.0)
    r_kne, l_kne = joint(P_RTHIGH, P_RSHIN), joint(P_LTHIGH, P_LSHIN)

    def ankle(shin, foot):  # under the shin (the foot extends toe-ward)
        x, z = cxz(shin)
        return [round(x, 2), round((bbox[shin][2] + bbox[foot][3]) / 2.0, 2), round(z, 2)]

    r_ank, l_ank = ankle(P_RSHIN, P_RFOOT), ankle(P_LSHIN, P_LFOOT)
    b = bbox[P_RARML]                          # hand = bottom of the forearm
    r_hand = [round((b[0] + b[1]) / 2.0, 2), round(b[2] + 1.0, 2),
              round((b[4] + b[5]) / 2.0, 2)]

    rig = {
        "band": BAND,
        "parts": [
            {"parent": None, "pivot": None},   # 0 torso (hip+belly+chest)
            {"parent": 0, "pivot": neck},      # 1 head
            {"parent": 0, "pivot": r_sho},     # 2 right upper arm
            {"parent": 0, "pivot": l_sho},     # 3 left upper arm
            {"parent": 0, "pivot": r_hip},     # 4 right thigh
            {"parent": 0, "pivot": l_hip},     # 5 left thigh
            {"parent": 2, "pivot": r_elb},     # 6 right forearm+hand
            {"parent": 3, "pivot": l_elb},     # 7 left forearm+hand
            {"parent": 4, "pivot": r_kne},     # 8 right shin
            {"parent": 5, "pivot": l_kne},     # 9 left shin
            {"parent": 8, "pivot": r_ank},     # 10 right foot
            {"parent": 9, "pivot": l_ank},     # 11 left foot
        ],
        "ik": {"legs": [
            {"hip": 4, "knee": 8, "foot": 10},
            {"hip": 5, "knee": 9, "foot": 11},
        ]},
        "handPart": 6,
        "hand": r_hand,
    }

    with open(RIGS_JSON, "r", encoding="utf-8") as f:
        rigs = json.load(f)
    rigs.setdefault("characters", {})["player_sakuna"] = rig
    with open(RIGS_JSON, "w", encoding="utf-8") as f:
        json.dump(rigs, f, indent=1)
        f.write("\n")

    # ── Report ─────────────────────────────────────────────────────────────
    w = max(xs) - min(xs) + 1
    h = max(ys) - min(ys) + 1
    d = max(zs) - min(zs) + 1
    counts = {}
    for _x, _y, _z, _ci, p in world:
        counts[p] = counts.get(p, 0) + 1
    print(f"wrote {OUT_VOX}: {len(world)} voxels, {w}x{d}x{h} (w x d x h), colors {len(ci_to_c0)}")
    print("per-part voxels:", {k: counts[k] for k in sorted(counts)})
    print("pivots: neck", neck, "rSho", r_sho, "rElb", r_elb)
    print("        rHip", r_hip, "rKnee", r_kne, "rAnkle", r_ank, "hand", r_hand)
    print("leg lengths: L1", round(r_hip[1] - r_kne[1], 2), "L2", round(r_kne[1] - r_ank[1], 2))
    print("feet ground check: min renderer y =", 0, "(grounded by construction)")
    print("\nfront silhouette (x/z-up):")
    print(silhouette(world, 0, 2))
    print("\nside silhouette (y/z-up):")
    print(silhouette(world, 1, 2))


if __name__ == "__main__":
    build()
