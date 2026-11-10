"""Build the concept's editable low-poly models and export Unity-ready FBX files.

Run with Blender 4.5 LTS --background --factory-startup --python this_file -- PROJECT_ROOT.
All construction is deterministic; the .blend master stays outside Unity's Assets directory.
"""

import math
import random
import sys
from pathlib import Path

import bmesh
import bpy
from mathutils import Quaternion, Vector

PROJECT = Path(sys.argv[sys.argv.index("--") + 1]).resolve()
EXPORT = PROJECT / "Assets/SnowballArena/Art/Concept"
SOURCE = PROJECT / "ArtSource"
EXPORT.mkdir(parents=True, exist_ok=True)
SOURCE.mkdir(parents=True, exist_ok=True)
RNG = random.Random(1109)
TAU = math.tau
SNOW = (0.91, 0.95, 1.0)
PACKED = (0.75, 0.85, 0.95)
ROCK = (0.20, 0.27, 0.35)
ICE = (0.43, 0.79, 0.98)
CORE = (0.21, 0.57, 0.78)
PINE = (0.045, 0.265, 0.25)
BARK = (0.27, 0.235, 0.205)

bpy.ops.object.select_all(action="SELECT")
bpy.ops.object.delete(use_global=False)
bpy.context.scene.unit_settings.system = "METRIC"
bpy.context.scene.unit_settings.scale_length = 1.0
bpy.context.preferences.filepaths.save_version = 0


def material(name, roughness, transmission=0.0):
    mat = bpy.data.materials.new(name)
    mat.use_nodes = True
    nodes = mat.node_tree.nodes
    shader = nodes.get("Principled BSDF")
    shader.inputs["Roughness"].default_value = roughness
    shader.inputs["IOR"].default_value = 1.31 if transmission else 1.45
    shader.inputs["Transmission Weight"].default_value = transmission
    color = nodes.new("ShaderNodeVertexColor")
    color.layer_name = "Col"
    mat.node_tree.links.new(color.outputs["Color"], shader.inputs["Base Color"])
    return mat


MATERIALS = {
    "Snow": material("Snow", 0.78),
    "PackedSnow": material("PackedSnow", 0.60),
    "Ice": material("Ice", 0.18, 0.35),
    "IceCore": material("IceCore", 0.32),
    "Rock": material("Rock", 0.88),
    "Foliage": material("Foliage", 0.85),
    "Bark": material("Bark", 0.87),
}


def color(base, factor=1.0):
    def linear(value):
        value = max(0.0, min(1.0, value * factor))
        return value / 12.92 if value <= 0.04045 else ((value + 0.055) / 1.055) ** 2.4
    return tuple(linear(value) for value in base) + (1.0,)


def radial(angle, radius=1.0, height=0.0):
    return Vector((math.cos(angle) * radius, math.sin(angle) * radius, height))


class Draft:
    def __init__(self, slots):
        self.slots = slots
        self.vertices = []
        self.faces = []
        self.materials = []
        self.colors = []

    def face(self, points, slot, tint, outward=None):
        points = [Vector(point) for point in points]
        if outward is not None:
            normal = (points[1] - points[0]).cross(points[2] - points[0])
            if normal.dot(Vector(outward)) < 0:
                points.reverse()
        first = len(self.vertices)
        self.vertices.extend(tuple(point) for point in points)
        self.faces.append(tuple(range(first, first + len(points))))
        self.materials.append(self.slots.index(slot))
        self.colors.append(tint)

    def tri(self, a, b, c, slot, tint, outward=None):
        self.face((a, b, c), slot, tint, outward)

    def quad(self, a, b, c, d, slot, tint, outward=None):
        self.tri(a, b, c, slot, tint, outward)
        self.tri(a, c, d, slot, tint, outward)

    def object(self, name):
        data = bpy.data.meshes.new(name)
        data.from_pydata(self.vertices, [], self.faces)
        data.update()
        obj = bpy.data.objects.new(name, data)
        bpy.context.collection.objects.link(obj)
        for slot in self.slots:
            data.materials.append(MATERIALS[slot])
        colors = data.color_attributes.new(name="Col", type="FLOAT_COLOR", domain="CORNER")
        for polygon, mat, tint in zip(data.polygons, self.materials, self.colors):
            polygon.material_index = mat
            polygon.use_smooth = False
            for loop in polygon.loop_indices:
                colors.data[loop].color = tint
        data.color_attributes.active_color = colors
        obj["source_reference"] = "docs/art/snow-arena-modeling-reference.png"
        obj["unity_material_order"] = ",".join(self.slots)
        return obj


