#!/usr/bin/env python3
"""Generate OSRS/RS3-style adult-proportioned voxel characters and weapons.

Renderer coords: x right, y up (ground 0), z front (toward camera at angle 0).
MagicaVoxel file mapping (matches voxel.js parseVox): file x=x, y=z, z=y.

Rigging: each voxel's part (torso/head/rArm/lArm/rLeg/lLeg) is encoded in its
.vox color index — index = part*BAND + color + 1, with the palette duplicated
across the 6 bands, so any .vox viewer sees normal colors while voxel.js can
recover the part as (ci-1) // BAND. Joint pivots, hand anchors, and weapon
grips are written to assets/rigs.json in the same centered model coords that
voxel.js prepModel produces.
"""
import struct, os, math, json

ASSETS = os.path.join(os.path.dirname(__file__), '..', 'src', 'Duels.Web', 'wwwroot', 'assets')

PARTS = ['torso', 'head', 'rArm', 'lArm', 'rLeg', 'lLeg']
BAND = 40

# v2 hierarchical rig (player): limbs split at elbow/knee/ankle so the
# renderer can chain rotations (FK) and solve leg IK. Part indices 0-5 keep
# the legacy meaning (torso/head/right arm/left arm/right leg/left leg) so
# attack poses that target part 2 still swing the (upper) weapon arm.
PARTS_V2 = ['torso', 'head', 'rArmU', 'lArmU', 'rThigh', 'lThigh',
            'rArmL', 'lArmL', 'rShin', 'lShin', 'rFoot', 'lFoot']
BAND_V2 = 16  # 12 parts × 16 ≤ 255 palette slots; ≤15 colors per model


def write_vox(path, voxels, palette, banded, band=BAND):
    """voxels: {(x,y,z): (color0, part)} renderer coords; color0 is 0-based."""
    mn = [min(v[i] for v in voxels) for i in range(3)]
    xyzi = b''
    for (x, y, z), (c0, part) in voxels.items():
        fx, fy, fz = x - mn[0], z - mn[2], y - mn[1]
        assert 0 <= fx < 256 and 0 <= fy < 256 and 0 <= fz < 256
        ci = (part * band + c0 + 1) if banded else (c0 + 1)
        assert ci <= 255, 'color index overflow'
        xyzi += struct.pack('<4B', fx, fy, fz, ci)
    size = struct.pack('<3i', max(v[0] for v in voxels) - mn[0] + 1,
                       max(v[2] for v in voxels) - mn[2] + 1,
                       max(v[1] for v in voxels) - mn[1] + 1)
    rgba = b''
    for i in range(255):
        src = i % band if banded else i
        r, g, b = palette[src] if src < len(palette) else (0, 0, 0)
        rgba += struct.pack('<4B', r, g, b, 255)
    rgba += struct.pack('<4B', 0, 0, 0, 255)

    def chunk(cid, content, children=b''):
        return cid + struct.pack('<2i', len(content), len(children)) + content + children

    body = chunk(b'SIZE', size) \
        + chunk(b'XYZI', struct.pack('<i', len(voxels)) + xyzi) \
        + chunk(b'RGBA', rgba)
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with open(path, 'wb') as f:
        f.write(b'VOX ' + struct.pack('<i', 150) + chunk(b'MAIN', b'', body))


class Rig:
    """Voxel painter that records the body part of every voxel plus joint
    pivots / hand anchor for the rig manifest."""

    def __init__(self, parts=PARTS, band=BAND):
        self.v = {}      # (x,y,z) -> (color0, part_idx)
        self.pal = []
        self.ci = {}
        self.pivots = {}
        self.hand = None
        self.parts = parts
        self.band = band
        self.v2 = None   # v2 rigs emit their own manifest (see m_player)

    def C(self, rgb):
        if rgb not in self.ci:
            assert len(self.pal) < self.band, 'palette band overflow'
            self.pal.append(rgb)
            self.ci[rgb] = len(self.pal) - 1
        return self.ci[rgb]

    def box(self, x0, x1, y0, y1, z0, z1, rgb, part='torso'):
        c = self.C(rgb)
        p = self.parts.index(part)
        for x in range(x0, x1 + 1):
            for y in range(y0, y1 + 1):
                for z in range(z0, z1 + 1):
                    self.v[(x, y, z)] = (c, p)

    def dot(self, x, y, z, rgb, part='torso'):
        self.v[(x, y, z)] = (self.C(rgb), self.parts.index(part))

    def mirror_box(self, x0, x1, y0, y1, z0, z1, rgb, parts=('torso', 'torso')):
        self.box(x0, x1, y0, y1, z0, z1, rgb, parts[0])
        self.box(-x1, -x0, y0, y1, z0, z1, rgb, parts[1])

    # Centering transform identical to voxel.js prepModel: x/z to bounds
    # midpoint, y to ground.
    def center_of(self, pt):
        xs = [k[0] for k in self.v]; ys = [k[1] for k in self.v]; zs = [k[2] for k in self.v]
        cx = (min(xs) + max(xs)) / 2; cz = (min(zs) + max(zs)) / 2; my = min(ys)
        return [pt[0] - cx, pt[1] - my, pt[2] - cz]

    def rig_json(self):
        if self.v2:
            return self.v2()
        out = {'pivots': {k: self.center_of(p) for k, p in self.pivots.items()}}
        if self.hand:
            out['hand'] = self.center_of(self.hand)
        return out


ARMS = ('rArm', 'lArm')
LEGS = ('rLeg', 'lLeg')


