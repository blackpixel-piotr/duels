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


def write_vox(path, voxels, palette, banded):
    """voxels: {(x,y,z): (color0, part)} renderer coords; color0 is 0-based."""
    mn = [min(v[i] for v in voxels) for i in range(3)]
    xyzi = b''
    for (x, y, z), (c0, part) in voxels.items():
        fx, fy, fz = x - mn[0], z - mn[2], y - mn[1]
        assert 0 <= fx < 256 and 0 <= fy < 256 and 0 <= fz < 256
        ci = (part * BAND + c0 + 1) if banded else (c0 + 1)
        xyzi += struct.pack('<4B', fx, fy, fz, ci)
    size = struct.pack('<3i', max(v[0] for v in voxels) - mn[0] + 1,
                       max(v[2] for v in voxels) - mn[2] + 1,
                       max(v[1] for v in voxels) - mn[1] + 1)
    rgba = b''
    for i in range(255):
        src = i % BAND if banded else i
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

    def __init__(self):
        self.v = {}      # (x,y,z) -> (color0, part_idx)
        self.pal = []
        self.ci = {}
        self.pivots = {}
        self.hand = None

    def C(self, rgb):
        if rgb not in self.ci:
            assert len(self.pal) < BAND, 'palette band overflow'
            self.pal.append(rgb)
            self.ci[rgb] = len(self.pal) - 1
        return self.ci[rgb]

    def box(self, x0, x1, y0, y1, z0, z1, rgb, part='torso'):
        c = self.C(rgb)
        p = PARTS.index(part)
        for x in range(x0, x1 + 1):
            for y in range(y0, y1 + 1):
                for z in range(z0, z1 + 1):
                    self.v[(x, y, z)] = (c, p)

    def dot(self, x, y, z, rgb, part='torso'):
        self.v[(x, y, z)] = (self.C(rgb), PARTS.index(part))

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


def m_player():
    r = Rig()
    steel = (96, 128, 158); steel_d = (72, 98, 124); trim = (204, 170, 84)
    humanoid(r, SKIN, steel_d, (58, 52, 48), steel, steel, hands_c=(84, 72, 58))
    # full helm covering head, face slit open
    r.box(-4, 4, 27, 34, -4, 2, steel, 'head')
    r.box(-2, 2, 30, 31, 2, 3, SKIN, 'head')          # visor opening
    eyes(r, 30, 1, DARK, 3)
    r.box(-4, 4, 34, 34, -4, 1, steel_d, 'head')
    r.box(0, 0, 34, 36, -1, 0, trim, 'head')          # crest stub
    # chest trim + belt buckle
    r.box(-4, 4, 20, 20, 2, 2, steel_d)
    r.box(-1, 1, 14, 15, 2, 2, trim)
    # cape (back)
    r.box(-5, 5, 6, 25, -4, -3, (140, 34, 34))
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

    def handle(self, y0=0, y1=4, rgb=HILT):
        self.box(0, 0, y0, y1, 0, 1, rgb)

    def rig_json(self):
        return {'grip': self.center_of(self.grip)}


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
    w = Wpn(); w.handle(0, 3)
    w.box(-1, 1, 4, 4, 0, 1, HILT)
    w.box(0, 0, 5, 11, 0, 1, steel)
    if jagged:
        w.dot(1, 7, 0, steel); w.dot(-1, 9, 0, steel)              # barbs
    w.dot(0, 12, 0, steel)
    return w


def w_rapier(steel):
    w = Wpn(); w.handle(0, 3)
    w.box(-1, 1, 4, 5, 0, 1, (120, 100, 60))                       # cup guard
    w.box(0, 0, 6, 19, 0, 0, steel)                                # thin blade
    w.dot(0, 20, 0, steel)
    return w


def w_whip(color, dark):
    w = Wpn(); w.handle(0, 5, dark)
    # trailing lash: droops out and down from the handle top
    lash = [(1, 6), (2, 6), (3, 5), (4, 4), (5, 3), (5, 2), (6, 1), (7, 1), (8, 0)]
    for x, y in lash:
        w.box(x, x, y, y, 0, 1, color)
    w.dot(9, 0, 0, color)                                          # tip
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


WEAPONS = {
    'rune_scimitar':      lambda: w_scimitar((96, 140, 170)),
    'dragon_scimitar':    lambda: w_scimitar((170, 48, 48)),
    'dragon_dagger':      lambda: w_dagger((170, 48, 48)),
    'venomous_fang':      lambda: w_dagger((214, 208, 190), jagged=True),
    'ghrazi_rapier':      lambda: w_rapier((215, 220, 228)),
    'abyssal_whip':       lambda: w_whip((150, 70, 190), (60, 30, 80)),
    'corrupted_whip':     lambda: w_whip((80, 200, 120), (24, 60, 40)),
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

MODELS = [m_player, m_goblin, m_swashbuckler, m_barbarian, m_desert_bandit,
          m_gladiator, m_corsair, m_berserker, m_warlord, m_champion,
          m_rare_tourist, m_rare_gladiator]

if __name__ == '__main__':
    rigs = {'characters': {}, 'weapons': {}}

    for fn in MODELS:
        r, name = fn()
        path = os.path.join(ASSETS, name + '.vox') if name == 'player' \
            else os.path.join(ASSETS, 'npcs', name + '.vox')
        write_vox(path, r.v, r.pal, banded=True)
        rigs['characters'][name] = r.rig_json()
        ys = [k[1] for k in r.v]
        print(f'{name:16s} {len(r.v):5d} voxels, height {max(ys) - min(ys) + 1}')

    for wid, builder in WEAPONS.items():
        w = builder()
        write_vox(os.path.join(ASSETS, 'items', wid + '.vox'), w.v, w.pal, banded=False)
        rigs['weapons'][wid] = w.rig_json()
        print(f'  item {wid:20s} {len(w.v):4d} voxels')

    with open(os.path.join(ASSETS, 'rigs.json'), 'w') as f:
        json.dump(rigs, f, indent=1)
    print('rigs.json written')