ICO = {}


def ico(draft, center, scale, slot, tint, seed, subdivisions=1, rotation=None):
    if subdivisions not in ICO:
        mesh = bmesh.new()
        bmesh.ops.create_icosphere(mesh, subdivisions=subdivisions, radius=1)
        mesh.verts.ensure_lookup_table()
        mesh.verts.index_update()
        ICO[subdivisions] = ([v.co.copy() for v in mesh.verts], [tuple(v.index for v in f.verts) for f in mesh.faces])
        mesh.free()
    points, faces = ICO[subdivisions]
    rng = random.Random(seed)
    center, scale = Vector(center), Vector(scale)
    rotation = rotation or Quaternion((0, 0, 1), seed * 0.37)
    transformed = [center + rotation @ Vector((p.x * scale.x, p.y * scale.y, p.z * scale.z)) * rng.uniform(0.90, 1.10) for p in points]
    for face in faces:
        p = [transformed[i] for i in face]
        shade = rng.uniform(0.94, 1.055)
        draft.face(p, slot, color(tint, shade), sum(p, Vector()) / len(p) - center)


def connect(draft, upper, lower, slot, base, rng, variation=0.08):
    count = len(upper)
    for i in range(count):
        a, b, c, d = upper[i], upper[(i + 1) % count], lower[(i + 1) % count], lower[i]
        tint = color(base, rng.uniform(1 - variation, 1 + variation))
        draft.quad(a, b, c, d, slot, tint, (a.x + b.x, a.y + b.y, 0))


def sculpt_snow_mantle(draft):
    """Union the snow pillows into one continuous, faceted snow cap."""
    snow_slot = draft.slots.index("Snow")
    data = bpy.data.meshes.new("Snow mantle sculpt")
    snow_faces = [face for face, slot in zip(draft.faces, draft.materials) if slot == snow_slot]
    data.from_pydata(draft.vertices, [], snow_faces)
    data.update()
    obj = bpy.data.objects.new("Snow mantle sculpt", data)
    bpy.context.collection.objects.link(obj)
    bpy.ops.object.select_all(action="DESELECT")
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    remesh = obj.modifiers.new("Join soft snow masses", "REMESH")
    remesh.mode = "VOXEL"
    remesh.voxel_size = 0.17
    remesh.use_smooth_shade = False
    bpy.ops.object.modifier_apply(modifier=remesh.name)
    decimate = obj.modifiers.new("Broad modeling facets", "DECIMATE")
    decimate.ratio = min(1.0, 1600 / max(1, len(obj.data.polygons) * 2))
    decimate.use_collapse_triangulate = True
    bpy.ops.object.modifier_apply(modifier=decimate.name)
    kept = [(face, mat, tint) for face, mat, tint in zip(draft.faces, draft.materials, draft.colors) if mat != snow_slot]
    draft.faces = [face for face, _, _ in kept]
    draft.materials = [mat for _, mat, _ in kept]
    draft.colors = [tint for _, _, tint in kept]
    rng = random.Random(9831)
    for polygon in obj.data.polygons:
        points = []
        for index in polygon.vertices:
            p = obj.data.vertices[index].co.copy()
            if math.hypot(p.x, p.y) < 10.25 and p.z > -0.15:
                p.z = 0
            points.append(p)
        draft.face(points, "Snow", color(SNOW, rng.uniform(0.985, 1.015)), polygon.normal)
    bpy.data.objects.remove(obj, do_unlink=True)