# ── Shared humanoid rig (≈36 tall, adult ~1:5.5 head ratio) ────────────────
# Front faces +z. Right hand = +x (holds weapon).
def humanoid(r, skin, legs_c, boots_c, torso_c, arms_c, hands_c=None,
             height=36, broad=0):
    b = broad  # extra shoulder/torso width
    # legs (y 0..13): thighs+shins with a knee step
    r.mirror_box(1, 3, 4, 13, -2, 1, legs_c, LEGS)
    r.mirror_box(1, 3, 0, 3, -2, 2, boots_c, LEGS)      # boots (toe forward)
    # hips/belt y14-15
    r.box(-4 - b, 4 + b, 14, 15, -2, 2, tuple(int(c * 0.75) for c in torso_c))
    # torso y16-25
    r.box(-4 - b, 4 + b, 16, 25, -2, 2, torso_c)
    # shoulders y24-26
    r.mirror_box(5 + b, 6 + b, 23, 26, -2, 2, arms_c, ARMS)
    # arms y16-23 hanging, hands y14-15
    r.mirror_box(5 + b, 6 + b, 16, 22, -1, 1, arms_c, ARMS)
    r.mirror_box(5 + b, 6 + b, 14, 15, -1, 1, hands_c or skin, ARMS)
    # neck y26
    r.box(-1, 1, 26, 26, -1, 1, skin, 'head')
    # head y27..33 (7 tall incl. jaw), 7 wide, front face +z
    r.box(-3, 3, 27, 33, -3, 2, skin, 'head')
    # joints + hand anchor
    r.pivots = {
        'rShoulder': (5.5 + b, 24, 0), 'lShoulder': (-(5.5 + b), 24, 0),
        'rHip': (2, 14, 0), 'lHip': (-2, 14, 0), 'neck': (0, 26.5, 0),
    }
    r.hand = (5.5 + b, 14.5, 0)
    return 33  # head top y


def eyes(r, y=31, dx=1, color=(20, 16, 14), z=3):
    r.dot(-dx - 1, y, z - 1, color, 'head')
    r.dot(dx + 1, y, z - 1, color, 'head')
    r.dot(-dx - 1, y, 2, color, 'head')
    r.dot(dx + 1, y, 2, color, 'head')


def sword(r, x, y0, hilt=(60, 44, 28), steel=(200, 205, 215), l=14, part='rArm'):
    # held clear of the body: 2-deep so it survives all view angles
    r.box(x, x, y0, y0 + 1, 0, 1, hilt, part)
    r.box(x, x, y0 + 2, y0 + 2 + l, 0, 1, steel, part)
    r.box(x - 1, x + 1, y0 + 2, y0 + 2, 0, 1, hilt, part)   # crossguard
    r.dot(x, y0 + 3 + l, 0, steel, part)                     # tip


def axe(r, x, y0, handle=(92, 64, 34), steel=(190, 195, 205), l=12, part='rArm'):
    r.box(x, x, y0, y0 + l, 0, 1, handle, part)
    r.box(x - 2, x + 2, y0 + l - 4, y0 + l - 1, 0, 1, steel, part)   # broad head
    r.box(x - 3, x + 3, y0 + l - 3, y0 + l - 2, 0, 1, steel, part)   # flared edge


def shield_round(r, x, cy, rad, face, rim):
    for y in range(cy - rad, cy + rad + 1):
        for z in range(-rad, rad + 1):
            d = math.hypot(y - cy, z)
            if d <= rad:
                r.box(x, x, y, y, z, z, rim if d > rad - 1.3 else face, 'lArm')


SKIN = (222, 178, 138)
SKIN_TAN = (196, 148, 104)
SKIN_GREEN = (110, 158, 66)
DARK = (24, 20, 18)


