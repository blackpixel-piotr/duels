#!/usr/bin/env python3
"""Generate OSRS/RS3-style adult-proportioned voxel characters as .vox files.

Renderer coords: x right, y up (ground 0), z front (toward camera at angle 0).
MagicaVoxel file mapping (matches voxel.js parseVox): file x=x, y=z, z=y.
"""
import struct, os, math

ASSETS = os.path.join(os.path.dirname(__file__), '..', 'src', 'Duels.Web', 'wwwroot', 'assets')


def write_vox(path, voxels, palette):
    """voxels: {(x,y,z): ci} renderer coords, ci 1-based into palette list."""
    mn = [min(v[i] for v in voxels) for i in range(3)]
    xyzi = b''
    for (x, y, z), ci in voxels.items():
        fx, fy, fz = x - mn[0], z - mn[2], y - mn[1]
        assert 0 <= fx < 256 and 0 <= fy < 256 and 0 <= fz < 256
        xyzi += struct.pack('<4B', fx, fy, fz, ci)
    size = struct.pack('<3i', max(v[0] for v in voxels) - mn[0] + 1,
                       max(v[2] for v in voxels) - mn[2] + 1,
                       max(v[1] for v in voxels) - mn[1] + 1)
    rgba = b''
    for i in range(255):
        r, g, b = palette[i] if i < len(palette) else (0, 0, 0)
        rgba += struct.pack('<4B', r, g, b, 255)
    rgba += struct.pack('<4B', 0, 0, 0, 255)

    def chunk(cid, content, children=b''):
        return cid + struct.pack('<2i', len(content), len(children)) + content + children

    body = chunk(b'SIZE', size) \
        + chunk(b'XYZI', struct.pack('<i', len(voxels)) + xyzi) \
        + chunk(b'RGBA', rgba)
    with open(path, 'wb') as f:
        f.write(b'VOX ' + struct.pack('<i', 150) + chunk(b'MAIN', b'', body))


class Rig:
    def __init__(self):
        self.v = {}
        self.pal = []
        self.ci = {}

    def C(self, rgb):
        if rgb not in self.ci:
            self.pal.append(rgb)
            self.ci[rgb] = len(self.pal)
        return self.ci[rgb]

    def box(self, x0, x1, y0, y1, z0, z1, rgb):
        c = self.C(rgb)
        for x in range(x0, x1 + 1):
            for y in range(y0, y1 + 1):
                for z in range(z0, z1 + 1):
                    self.v[(x, y, z)] = c

    def dot(self, x, y, z, rgb):
        self.v[(x, y, z)] = self.C(rgb)

    def mirror_box(self, x0, x1, y0, y1, z0, z1, rgb):
        self.box(x0, x1, y0, y1, z0, z1, rgb)
        self.box(-x1, -x0, y0, y1, z0, z1, rgb)


# ── Shared humanoid rig (≈36 tall, adult ~1:5.5 head ratio) ────────────────
# Front faces +z. Right hand = +x (holds weapon).
def humanoid(r, skin, legs_c, boots_c, torso_c, arms_c, hands_c=None,
             height=36, broad=0):
    b = broad  # extra shoulder/torso width
    # legs (y 0..13): thighs+shins with a knee step
    r.mirror_box(1, 3, 4, 13, -2, 1, legs_c)
    r.mirror_box(1, 3, 0, 3, -2, 2, boots_c)      # boots (toe forward)
    # hips/belt y14-15
    r.box(-4 - b, 4 + b, 14, 15, -2, 2, tuple(int(c * 0.75) for c in torso_c))
    # torso y16-25
    r.box(-4 - b, 4 + b, 16, 25, -2, 2, torso_c)
    # shoulders y24-26
    r.mirror_box(5 + b, 6 + b, 23, 26, -2, 2, arms_c)
    # arms y16-23 hanging, hands y14-15
    r.mirror_box(5 + b, 6 + b, 16, 22, -1, 1, arms_c)
    r.mirror_box(5 + b, 6 + b, 14, 15, -1, 1, hands_c or skin)
    # neck y26
    r.box(-1, 1, 26, 26, -1, 1, skin)
    # head y27..33 (7 tall incl. jaw), 7 wide, front face +z
    r.box(-3, 3, 27, 33, -3, 2, skin)
    return 33  # head top y


def eyes(r, y=31, dx=1, color=(20, 16, 14), z=3):
    r.dot(-dx - 1, y, z - 1, color)
    r.dot(dx + 1, y, z - 1, color)
    # place on front surface: overwrite face front voxels
    r.dot(-dx - 1, y, 2, color)
    r.dot(dx + 1, y, 2, color)