def platform():
    rng = random.Random(22011)
    mesh = Draft(["Snow", "Ice", "Rock", "IceCore"])
    n = 72
    rim = []
    for i in range(n):
        angle = i * TAU / n
        r = 10.98 + 0.06 * math.sin(angle * 7) + rng.uniform(-0.055, 0.055)
        rim.append(radial(angle, r, 0))
    inner = [radial(i * TAU / n, 8.5, 0) for i in range(n)]
    for i in range(n):
        j = (i + 1) % n
        mesh.tri((0, 0, 0), inner[i], inner[j], "Snow", color(SNOW, rng.uniform(0.985, 1.015)), (0, 0, 1))
        mesh.quad(inner[i], rim[i], rim[j], inner[j], "Snow", color(SNOW, rng.uniform(0.975, 1.02)), (0, 0, 1))
    snow_bottom = [radial(i * TAU / n, 10.92 + rng.uniform(-0.12, 0.12), -0.95 + rng.uniform(-0.12, 0.10)) for i in range(n)]
    connect(mesh, rim, snow_bottom, "Snow", SNOW, rng, 0.035)
    for i in range(n):
        mesh.tri(snow_bottom[i], snow_bottom[(i + 1) % n], (0, 0, -0.96), "Snow", color(SNOW, 0.97), (0, 0, -1))
    core_top = [radial(i * TAU / n, 10.72, -0.90) for i in range(n)]
    core_bottom = [radial(i * TAU / n, 10.46 + rng.uniform(-0.10, 0.10), -2.10 + rng.uniform(-0.14, 0.14)) for i in range(n)]
    connect(mesh, core_top, core_bottom, "IceCore", CORE, rng, 0.11)
    for i in range(n):
        j = (i + 1) % n
        a = snow_bottom[i] + radial(i * TAU / n, 0.03)
        b = snow_bottom[j] + radial(j * TAU / n, 0.03)
        c = core_bottom[j] + radial(j * TAU / n, 0.19)
        d = core_bottom[i] + radial(i * TAU / n, 0.19)
        mid = (a + b + c + d) / 4 + radial((i + 0.5) * TAU / n, rng.uniform(0.04, 0.18))
        for p, q in ((a, b), (b, c), (c, d), (d, a)):
            mesh.tri(p, q, mid, "Ice", color(ICE, rng.uniform(0.9, 1.12)), radial((i + 0.5) * TAU / n))
    rock_n = 40
    rings = []
    for ring, (r, z, spread) in enumerate(((10.65, -1.98, 0.20), (10.45, -3.45, 0.40), (9.60, -5.0, 0.50), (8.35, -6.25, 0.50))):
        rings.append([radial((i + (ring % 2) * 0.48) * TAU / rock_n, r + rng.uniform(-spread, spread), z + rng.uniform(-0.18, 0.18)) for i in range(rock_n)])
    for a, b in zip(rings, rings[1:]):
        connect(mesh, a, b, "Rock", ROCK, rng, 0.18)
    for i in range(rock_n):
        mesh.tri(rings[-1][i], rings[-1][(i + 1) % rock_n], (0, 0, -6.4), "Rock", color(ROCK, 0.7), (0, 0, -1))
    for i in range(36):
        angle = (i + rng.uniform(-0.22, 0.22)) * TAU / 36
        center = radial(angle, 10.76 + rng.uniform(-0.12, 0.12), -0.40)
        ico(mesh, center, (rng.uniform(0.70, 1.05), rng.uniform(0.38, 0.58), rng.uniform(0.42, 0.49)), "Snow", SNOW, 500 + i, 2,
            Quaternion((0, 0, 1), angle + math.pi / 2))
    for i in range(38):
        angle = (i + rng.uniform(-0.25, 0.25)) * TAU / 38
        r = 10.79
        center = radial(angle, r, -1.38)
        width = rng.uniform(0.28, 0.60)
        length = rng.uniform(0.90, 1.65)
        crystal(mesh, center, angle, width, length, rng)
    sculpt_snow_mantle(mesh)
    return mesh.object("ConceptPlatform")


def crystal(mesh, center, angle, width, length, rng):
    ring = [center + radial(angle + j * TAU / 5, width * (0.75 if j % 2 else 1)) for j in range(5)]
    tip = center + Vector((math.cos(angle) * width * 0.18, math.sin(angle) * width * 0.18, -length))
    top = center + Vector((0, 0, width * 0.55))
    for j in range(5):
        outward = (ring[j] + ring[(j + 1) % 5]) / 2 - center
        mesh.tri(ring[j], ring[(j + 1) % 5], tip, "Ice", color(ICE, rng.uniform(0.90, 1.18)), outward)
        mesh.tri(ring[j], top, ring[(j + 1) % 5], "Ice", color(ICE, rng.uniform(0.95, 1.12)), outward)