# ── HD player (v2 hierarchical rig, ~56 tall) ──────────────────────────────
# Anime-asset level of detail: visible face with 2×2 eyes, layered silver
# spike hair, steel cuirass with highlight/seam shading, bare muscled arms
# with leather bracers, belted red trousers tucked into boots. Limbs split
# at elbow / knee / ankle so voxel.js can chain FK rotations and run leg IK.
def m_player():
    # OSRS-default look: short brown hair, plain t-shirt, green trousers,
    # brown boots, bare arms. No armor, no markings.
    r = Rig(parts=PARTS_V2, band=BAND_V2)
    SK = (222, 178, 138); SK_D = (184, 138, 98)
    HAIR = (106, 72, 40); HAIR_D = (76, 50, 28)
    SHIRT = (198, 189, 167); SHIRT_D = (162, 152, 130)
    GOLD = (206, 172, 86); LEATH = (94, 70, 46); LEATH_D = (62, 46, 32)
    PANTS = (74, 108, 56); PANTS_D = (54, 82, 42)
    BOOT = (52, 44, 38); WHITE = (236, 236, 232)

    RA, LA = ('rArmU', 'lArmU'), ('rArmL', 'lArmL')
    RT, RS, RF = ('rThigh', 'lThigh'), ('rShin', 'lShin'), ('rFoot', 'lFoot')

    # feet y0-2: boots with dark soles and a toe cap
    r.mirror_box(1, 4, 0, 0, -2, 3, LEATH_D, RF)          # sole
    r.mirror_box(1, 4, 1, 2, -2, 2, BOOT, RF)             # boot body
    r.mirror_box(1, 4, 1, 1, 3, 3, BOOT, RF)              # toe cap
    # shins y3-11: boot shaft, then trouser tucked in
    r.mirror_box(1, 4, 3, 5, -1, 2, BOOT, RS)
    r.mirror_box(1, 4, 5, 5, 2, 2, LEATH_D, RS)           # shaft rim
    r.mirror_box(1, 3, 6, 11, -1, 1, PANTS, RS)
    r.mirror_box(2, 3, 7, 9, -2, -2, PANTS_D, RS)         # calf shadow
    # thighs y12-21: green trousers with fold shading
    r.mirror_box(1, 4, 12, 21, -2, 2, PANTS, RT)
    r.mirror_box(4, 4, 16, 18, -1, 2, PANTS_D, RT)        # outer fold
    r.mirror_box(1, 4, 20, 21, 2, 2, PANTS_D, RT)         # hip crease
    # pelvis y22-25 (torso root): trouser waist + simple belt
    r.box(-4, 4, 22, 23, -2, 2, PANTS_D)
    r.box(-4, 4, 24, 25, -2, 2, LEATH)
    r.box(-1, 1, 24, 25, 2, 2, GOLD)                      # buckle
    # torso y26-38: plain t-shirt with soft shading
    r.box(-4, 4, 26, 29, -2, 2, SHIRT)
    r.box(-4, 4, 26, 29, -2, -2, SHIRT_D)                 # lower-back shade
    r.box(-5, 5, 30, 36, -3, 3, SHIRT)
    r.box(-5, 5, 30, 36, -3, -3, SHIRT_D)                 # back shade
    r.box(-5, -5, 30, 36, -3, 3, SHIRT_D)                 # side shade
    r.box(0, 0, 26, 35, 3, 3, SHIRT_D)                    # front seam
    r.box(-2, 2, 36, 36, -2, 3, SHIRT_D)                  # collar
    r.box(-1, 1, 37, 38, -1, 2, SK)                       # neck opening
    # t-shirt sleeves + bare upper arms y28-37
    r.mirror_box(5, 8, 34, 37, -2, 2, SHIRT, RA)          # short sleeve
    r.mirror_box(5, 8, 34, 34, -2, 2, SHIRT_D, RA)        # sleeve hem
    r.mirror_box(6, 7, 28, 33, -1, 1, SK, RA)             # bare arm
    r.mirror_box(6, 7, 28, 31, -1, -1, SK_D, RA)          # tricep shade
    # forearms + hands y20-27: bare arms, bare fists
    r.mirror_box(6, 7, 22, 27, -1, 1, SK, LA)
    r.mirror_box(6, 7, 22, 25, -1, -1, SK_D, LA)          # forearm shade
    r.mirror_box(6, 7, 20, 21, -1, 1, SK, LA)             # hand
    r.mirror_box(6, 7, 21, 21, 1, 1, SK_D, LA)            # knuckle shade
    # neck y39
    r.box(-1, 1, 39, 39, -1, 1, SK)
    r.box(-1, 1, 39, 39, -1, -1, SK_D)
    # head y40-50: face on the +z plane
    r.box(-3, 3, 40, 50, -3, 3, SK, 'head')
    r.dot(0, 40, 3, SK_D, 'head')                         # chin shade
    r.box(-1, 0, 42, 42, 3, 3, DARK, 'head')              # mouth
    r.box(0, 0, 43, 44, 3, 3, SK_D, 'head')               # nose shadow
    r.dot(0, 44, 4, SK, 'head')                           # nose tip
    for sx in (1, -1):                                    # 2×2 eyes
        r.box(sx * 1, sx * 2, 45, 46, 3, 3, WHITE, 'head')
        r.box(sx * 1, sx * 1, 45, 46, 3, 3, DARK, 'head') # iris (inner)
        r.box(sx * 1, sx * 2, 47, 47, 3, 3, DARK, 'head') # brow
    r.mirror_box(4, 4, 44, 45, 0, 1, SK, ('head', 'head'))       # ears
    r.mirror_box(4, 4, 44, 44, 0, 0, SK_D, ('head', 'head'))
    # short hair: snug back/side shell + rounded cap + straight fringe line
    r.box(-4, 4, 44, 47, -4, -1, HAIR, 'head')            # sides/back
    r.box(-4, 4, 44, 45, -4, -4, HAIR_D, 'head')          # nape shade
    r.box(-4, 4, 48, 50, -4, 3, HAIR, 'head')             # cap
    r.box(-4, 4, 48, 48, -4, -1, HAIR_D, 'head')          # cap underside
    r.box(-3, 3, 51, 51, -3, 2, HAIR, 'head')             # crown
    r.box(-3, 3, 48, 48, 4, 4, HAIR_D, 'head')            # fringe line
    r.box(-4, 4, 49, 50, 4, 4, HAIR, 'head')              # brow-line fringe
    r.dot(-2, 43, 3, SK_D, 'head'); r.dot(2, 43, 3, SK_D, 'head')  # cheeks

    # joints (pre-centering coords; rig_json centers them)
    r.pivots = {
        'neck': (0, 39.5, 0),
        'rShoulder': (6.5, 34, 0), 'lShoulder': (-6.5, 34, 0),
        'rElbow': (6.5, 27.5, 0), 'lElbow': (-6.5, 27.5, 0),
        'rHip': (2.5, 22, 0), 'lHip': (-2.5, 22, 0),
        'rKnee': (2.5, 12, 0), 'lKnee': (-2.5, 12, 0),
        'rAnkle': (2.5, 3, 0), 'lAnkle': (-2.5, 3, 0),
    }
    r.hand = (6.5, 20.5, 0)

    PARENT = [None, 0, 0, 0, 0, 0, 2, 3, 4, 5, 8, 9]
    PIV = [None, 'neck', 'rShoulder', 'lShoulder', 'rHip', 'lHip',
           'rElbow', 'lElbow', 'rKnee', 'lKnee', 'rAnkle', 'lAnkle']
    r.v2 = lambda: {
        'band': BAND_V2,
        'handPart': PARTS_V2.index('rArmL'),
        'hand': r.center_of(r.hand),
        'parts': [{'parent': PARENT[i],
                   'pivot': r.center_of(r.pivots[PIV[i]]) if PIV[i] else None}
                  for i in range(len(PARTS_V2))],
        'ik': {'legs': [{'hip': 4, 'knee': 8, 'foot': 10},
                        {'hip': 5, 'knee': 9, 'foot': 11}]},
    }
    return r, 'player'