def sword(r, x, y0, blade=None, hilt=(60, 44, 28), steel=(200, 205, 215), l=14):
    # held clear of the body: 2-deep so it survives all view angles
    r.box(x, x, y0, y0 + 1, 0, 1, hilt)
    r.box(x, x, y0 + 2, y0 + 2 + l, 0, 1, steel)
    r.box(x - 1, x + 1, y0 + 2, y0 + 2, 0, 1, hilt)   # crossguard
    r.dot(x, y0 + 3 + l, 0, steel)                     # tip


def axe(r, x, y0, handle=(92, 64, 34), steel=(190, 195, 205), l=12):
    r.box(x, x, y0, y0 + l, 0, 1, handle)
    r.box(x - 2, x + 2, y0 + l - 4, y0 + l - 1, 0, 1, steel)   # broad head
    r.box(x - 3, x + 3, y0 + l - 3, y0 + l - 2, 0, 1, steel)   # flared edge


def shield_round(r, x, cy, rad, face, rim):
    for y in range(cy - rad, cy + rad + 1):
        for z in range(-rad, rad + 1):
            d = math.hypot(y - cy, z)
            if d <= rad:
                r.box(x, x, y, y, z, z, rim if d > rad - 1.3 else face)


SKIN = (222, 178, 138)
SKIN_TAN = (196, 148, 104)
SKIN_GREEN = (110, 158, 66)
DARK = (24, 20, 18)


def m_player():
    r = Rig()
    steel = (96, 128, 158); steel_d = (72, 98, 124); trim = (204, 170, 84)
    humanoid(r, SKIN, steel_d, (58, 52, 48), steel, steel, hands_c=(84, 72, 58))
    # full helm covering head, face slit open
    r.box(-4, 4, 27, 34, -4, 2, steel)
    r.box(-2, 2, 30, 31, 2, 3, SKIN)          # visor opening
    eyes(r, 30, 1, DARK, 3)
    r.box(-4, 4, 34, 34, -4, 1, steel_d)
    r.box(0, 0, 34, 36, -1, 0, trim)          # crest stub
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
    r.mirror_box(1, 3, 3, 8, -2, 1, SKIN_GREEN)
    r.mirror_box(1, 3, 0, 2, -2, 2, (70, 52, 30))
    r.box(-4, 4, 9, 10, -2, 2, cloth)                    # loincloth belt
    r.box(-5, 5, 11, 17, -3, 2, SKIN_GREEN)              # hunched torso
    r.mirror_box(6, 7, 9, 16, -2, 1, SKIN_GREEN)         # long arms
    r.mirror_box(6, 7, 7, 8, -1, 1, (86, 128, 50))
    r.box(-4, 4, 18, 23, -3, 3, SKIN_GREEN)              # big head, jutting
    r.mirror_box(5, 8, 20, 21, -1, 0, SKIN_GREEN)        # pointy ears
    eyes(r, 21, 1, (200, 40, 30), 4)
    r.dot(-1, 19, 3, DARK); r.dot(1, 19, 3, DARK)        # snaggle mouth
    r.box(9, 9, 7, 17, 0, 1, (92, 64, 34))               # crude club
    r.box(8, 10, 14, 17, 0, 1, (120, 86, 48))
    return r, 'goblin'


def m_swashbuckler():
    r = Rig()
    shirt = (226, 220, 204); vest = (40, 38, 48); pants = (52, 56, 72)
    humanoid(r, SKIN, pants, (36, 30, 26), vest, shirt)
    r.box(-4, 4, 22, 25, -3, 3, vest)          # vest overlay wider
    r.box(-1, 1, 16, 21, 2, 2, shirt)          # shirt front
    r.box(-4, 4, 14, 15, -2, 2, (160, 34, 34)) # red sash
    # hair + tricorn-ish hat
    r.box(-3, 3, 33, 33, -3, 2, (58, 38, 24))
    r.box(-6, 6, 34, 34, -5, 4, (34, 30, 30))  # wide brim
    r.box(-3, 3, 35, 36, -3, 2, (34, 30, 30))  # crown
    r.box(-3, 3, 36, 36, -3, 2, (160, 34, 34)) # red band on top edge
    eyes(r)
    r.dot(0, 29, 2, (58, 38, 24))              # goatee
    sword(r, 8, 14, steel=(210, 214, 222), l=13)
    return r, 'swashbuckler'