def islet(variant, cliff=False):
    rng = random.Random(130 + variant + int(cliff) * 30)
    mesh = Draft(["Snow", "Ice", "Rock", "IceCore"])
    n = 13
    angles = [(i + rng.uniform(-0.12, 0.12)) * TAU / n for i in range(n)]
    radii = [rng.uniform(0.83, 1.13) for _ in range(n)]
    top = [radial(a, r, 0) for a, r in zip(angles, radii)]
    snow_bottom = [radial(a, r * 0.99, -0.16 + rng.uniform(-0.035, 0.025)) for a, r in zip(angles, radii)]
    ice_bottom = [radial(a, r * 0.96, -0.34) for a, r in zip(angles, radii)]
    for i in range(n):
        mesh.tri((0, 0, 0), top[i], top[(i + 1) % n], "Snow", color(SNOW, rng.uniform(0.97, 1.02)), (0, 0, 1))
    connect(mesh, top, snow_bottom, "Snow", SNOW, rng, 0.04)
    connect(mesh, snow_bottom, ice_bottom, "Ice", ICE, rng, 0.10)
    core_top = [Vector((p.x * 0.95, p.y * 0.95, p.z)) for p in snow_bottom]
    core_bottom = [Vector((p.x * 0.95, p.y * 0.95, p.z)) for p in ice_bottom]
    connect(mesh, core_top, core_bottom, "IceCore", CORE, rng, 0.10)
    depth = 4.3 if cliff else 1.9 + variant * 0.1
    prior = ice_bottom
    for ring, (fraction, scale) in enumerate(((0.28, 1.0), (0.61, 0.88), (0.88, 0.67), (1.0, 0.32 if not cliff else 0.56))):
        current = [radial(a + (0.12 if ring % 2 else -0.08), r * scale * rng.uniform(0.9, 1.1), -depth * fraction + rng.uniform(-0.12, 0.12)) for a, r in zip(angles, radii)]
        connect(mesh, prior, current, "Rock", ROCK, rng, 0.20)
        prior = current
    for i in range(n):
        mesh.tri(prior[i], prior[(i + 1) % n], (0, 0, -depth), "Rock", color(ROCK, 0.8), (0, 0, -1))
    for i in range(n):
        ico(mesh, radial(angles[i], radii[i] * 0.88, -0.065), (0.20, 0.18, 0.09), "Snow", SNOW, 1200 + i + variant * 20, 1)
    return mesh.object("ConceptCliff" if cliff else "ConceptIslet" + str(variant))