def m_goblin():
    r = Rig()
    cloth = (104, 78, 44)
    # short + hunched: scaled-down rig by hand
    r.mirror_box(1, 3, 3, 8, -2, 1, SKIN_GREEN, LEGS)
    r.mirror_box(1, 3, 0, 2, -2, 2, (70, 52, 30), LEGS)
    r.box(-4, 4, 9, 10, -2, 2, cloth)                          # loincloth belt
    r.box(-5, 5, 11, 17, -3, 2, SKIN_GREEN)                    # hunched torso
    r.mirror_box(6, 7, 9, 16, -2, 1, SKIN_GREEN, ARMS)         # long arms
    r.mirror_box(6, 7, 7, 8, -1, 1, (86, 128, 50), ARMS)
    r.box(-4, 4, 18, 23, -3, 3, SKIN_GREEN, 'head')            # big head, jutting
    r.mirror_box(5, 8, 20, 21, -1, 0, SKIN_GREEN, ('head', 'head'))  # pointy ears
    eyes(r, 21, 1, (200, 40, 30), 4)
    r.dot(-1, 19, 3, DARK, 'head'); r.dot(1, 19, 3, DARK, 'head')
    r.box(9, 9, 7, 17, 0, 1, (92, 64, 34), 'rArm')             # crude club
    r.box(8, 10, 14, 17, 0, 1, (120, 86, 48), 'rArm')
    r.pivots = {
        'rShoulder': (6.5, 16, 0), 'lShoulder': (-6.5, 16, 0),
        'rHip': (2, 9, 0), 'lHip': (-2, 9, 0), 'neck': (0, 17.5, 0),
    }
    r.hand = (6.5, 7.5, 0)
    return r, 'goblin'


def m_swashbuckler():
    r = Rig()
    shirt = (226, 220, 204); vest = (40, 38, 48); pants = (52, 56, 72)
    humanoid(r, SKIN, pants, (36, 30, 26), vest, shirt)
    r.box(-4, 4, 22, 25, -3, 3, vest)          # vest overlay wider
    r.box(-1, 1, 16, 21, 2, 2, shirt)          # shirt front
    r.box(-4, 4, 14, 15, -2, 2, (160, 34, 34)) # red sash
    # hair + tricorn-ish hat
    r.box(-3, 3, 33, 33, -3, 2, (58, 38, 24), 'head')
    r.box(-6, 6, 34, 34, -5, 4, (34, 30, 30), 'head')  # wide brim
    r.box(-3, 3, 35, 36, -3, 2, (34, 30, 30), 'head')  # crown
    r.box(-3, 3, 36, 36, -3, 2, (160, 34, 34), 'head') # red band on top edge
    eyes(r)
    r.dot(0, 29, 2, (58, 38, 24), 'head')      # goatee
    sword(r, 8, 14, steel=(210, 214, 222), l=13)
    return r, 'swashbuckler'


def m_barbarian():
    r = Rig()
    fur = (110, 74, 40); pants = (86, 62, 40)
    humanoid(r, SKIN_TAN, pants, (60, 44, 30), fur, SKIN_TAN, broad=1)
    r.box(-5, 5, 22, 25, -3, 3, fur)                     # fur vest
    r.box(-2, 2, 16, 21, 2, 2, SKIN_TAN)                 # bare chest gap
    # long hair + beard
    r.box(-4, 4, 33, 34, -3, 2, (150, 96, 40), 'head')
    r.box(-4, -4, 27, 32, -3, 1, (150, 96, 40), 'head')
    r.box(4, 4, 27, 32, -3, 1, (150, 96, 40), 'head')
    r.box(-2, 2, 27, 28, 2, 3, (150, 96, 40), 'head')    # beard
    eyes(r, 31)
    axe(r, 9, 12, l=16)
    return r, 'barbarian'


def m_desert_bandit():
    r = Rig()
    robe = (196, 176, 128); robe_d = (160, 140, 96)
    humanoid(r, SKIN_TAN, robe_d, (110, 88, 56), robe, robe)
    r.box(-5, 5, 4, 15, -3, 2, robe)                      # long robe skirt
    r.box(-4, 4, 27, 34, -4, 3, (232, 226, 208), 'head')  # keffiyeh wrap
    r.box(-3, 3, 29, 31, 3, 3, SKIN_TAN, 'head')          # eye band opening
    r.box(-3, 3, 27, 28, 3, 3, (70, 56, 40), 'head')      # face veil
    eyes(r, 30, 1, DARK, 4)
    r.box(-1, 1, 18, 20, 2, 2, (70, 56, 40))              # bandolier
    sword(r, 8, 14, steel=(180, 186, 170), l=11)          # scimitar
    return r, 'desert_bandit'