def m_barbarian():
    r = Rig()
    fur = (110, 74, 40); pants = (86, 62, 40)
    humanoid(r, SKIN_TAN, pants, (60, 44, 30), fur, SKIN_TAN, broad=1)
    r.box(-5, 5, 22, 25, -3, 3, fur)               # fur vest
    r.box(-2, 2, 16, 21, 2, 2, SKIN_TAN)           # bare chest gap
    # long hair + beard
    r.box(-4, 4, 33, 34, -3, 2, (150, 96, 40))
    r.box(-4, -4, 27, 32, -3, 1, (150, 96, 40))
    r.box(4, 4, 27, 32, -3, 1, (150, 96, 40))
    r.box(-2, 2, 27, 28, 2, 3, (150, 96, 40))      # beard
    eyes(r, 31)
    axe(r, 9, 12, l=16)
    return r, 'barbarian'


def m_desert_bandit():
    r = Rig()
    robe = (196, 176, 128); robe_d = (160, 140, 96)
    humanoid(r, SKIN_TAN, robe_d, (110, 88, 56), robe, robe)
    r.box(-5, 5, 4, 15, -3, 2, robe)              # long robe skirt
    r.box(-4, 4, 27, 34, -4, 3, (232, 226, 208))  # keffiyeh wrap
    r.box(-3, 3, 29, 31, 3, 3, SKIN_TAN)          # eye band opening
    r.box(-3, 3, 27, 28, 3, 3, (70, 56, 40))      # face veil
    eyes(r, 30, 1, DARK, 4)
    r.box(-1, 1, 18, 20, 2, 2, (70, 56, 40))      # bandolier
    sword(r, 8, 14, steel=(180, 186, 170), l=11)  # scimitar
    return r, 'desert_bandit'


def m_gladiator():
    r = Rig()
    bronze = (168, 122, 62); bronze_d = (128, 92, 46); cloth = (150, 34, 34)
    humanoid(r, SKIN_TAN, cloth, (96, 70, 40), bronze, SKIN_TAN, broad=1)
    r.box(-5, 5, 4, 9, -3, 2, cloth)               # battle skirt
    r.mirror_box(5, 7, 23, 26, -3, 3, bronze_d)    # big pauldrons
    # crested helm
    r.box(-4, 4, 27, 34, -4, 2, bronze)
    r.box(-2, 2, 29, 31, 2, 3, SKIN_TAN)           # face opening
    eyes(r, 30, 1, DARK, 3)
    r.box(0, 0, 35, 38, -4, 2, cloth)              # red crest front-to-back
    sword(r, 9, 14, l=12)
    shield_round(r, -9, 19, 5, bronze, bronze_d)
    return r, 'gladiator'


def m_corsair():
    r = Rig()
    coat = (36, 52, 88); coat_d = (26, 38, 66); shirt = (226, 220, 204)
    humanoid(r, SKIN, (44, 40, 36), (30, 26, 24), coat, coat)
    r.box(-5, 5, 8, 15, -3, 2, coat)                # long coat skirt
    r.box(-1, 1, 16, 24, 2, 2, shirt)               # shirt front
    r.box(-4, 4, 24, 25, -3, 3, coat_d)             # collar
    r.box(-4, 4, 33, 35, -4, 3, (150, 34, 34))      # bandana
    r.box(4, 4, 28, 32, -4, -1, (150, 34, 34))      # bandana tail
    eyes(r)
    r.box(-2, 0, 30, 31, 2, 3, (20, 18, 16))        # eyepatch (left)
    sword(r, 8, 14, steel=(210, 214, 222), l=13)
    return r, 'corsair'


def m_berserker():
    r = Rig()
    pants = (70, 52, 38)
    humanoid(r, SKIN_TAN, pants, (48, 38, 28), SKIN_TAN, SKIN_TAN, broad=1)
    # war paint: three claw stripes across the chest
    for x in (-4, -1, 2):
        r.box(x, x + 1, 17, 24, 2, 2, (170, 40, 30))
    r.box(-6, 6, 25, 25, -3, 3, (96, 66, 40))       # leather harness strap
    # tall mohawk (2 wide so it reads at distance)
    r.box(-1, 0, 33, 38, -3, 2, (190, 50, 34))
    eyes(r, 31)
    r.box(-2, 2, 27, 28, 2, 3, (120, 70, 30))       # beard
    r.box(-1, 1, 30, 30, 2, 3, (170, 40, 30))       # face paint stripe
    axe(r, 9, 12, l=14)
    axe(r, -9, 12, l=14)
    return r, 'berserker'