def cedar(variant):
    rng = random.Random(720 + variant)
    mesh = Draft(["Snow", "Foliage", "Bark"])
    n = 8
    lower = [radial(i * TAU / n, 0.12, 0) for i in range(n)]
    upper = [radial(i * TAU / n, 0.075, 1.3) for i in range(n)]
    connect(mesh, upper, lower, "Bark", BARK, rng)
    tiers = ((0.68, 1.16, 1.25), (1.40, 0.93, 1.15), (2.13, 0.68, 1.04), (2.83, 0.40, 0.98))
    for tier, (base, radius, height) in enumerate(tiers):
        phase = tier * 0.33 + variant * 0.24
        count = 7 if tier == 0 else 6
        # A continuous inner crown supports individual, snow-laden radial boughs.
        core_lower = [radial(i * TAU / 12 + phase, radius * 0.52, base + 0.36) for i in range(12)]
        core_upper = [radial(i * TAU / 12 + phase, radius * 0.26, base + height * 0.78) for i in range(12)]
        connect(mesh, core_upper, core_lower, "Snow", SNOW, rng, 0.035)
        tip = Vector((0.02, -0.01, base + height + 0.09))
        for i in range(12):
            mesh.tri(core_upper[i], core_upper[(i + 1) % 12], tip, "Snow", color(SNOW, rng.uniform(0.98, 1.03)), radial(i * TAU / 12 + phase) + Vector((0, 0, 1)))
        for branch in range(count):
            angle = branch * TAU / count + phase + rng.uniform(-0.055, 0.055)
            reach = radius * rng.uniform(0.94, 1.08)
            direction = Vector((math.cos(angle), math.sin(angle), 0))
            tangent = Vector((-math.sin(angle), math.cos(angle), 0))
            rows = (0.10, 0.32, 0.58, 0.82, 1.0)
            widths = (0.17, 0.27, 0.38, 0.40, 0.17)
            side = (-1, -0.52, 0, 0.52, 1)
            grid = []
            for row, t in enumerate(rows):
                center = direction * (reach * t * 0.82)
                center.z = base + height * (1 - t) ** 1.35 + 0.29 + rng.uniform(-0.015, 0.015)
                line = []
                for u in side:
                    p = center + tangent * (radius * widths[row] * u * 0.74)
                    p.z += (1 - u * u) * radius * 0.105
                    p.z -= max(0, t - 0.74) * radius * 0.20
                    line.append(p)
                grid.append(line)
            for row in range(4):
                for col in range(4):
                    mesh.quad(grid[row][col], grid[row + 1][col], grid[row + 1][col + 1], grid[row][col + 1],
                        "Snow", color(SNOW, rng.uniform(0.96, 1.035)), direction * 0.4 + Vector((0, 0, 1)))
            # Faceted thickness at the hanging edge and dark evergreen needles underneath.
            perimeter = [grid[row][0] for row in range(5)] + list(grid[-1][1:]) + [grid[row][-1] for row in range(3, -1, -1)]
            under = [Vector((p.x * 1.035, p.y * 1.035, p.z - 0.13 - radius * 0.025)) for p in perimeter]
            for i in range(len(perimeter)):
                j = (i + 1) % len(perimeter)
                mesh.quad(perimeter[i], perimeter[j], under[j], under[i], "Snow",
                    color(SNOW, rng.uniform(0.91, 0.98)), (perimeter[i] + perimeter[j]) / 2 - Vector((0, 0, base + 0.3)))
            green_tip = direction * (reach * 1.12)
            green_tip.z = base - 0.12
            green_left = direction * (reach * 0.52) - tangent * radius * 0.49
            green_right = direction * (reach * 0.52) + tangent * radius * 0.49
            green_left.z = green_right.z = base + 0.20
            green_root = Vector((0, 0, base + height * 0.88))
            mesh.tri(green_tip, green_left, green_root, "Foliage", color(PINE, rng.uniform(0.92, 1.08)), direction + Vector((0, 0, 1)))
            mesh.tri(green_tip, green_root, green_right, "Foliage", color(PINE, rng.uniform(0.92, 1.08)), direction + Vector((0, 0, 1)))
            mesh.tri(green_tip, green_right, green_left, "Foliage", color(PINE, 0.83), (0, 0, -1))
            green_edges = [green_tip, green_left, green_root, green_right]
            for edge in range(4):
                a, b = green_edges[edge], green_edges[(edge + 1) % 4]
                drop = Vector((0, 0, -0.12))
                mesh.quad(a, b, b + drop, a + drop, "Foliage", color(PINE, rng.uniform(0.85, 1.0)), direction)
    return mesh.object("ConceptCedar" + str(variant))