def m_gladiator():
    r = Rig()
    bronze = (168, 122, 62); bronze_d = (128, 92, 46); cloth = (150, 34, 34)
    humanoid(r, SKIN_TAN, cloth, (96, 70, 40), bronze, SKIN_TAN, broad=1)
    r.box(-5, 5, 4, 9, -3, 2, cloth)                       # battle skirt
    r.mirror_box(5, 7, 23, 26, -3, 3, bronze_d, ARMS)      # big pauldrons
    # crested helm
    r.box(-4, 4, 27, 34, -4, 2, bronze, 'head')
    r.box(-2, 2, 29, 31, 2, 3, SKIN_TAN, 'head')           # face opening
    eyes(r, 30, 1, DARK, 3)
    r.box(0, 0, 35, 38, -4, 2, cloth, 'head')              # red crest
    sword(r, 9, 14, l=12)
    shield_round(r, -9, 19, 5, bronze, bronze_d)
    return r, 'gladiator'


def m_corsair():
    r = Rig()
    coat = (36, 52, 88); coat_d = (26, 38, 66); shirt = (226, 220, 204)
    humanoid(r, SKIN, (44, 40, 36), (30, 26, 24), coat, coat)
    r.box(-5, 5, 8, 15, -3, 2, coat)                        # long coat skirt
    r.box(-1, 1, 16, 24, 2, 2, shirt)                       # shirt front
    r.box(-4, 4, 24, 25, -3, 3, coat_d)                     # collar
    r.box(-4, 4, 33, 35, -4, 3, (150, 34, 34), 'head')      # bandana
    r.box(4, 4, 28, 32, -4, -1, (150, 34, 34), 'head')      # bandana tail
    eyes(r)
    r.box(-2, 0, 30, 31, 2, 3, (20, 18, 16), 'head')        # eyepatch (left)
    sword(r, 8, 14, steel=(210, 214, 222), l=13)
    return r, 'corsair'


def m_berserker():
    r = Rig()
    pants = (70, 52, 38)
    humanoid(r, SKIN_TAN, pants, (48, 38, 28), SKIN_TAN, SKIN_TAN, broad=1)
    # war paint: three claw stripes across the chest
    for x in (-4, -1, 2):
        r.box(x, x + 1, 17, 24, 2, 2, (170, 40, 30))
    r.box(-6, 6, 25, 25, -3, 3, (96, 66, 40))               # harness strap
    # tall mohawk (2 wide so it reads at distance)
    r.box(-1, 0, 33, 38, -3, 2, (190, 50, 34), 'head')
    eyes(r, 31)
    r.box(-2, 2, 27, 28, 2, 3, (120, 70, 30), 'head')       # beard
    r.box(-1, 1, 30, 30, 2, 3, (170, 40, 30), 'head')       # face paint stripe
    axe(r, 9, 12, l=14)
    axe(r, -9, 12, l=14, part='lArm')
    return r, 'berserker'


def m_warlord():
    r = Rig()
    steel = (70, 74, 86); steel_d = (48, 52, 62); accent = (170, 40, 30)
    humanoid(r, SKIN_TAN, steel_d, (40, 42, 50), steel, steel, broad=2)
    r.mirror_box(6, 8, 23, 27, -3, 3, steel_d, ARMS)        # huge pauldrons
    r.mirror_box(7, 7, 28, 29, -1, 0, steel, ARMS)          # spikes
    r.box(-5, 5, 27, 35, -4, 2, steel_d, 'head')            # great helm
    r.mirror_box(5, 6, 33, 36, -1, 0, (210, 200, 180), ('head', 'head'))  # horns
    r.box(-2, 2, 29, 31, 2, 3, (14, 12, 12), 'head')        # dark face void
    eyes(r, 30, 1, (220, 60, 40), 3)                        # glowing red eyes
    r.box(-6, 6, 4, 26, -5, -4, accent)                     # war cape
    axe(r, 10, 11, steel=(150, 155, 165), l=18)             # great axe
    return r, 'warlord'


def m_champion():
    r = Rig()
    gold = (208, 168, 74); gold_d = (164, 128, 52); white = (228, 224, 212)
    humanoid(r, SKIN, gold_d, (120, 96, 44), gold, gold, broad=1)
    r.mirror_box(5, 7, 23, 26, -3, 3, gold_d, ARMS)         # pauldrons
    r.box(-4, 4, 27, 34, -4, 2, gold, 'head')               # full helm
    r.box(-2, 2, 30, 31, 2, 3, (14, 12, 12), 'head')        # visor slit
    r.box(0, 0, 34, 38, -4, 1, white, 'head')               # tall plume
    r.box(-4, 4, 20, 20, 2, 2, gold_d)
    r.box(-1, 1, 16, 24, 2, 2, white)                       # tabard stripe
    sword(r, 9, 14, steel=(226, 230, 238), l=15)
    shield_round(r, -9, 19, 5, gold, gold_d)
    return r, 'champion'


def m_maggot_king():
    r = Rig()
    pale = (216, 206, 176); pale_d = (186, 174, 142)     # maggot flesh
    rot = (134, 150, 58)                                 # weeping sickly green
    chitin = (104, 84, 52); bone = (228, 222, 198)
    humanoid(r, pale, pale_d, chitin, pale, pale, broad=2)
    # distended, segmented belly: alternating bulge rings proud of the torso
    for y0 in (16, 20, 24):
        r.box(-7, 7, y0, y0 + 1, -3, 3, pale_d)
    r.box(-2, 2, 16, 23, 3, 3, rot)                      # rot streak down the gut
    r.mirror_box(6, 8, 23, 26, -3, 3, pale_d, ARMS)      # engorged shoulders
    # pallid dome of a head with sunken green eyes and chitin mandibles
    r.box(-4, 4, 27, 34, -4, 3, pale, 'head')
    eyes(r, 31, dx=2, color=(46, 92, 30), z=4)
    r.mirror_box(1, 2, 27, 28, 3, 4, chitin, ('head', 'head'))
    # the crown: a chitin band ringed with jagged bone spikes
    r.box(-3, 3, 34, 34, -2, 1, chitin, 'head')
    for x in (-3, -1, 1, 3):
        r.box(x, x, 35, 37 if x in (-1, 1) else 36, -1, 0, bone, 'head')
    return r, 'maggot_king'