def m_warlord():
    r = Rig()
    steel = (70, 74, 86); steel_d = (48, 52, 62); accent = (170, 40, 30)
    humanoid(r, SKIN_TAN, steel_d, (40, 42, 50), steel, steel, broad=2)
    r.mirror_box(6, 8, 23, 27, -3, 3, steel_d)      # huge pauldrons
    r.mirror_box(7, 7, 28, 29, -1, 0, steel)        # spikes
    r.box(-5, 5, 27, 35, -4, 2, steel_d)            # horned great helm
    r.mirror_box(5, 6, 33, 36, -1, 0, (210, 200, 180))  # horns
    r.box(-2, 2, 29, 31, 2, 3, (14, 12, 12))        # dark face void
    eyes(r, 30, 1, (220, 60, 40), 3)                # glowing red eyes
    r.box(-6, 6, 4, 26, -5, -4, accent)             # war cape
    axe(r, 10, 11, steel=(150, 155, 165), l=18)     # great axe
    return r, 'warlord'


def m_champion():
    r = Rig()
    gold = (208, 168, 74); gold_d = (164, 128, 52); white = (228, 224, 212)
    humanoid(r, SKIN, gold_d, (120, 96, 44), gold, gold, broad=1)
    r.mirror_box(5, 7, 23, 26, -3, 3, gold_d)       # pauldrons
    r.box(-4, 4, 27, 34, -4, 2, gold)               # full helm
    r.box(-2, 2, 30, 31, 2, 3, (14, 12, 12))        # visor slit
    r.box(0, 0, 34, 38, -4, 1, white)               # tall plume
    r.box(-4, 4, 20, 20, 2, 2, gold_d)
    r.box(-1, 1, 16, 24, 2, 2, white)               # tabard stripe
    sword(r, 9, 14, steel=(226, 230, 238), l=15)
    shield_round(r, -9, 19, 5, gold, gold_d)
    return r, 'champion'


def m_rare_tourist():
    r = Rig()
    shirt = (240, 110, 140); shorts = (90, 140, 190)
    humanoid(r, SKIN, shorts, (230, 226, 214), shirt, shirt)
    r.mirror_box(5, 6, 16, 19, -1, 1, SKIN)          # bare forearms
    # flower pattern on shirt
    for (x, y) in [(-3, 18), (0, 22), (3, 17), (-2, 24), (2, 21)]:
        r.dot(x, y, 2, (70, 180, 150))
    r.box(-4, 4, 6, 13, -2, 1, shorts)               # baggy shorts
    r.box(-4, 4, 34, 35, -4, 3, (232, 214, 160))     # sun hat
    r.box(-5, 5, 34, 34, -5, 4, (232, 214, 160))
    eyes(r)
    r.box(-3, 3, 31, 31, 2, 3, (30, 30, 34))         # sunglasses
    r.box(5, 7, 15, 17, 0, 2, (40, 40, 44))          # camera in hand
    r.dot(6, 16, 3, (120, 180, 220))
    return r, 'rare_tourist'


def m_rare_gladiator():
    r = Rig()
    gold = (214, 178, 88); gold_d = (170, 136, 60); white = (238, 234, 224)
    humanoid(r, SKIN_TAN, white, (150, 120, 60), gold, SKIN_TAN, broad=1)
    r.box(-5, 5, 4, 9, -3, 2, white)                 # white battle skirt
    r.mirror_box(5, 7, 23, 26, -3, 3, gold_d)
    r.box(-4, 4, 27, 34, -4, 2, gold)
    r.box(-2, 2, 29, 31, 2, 3, SKIN_TAN)
    eyes(r, 30, 1, DARK, 3)
    r.box(0, 0, 35, 38, -4, 2, white)                # white crest
    sword(r, 9, 14, steel=(230, 234, 240), l=12)
    shield_round(r, -9, 19, 5, gold, gold_d)
    return r, 'rare_gladiator'


MODELS = [m_player, m_goblin, m_swashbuckler, m_barbarian, m_desert_bandit,
          m_gladiator, m_corsair, m_berserker, m_warlord, m_champion,
          m_rare_tourist, m_rare_gladiator]

for fn in MODELS:
    r, name = fn()
    path = os.path.join(ASSETS, name + '.vox') if name == 'player' \
        else os.path.join(ASSETS, 'npcs', name + '.vox')
    write_vox(path, r.v, r.pal)
    ys = [k[1] for k in r.v]
    print(f'{name:16s} {len(r.v):5d} voxels, height {max(ys) - min(ys) + 1}')