def mountain(variant):
    from mathutils.geometry import delaunay_2d_cdt
    rng = random.Random(4500 + variant)
    mesh = Draft(["Snow", "Rock"])
    peaks = [(-0.18 + variant * 0.06, 0.10, 1.12, 1.04),
             (0.49, 0.30, 0.91, 0.75), (-0.57, -0.23, 0.77, 0.72),
             (0.16, -0.58, 0.60, 0.59)]
    sites = []
    for y in range(25):
        for x in range(27):
            px, py = -1.30 + x * 2.60 / 26, -1.18 + y * 2.36 / 24
            sites.append(Vector((px + rng.uniform(-0.032, 0.032), py + rng.uniform(-0.032, 0.032))))
    sites.extend(Vector((x, y)) for x, y, _, _ in peaks)
    coords, _, faces, _, _, _ = delaunay_2d_cdt(sites, [], [], 0, 0.00001, False)
    def height(x, y):
        result = 0.0
        for peak_index, (cx, cy, h, width) in enumerate(peaks):
            dx, dy = (x - cx) * 0.97, (y - cy) * 1.10
            t = math.hypot(dx, dy) / width
            if t >= 1:
                continue
            angle = math.atan2(dy, dx)
            cone = h * (1 - t) ** 1.08
            ridge = h * 0.17 * math.sin(math.pi * t) * (1 - t) * math.cos(angle * 6 + peak_index * 1.2 + variant * 0.4)
            crag = h * 0.028 * math.sin(x * 19 + y * 7) * math.sin(y * 17 - x * 9) * t * (1 - t)
            result = max(result, cone + ridge + crag)
        return max(0, result)
    points = [Vector((p.x, p.y, height(p.x, p.y))) for p in coords]
    rock_edges, snow_edges = {}, {}
    snow_height = [0.020 + 0.006 * math.sin(p.x * 13 + p.y * 11) for p in points]
    for face in faces:
        if len(face) != 3:
            continue
        p = [points[i] for i in face]
        if max(v.z for v in p) < 0.035:
            continue
        center = sum(p, Vector()) / 3
        mesh.face(p, "Rock", color((0.08, 0.28, 0.36), rng.uniform(0.86, 1.15)), (0, 0, 1))
        for i in range(3):
            a, b = face[i], face[(i + 1) % 3]
            key = tuple(sorted((a, b)))
            rock_edges.setdefault(key, []).append((a, b))
        snowline = 0.68 + 0.065 * math.sin(center.x * 9 + center.y * 6 + variant)
        if center.z > snowline:
            top = [points[i] + Vector((0, 0, snow_height[i])) for i in face]
            mesh.face(top, "Snow", color(SNOW, rng.uniform(0.96, 1.04)), (0, 0, 1))
            for i in range(3):
                a, b = face[i], face[(i + 1) % 3]
                key = tuple(sorted((a, b)))
                snow_edges.setdefault(key, []).append((a, b))
    for edges in snow_edges.values():
        if len(edges) != 1:
            continue
        a, b = edges[0]
        pa, pb = points[a], points[b]
        mesh.quad(pa + Vector((0, 0, snow_height[a])), pb + Vector((0, 0, snow_height[b])), pb, pa,
            "Snow", color(SNOW, 0.94), Vector((pa.x + pb.x, pa.y + pb.y, 0.1)))
    for edges in rock_edges.values():
        if len(edges) != 1:
            continue
        a, b = edges[0]
        pa, pb = points[a], points[b]
        qa, qb = Vector((pa.x, pa.y, 0)), Vector((pb.x, pb.y, 0))
        mesh.quad(pa, pb, qb, qa, "Rock", color((0.08, 0.28, 0.36), 0.88), Vector((pa.x + pb.x, pa.y + pb.y, 0)))
        mesh.tri(qa, qb, (0, 0, 0), "Rock", color((0.08, 0.28, 0.36), 0.8), (0, 0, -1))
    return mesh.object("ConceptMountain" + str(variant))


def sphere(phi, theta):
    return Vector((math.sin(phi) * math.cos(theta), math.sin(phi) * math.sin(theta), math.cos(phi)))