def m_rare_tourist():
    r = Rig()
    shirt = (240, 110, 140); shorts = (90, 140, 190)
    humanoid(r, SKIN, shorts, (230, 226, 214), shirt, shirt)
    r.mirror_box(5, 6, 16, 19, -1, 1, SKIN, ARMS)           # bare forearms
    for (x, y) in [(-3, 18), (0, 22), (3, 17), (-2, 24), (2, 21)]:
        r.dot(x, y, 2, (70, 180, 150))
    r.box(-4, 4, 6, 13, -2, 1, shorts)                      # baggy shorts
    r.box(-4, 4, 34, 35, -4, 3, (232, 214, 160), 'head')    # sun hat
    r.box(-5, 5, 34, 34, -5, 4, (232, 214, 160), 'head')
    eyes(r)
    r.box(-3, 3, 31, 31, 2, 3, (30, 30, 34), 'head')        # sunglasses
    r.box(5, 7, 15, 17, 0, 2, (40, 40, 44), 'rArm')         # camera in hand
    r.dot(6, 16, 3, (120, 180, 220), 'rArm')
    return r, 'rare_tourist'


def m_rare_gladiator():
    r = Rig()
    gold = (214, 178, 88); gold_d = (170, 136, 60); white = (238, 234, 224)
    humanoid(r, SKIN_TAN, white, (150, 120, 60), gold, SKIN_TAN, broad=1)
    r.box(-5, 5, 4, 9, -3, 2, white)                        # white battle skirt
    r.mirror_box(5, 7, 23, 26, -3, 3, gold_d, ARMS)
    r.box(-4, 4, 27, 34, -4, 2, gold, 'head')
    r.box(-2, 2, 29, 31, 2, 3, SKIN_TAN, 'head')
    eyes(r, 30, 1, DARK, 3)
    r.box(0, 0, 35, 38, -4, 2, white, 'head')               # white crest
    sword(r, 9, 14, steel=(230, 234, 240), l=12)
    shield_round(r, -9, 19, 5, gold, gold_d)
    return r, 'rare_gladiator'


# ── Weapons ─────────────────────────────────────────────────────────────────
# Local coords: grip at (0, 2, 0), blade rising along +y (rest pose = held
# vertical at the hand). 2 voxels deep so every view angle reads.

WOOD = (92, 64, 34)
HILT = (60, 44, 28)


class Wpn(Rig):
    def __init__(self):
        super().__init__()
        self.grip = (0, 2, 0)
        self.extra = {}   # extra rig fields (e.g. whip 'lash' physics config)

    def handle(self, y0=0, y1=4, rgb=HILT):
        self.box(0, 0, y0, y1, 0, 1, rgb)

    def rig_json(self):
        return {'grip': self.center_of(self.grip), **self.extra}


def blade_sword(steel, accent=None, l=13):
    w = Wpn(); w.handle()
    w.box(-1, 1, 5, 5, 0, 1, HILT)                                 # crossguard
    w.box(0, 0, 6, 6 + l, 0, 1, steel)
    if accent:
        w.dot(0, 6, 0, accent); w.dot(0, 8, 0, accent)             # runes
    w.dot(0, 7 + l, 0, steel)                                      # tip
    return w


def w_scimitar(steel):
    w = Wpn(); w.handle()
    w.box(0, 0, 5, 5, 0, 1, HILT)
    for i, x in enumerate([0, 0, 0, 1, 1, 2, 2, 3]):               # curved blade
        w.box(x, x + 1, 6 + i, 6 + i, 0, 1, steel)
    w.dot(4, 14, 0, steel)
    return w


def w_dagger(steel, jagged=False):
    # Reverse (karambit) grip: the fist wraps the handle and the blade
    # rakes DOWNWARD out of the pinky side, hooking slightly back — held
    # blade-down at rest, it rips down-forward in the stab.
    w = Wpn(); w.handle(0, 3)
    w.box(-1, 1, 4, 4, 0, 1, HILT)                                 # pommel cap
    w.box(-1, 1, -1, -1, 0, 1, HILT)                               # guard under the fist
    w.box(0, 0, -5, -2, 0, 1, steel)                               # blade
    w.box(0, 0, -7, -6, 0, 0, steel)                               # taper
    w.dot(0, -8, -1, steel)                                        # hooked tip
    if jagged:
        w.dot(1, -3, 0, steel); w.dot(-1, -5, 0, steel)            # barbs
    return w


def w_rapier(steel):
    w = Wpn(); w.handle(0, 3)
    w.box(-1, 1, 4, 5, 0, 1, (120, 100, 60))                       # cup guard
    w.box(0, 0, 6, 19, 0, 0, steel)                                # thin blade
    w.dot(0, 20, 0, steel)
    return w


def w_whip(color, dark):
    # Handle plus a coiled lash. The coil is icon dressing only: at attach
    # time the runtime keeps just the handle column and replaces the lash
    # with a physics rope (verlet chain) driven by the 'lash' config below.
    w = Wpn(); w.handle(0, 3, dark)                 # fist-sized handle
    w.dot(0, 1, 0, color); w.dot(0, 3, 0, color)    # grip wraps
    for a in range(22):                             # coil hanging off the grip
        ang = a / 22 * 6.2832
        x = round(4.6 + math.cos(ang) * 2.6)
        y = round(3.0 + math.sin(ang) * 2.6)
        for z in (0, 1):
            w.dot(x, y, z, color if a % 4 else dark)
    w.dot(2, 4, 0, color)                           # lash leaving the handle
    w.extra = {'lash': {
        # rope exits just BELOW the fist (top: -2 relative to the grip) so
        # the hanging lash reads as connected to the hand, not the hip.
        # len/g/damp/power/taper: tuned live in the anim editor for a snappy,
        # weighty forward crack (see get/setLashDebug + updateLash in voxel.js).
        'len': 0.97, 'segs': 12, 'top': -2,
        'g': 37, 'damp': 0.904, 'power': 2.1, 'taper': 3.3,
        'color': '#%02x%02x%02x' % color, 'dark': '#%02x%02x%02x' % dark,
    }}
    return w


def w_maul(head_c, head_d, size=3, l=13):
    w = Wpn(); w.handle(0, l, WOOD)
    w.box(-size, size, l + 1, l + 5, -1, 2, head_c)                # massive head
    w.box(-size, size, l + 2, l + 4, -1, 2, head_d)
    return w


def w_bludgeon():
    w = Wpn(); w.handle(0, 6, (54, 48, 58))
    w.box(-1, 1, 7, 14, 0, 1, (76, 66, 84))                        # club body
    for y in (8, 10, 12):                                          # spikes
        w.dot(2, y, 0, (170, 160, 180)); w.dot(-2, y, 0, (170, 160, 180))
    w.dot(0, 15, 0, (170, 160, 180))
    return w


def w_hasta(shaft, steel):
    w = Wpn()
    w.box(0, 0, 0, 19, 0, 0, shaft)                                # long shaft
    w.box(0, 0, 16, 18, 0, 1, (150, 34, 34))                       # binding
    w.box(0, 0, 20, 23, 0, 1, steel)                               # leaf point
    w.dot(0, 24, 0, steel)
    w.grip = (0, 6, 0)
    return w


def w_claws(steel):
    w = Wpn()
    w.box(-1, 1, 2, 4, 0, 1, HILT)                                 # bracket
    for x in (-1, 0, 1):
        w.box(x, x, 5, 10 + (1 if x == 0 else 0), 0, 0, steel)     # three blades
    return w


def w_scythe():
    w = Wpn()
    w.box(0, 0, 0, 21, 0, 1, (70, 62, 72))                         # dark shaft
    for i, x in enumerate([1, 2, 3, 4, 5, 6]):                     # sweeping blade
        w.box(x, x, 21 - min(i, 2), 22, 0, 1, (196, 200, 210))
    w.box(1, 6, 22, 22, 0, 1, (150, 34, 34))                       # red spine
    w.grip = (0, 8, 0)
    return w


def w_godsword(steel, accent):
    w = Wpn(); w.handle(0, 4)
    w.box(-2, 2, 5, 6, 0, 1, (120, 100, 60))                       # huge guard
    w.box(-1, 1, 7, 22, 0, 1, steel)                               # broad blade
    w.box(0, 0, 7, 22, 0, 1, accent)                               # core stripe
    w.box(0, 0, 23, 24, 0, 1, steel)                               # tip
    return w


# ── Food (held during the eat animation; doubles as bag/shop icon) ─────────

def f_shark():
    r = Rig()
    body = (108, 128, 148); belly = (218, 222, 228); fin = (78, 96, 114)
    r.box(1, 8, 1, 4, 0, 1, body)                   # body
    r.box(2, 7, 1, 1, 0, 1, belly)                  # belly line
    r.box(0, 0, 1, 5, 0, 0, fin); r.dot(0, 0, 0, fin)   # tail fork
    r.box(4, 5, 5, 6, 0, 0, fin)                    # dorsal fin
    r.dot(8, 3, 1, (20, 22, 26))                    # eye
    return r, 'shark'


def f_karambwan():
    r = Rig()
    body = (168, 96, 44); dark = (120, 62, 28)
    for x0, x1, y in [(2, 6, 1), (1, 7, 2), (1, 7, 3), (2, 6, 4)]:
        r.box(x0, x1, y, y, 0, 1, body)             # rounded blob
    r.box(2, 6, 4, 4, 0, 1, dark)                   # top shade
    for x in (1, 3, 5, 7):                          # trailing tentacle nubs
        r.dot(x, 0, 0, dark)
    r.dot(2, 3, 1, (20, 22, 26)); r.dot(5, 3, 1, (20, 22, 26))  # eyes
    return r, 'karambwan'


def f_anglerfish():
    r = Rig()
    body = (150, 158, 108); belly = (196, 200, 160); fin = (104, 112, 72)
    r.box(1, 7, 1, 4, 0, 1, body)
    r.box(2, 6, 1, 1, 0, 1, belly)
    r.box(0, 0, 1, 4, 0, 0, fin)                    # tail
    r.dot(7, 5, 0, fin); r.dot(8, 6, 0, (255, 220, 120))  # lure stalk + glow
    r.dot(7, 3, 1, (20, 22, 26))                    # eye
    return r, 'anglerfish'


FOODS = [f_shark, f_karambwan, f_anglerfish]