def snowball():
    rng = random.Random(850)
    mesh = Draft(["Snow", "PackedSnow"])
    raw = bmesh.new()
    bmesh.ops.create_icosphere(raw, subdivisions=3, radius=0.481)
    for face in raw.faces:
        points = []
        for vertex in face.verts:
            p = vertex.co.copy()
            direction = p.normalized()
            phi, theta = math.acos(max(-1, min(1, direction.z))), math.atan2(direction.y, direction.x)
            p *= 1 + 0.006 * math.sin(theta * 7 + phi * 5) + 0.004 * math.cos(theta * 3 - phi * 9)
            points.append(p)
        mesh.face(points, "Snow", color(SNOW, rng.uniform(0.965, 1.025)), sum(points, Vector()))
    raw.free()
    tilt = Quaternion(Vector((0.6, 0.8, 0.1)).normalized(), 0.35)
    steps = 96
    offsets = (-0.195, -0.13, -0.015, 0.11, 0.195)
    radii = (0.479, 0.507, 0.511, 0.495, 0.479)
    def band(step, edge):
        t = step / steps
        fade = min(1.0, t * 12, (1 - t) * 12)
        fade = fade * fade * (3 - 2 * fade)
        width = 1 + 0.12 * math.sin(t * 19)
        phi = 0.26 + t * (math.pi - 0.52) + offsets[edge] * width * (0.25 + 0.75 * fade)
        theta = t * math.pi * 7.0 + 0.07 * math.sin(t * 31)
        radius = 0.479 + (radii[edge] - 0.479) * fade
        return tilt @ (sphere(phi, theta) * (radius + 0.0015 * math.sin(t * 47 + edge)))
    for i in range(steps):
        for edge in range(4):
            a, b, c, d = band(i, edge), band(i + 1, edge), band(i + 1, edge + 1), band(i, edge + 1)
            slot, tint = ("PackedSnow", PACKED) if edge == 0 else ("Snow", SNOW)
            mesh.quad(a, b, c, d, slot, color(tint, rng.uniform(0.97, 1.025)), a + b + c + d)
    for i in range(10):
        direction = sphere(0.52 + (i * 0.723 % 2.0), i * 2.3999)
        rotation = Vector((0, 0, 1)).rotation_difference(direction)
        ico(mesh, direction * 0.478, (0.042 + (i % 3) * 0.009, 0.049, 0.032), "Snow", SNOW, 5000 + i, 2, rotation)
    for i in range(22):
        direction = sphere(0.40 + (i * 0.631 % 2.2), i * 2.161)
        ico(mesh, direction * 0.485, (0.008, 0.010, 0.008), "PackedSnow", (0.50, 0.78, 0.94), 5100 + i, 1)
    result = mesh.object("ConceptSnowball")
    result["unit_diameter"] = 1.0
    result["initial_game_radius_m"] = 0.85
    return result


def export(obj):
    bpy.ops.object.select_all(action="DESELECT")
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    bpy.ops.export_scene.fbx(filepath=str(EXPORT / (obj.name + ".fbx")), use_selection=True,
        object_types={"MESH"}, add_leaf_bones=False, bake_anim=False, axis_forward="-Z", axis_up="Y",
        apply_unit_scale=True, apply_scale_options="FBX_SCALE_UNITS", use_space_transform=True,
        bake_space_transform=True, mesh_smooth_type="FACE", use_mesh_modifiers=True, colors_type="LINEAR")
    print("CONCEPT_ASSET", obj.name, "triangles", sum(len(p.vertices) - 2 for p in obj.data.polygons), flush=True)


assets = [platform(), snowball(), cedar(0), cedar(1), islet(0), islet(1), islet(2), islet(0, cliff=True)]
assets.extend(mountain(i) for i in range(3))
for i, obj in enumerate(assets):
    export(obj)
    if i > 0:
        obj.location = (17 + (i - 1) % 4 * 4.5, (i - 1) // 4 * 6 - 6, 0.6 if obj.name == "ConceptSnowball" else 0)

scene = bpy.context.scene
scene.render.engine = "CYCLES"
scene.cycles.samples = 32
scene.world.color = (0.32, 0.40, 0.52)
bpy.ops.object.light_add(type="AREA", location=(-8, -12, 18))
light = bpy.context.object
light.name = "Soft alpine studio key"
light.data.energy = 3500
light.data.shape = "DISK"
light.data.size = 10
light.rotation_euler = (Vector((0, 0, 0)) - light.location).to_track_quat("-Z", "Y").to_euler()
bpy.ops.object.camera_add(location=(25, -37, 27))
camera = bpy.context.object
camera.rotation_euler = (Vector((8, 0, -1)) - camera.location).to_track_quat("-Z", "Y").to_euler()
camera.data.type = "ORTHO"
camera.data.ortho_scale = 48
scene.camera = camera
scene.render.resolution_x = 1920
scene.render.resolution_y = 1080
scene.render.resolution_percentage = 100
bpy.data.orphans_purge(do_recursive=True)
bpy.ops.wm.save_as_mainfile(filepath=str(SOURCE / "SnowArenaConcept.blend"), compress=True)
print("CONCEPT_MASTER", str(SOURCE / "SnowArenaConcept.blend"), flush=True)