WEAPONS = {
    'rune_scimitar':      lambda: w_scimitar((96, 140, 170)),
    'dragon_scimitar':    lambda: w_scimitar((170, 48, 48)),
    'dragon_dagger':      lambda: w_dagger((170, 48, 48)),
    'venomous_fang':      lambda: w_dagger((214, 208, 190), jagged=True),
    'ghrazi_rapier':      lambda: w_rapier((215, 220, 228)),
    'abyssal_whip':       lambda: w_whip((140, 90, 50), (75, 45, 22)),   # aged leather
    'corrupted_whip':     lambda: w_whip((120, 100, 55), (60, 48, 24)), # olive-tinted leather
    'armadyl_sword':      lambda: blade_sword((215, 220, 228), (238, 210, 120), l=14),
    'bandos_godsword':    lambda: w_godsword((205, 210, 218), (150, 130, 60)),
    'zamorak_godsword':   lambda: w_godsword((205, 210, 218), (180, 44, 40)),
    'saradomin_godsword': lambda: w_godsword((205, 210, 218), (70, 110, 190)),
    'armadyl_godsword':   lambda: w_godsword((205, 210, 218), (230, 200, 90)),
    'dragon_claws':       lambda: w_claws((170, 48, 48)),
    'granite_maul':       lambda: w_maul((120, 118, 114), (90, 88, 86)),
    'elder_maul':         lambda: w_maul((70, 60, 80), (140, 90, 50), size=4, l=14),
    'abyssal_bludgeon':   w_bludgeon,
    'zamorakian_hasta':   lambda: w_hasta((120, 40, 40), (200, 205, 215)),
    'scythe_of_vitur':    w_scythe,
}

# ── Scene props (field scene set dressing; unbanded, unrigged) ─────────────

def disc(r, cx, cy, cz, rad, rgb, squash=1.0):
    for x in range(cx - rad, cx + rad + 1):
        for z in range(cz - rad, cz + rad + 1):
            if math.hypot(x - cx, (z - cz) * squash) <= rad + 0.3:
                r.dot(x, cy, z, rgb)


def p_tree():
    r = Rig()
    bark = (96, 68, 40); bark_d = (74, 52, 30)
    leaf = (64, 112, 48); leaf_d = (48, 88, 38); leaf_l = (86, 134, 58)
    r.box(-1, 1, 0, 11, -1, 1, bark)
    r.box(0, 0, 0, 11, 0, 0, bark_d)               # core shade line
    r.box(-3, -2, 0, 1, -1, 0, bark_d)              # root flares
    r.box(2, 3, 0, 1, 0, 1, bark_d)
    # canopy: stacked discs, widest mid, lighter crown
    for y, rad, c in [(9, 4, leaf_d), (10, 5, leaf), (11, 6, leaf), (12, 6, leaf),
                      (13, 5, leaf), (14, 4, leaf_l), (15, 3, leaf_l), (16, 2, leaf_l)]:
        disc(r, 0, y, 0, rad, c)
    return r, 'tree'


def p_rock():
    r = Rig()
    gray = (126, 124, 120); gray_d = (96, 94, 92); moss = (80, 110, 60)
    r.box(-3, 3, 0, 1, -2, 2, gray)
    r.box(-2, 2, 2, 3, -1, 2, gray)
    r.box(-1, 1, 4, 4, 0, 1, gray_d)
    r.box(-3, -1, 0, 2, 2, 2, gray_d)               # shaded face
    r.box(0, 2, 4, 4, -1, 0, moss)                  # moss cap
    return r, 'rock'


def p_bush():
    r = Rig()
    leaf = (70, 118, 52); leaf_d = (52, 92, 40)
    for y, rad in [(0, 3), (1, 3), (2, 2), (3, 1)]:
        disc(r, 0, y, 0, rad, leaf if y % 2 else leaf_d)
    return r, 'bush'


PROPS = [p_tree, p_rock, p_bush]

MODELS = [m_player, m_goblin, m_swashbuckler, m_barbarian, m_desert_bandit,
          m_gladiator, m_corsair, m_berserker, m_warlord, m_champion,
          m_maggot_king, m_rare_tourist, m_rare_gladiator]

if __name__ == '__main__':
    rigs = {'characters': {}, 'weapons': {}}

    for fn in MODELS:
        r, name = fn()
        path = os.path.join(ASSETS, name + '.vox') if name == 'player' \
            else os.path.join(ASSETS, 'npcs', name + '.vox')
        write_vox(path, r.v, r.pal, banded=True, band=r.band)
        rigs['characters'][name] = r.rig_json()
        ys = [k[1] for k in r.v]
        print(f'{name:16s} {len(r.v):5d} voxels, height {max(ys) - min(ys) + 1}')

    for wid, builder in WEAPONS.items():
        w = builder()
        write_vox(os.path.join(ASSETS, 'items', wid + '.vox'), w.v, w.pal, banded=False)
        rigs['weapons'][wid] = w.rig_json()
        print(f'  item {wid:20s} {len(w.v):4d} voxels')

    for fn in FOODS:
        r, name = fn()
        write_vox(os.path.join(ASSETS, 'items', name + '.vox'), r.v, r.pal, banded=False)
        print(f'  food {name:20s} {len(r.v):4d} voxels')

    for fn in PROPS:
        r, name = fn()
        write_vox(os.path.join(ASSETS, 'props', name + '.vox'), r.v, r.pal, banded=False)
        print(f'  prop {name:20s} {len(r.v):4d} voxels')

    with open(os.path.join(ASSETS, 'rigs.json'), 'w') as f:
        json.dump(rigs, f, indent=1)
    print('rigs.json written')
