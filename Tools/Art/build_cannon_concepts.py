"""Blender-authored cannon studies; no generative image service is used.

blender --background --factory-startup --python Tools/Art/build_cannon_concepts.py -- PROJECT_ROOT
Add --animate to render the slowed presentation clips as PNG sequences.
All views of a variant come from the same editable metric model.
"""
import json
import math
import random
import sys
import gzip
from pathlib import Path

import bpy
from mathutils import Vector

ROOT = Path(sys.argv[sys.argv.index("--") + 1]).resolve()
SELECTED = "--selected" in sys.argv
OUT = ROOT / ("docs/art/selected-cannon-b" if SELECTED else "docs/art/cannon-concepts")
SOURCE = ROOT / "ArtSource/CannonConcepts"
ANIMATE = "--animate" in sys.argv
VARIANTS = "B" if SELECTED else (sys.argv[sys.argv.index("--variant") + 1] if "--variant" in sys.argv else "ABC")
BORE_SIDES = 48 if SELECTED else 8
BORE_RADIUS = .30 / math.cos(math.pi / BORE_SIDES) if SELECTED else .31
MUZZLE_FORWARD = 2.00 if SELECTED else 1.18
OUT.mkdir(parents=True, exist_ok=True)
SOURCE.mkdir(parents=True, exist_ok=True)
TAU = math.tau


def material(name, color, metallic=0, roughness=.6, glow=0):
    mat = bpy.data.materials.new(name)
    mat.diffuse_color = (*color, 1)
    mat.use_nodes = True
    bsdf = mat.node_tree.nodes.get("Principled BSDF")
    bsdf.inputs["Base Color"].default_value = (*color, 1)
    bsdf.inputs["Metallic"].default_value = metallic
    bsdf.inputs["Roughness"].default_value = roughness
    bsdf.inputs["Emission Color"].default_value = (*color, 1)
    bsdf.inputs["Emission Strength"].default_value = glow
    return mat


def finish(obj, name, mat, group):
    obj.name = name
    for col in list(obj.users_collection):
        col.objects.unlink(obj)
    group.objects.link(obj)
    obj.data.materials.append(mat)
    return obj


def cube(name, loc, size, mat, group, bevel=.05):
    bpy.ops.mesh.primitive_cube_add(size=1, location=loc)
    obj = finish(bpy.context.object, name, mat, group)
    obj.dimensions = size
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    if bevel:
        mod = obj.modifiers.new("Broad readable bevel", "BEVEL")
        mod.width = bevel
        mod.segments = 3 if SELECTED else 1
        obj.modifiers.new("Weighted corner normals", "WEIGHTED_NORMAL")
    return obj


def cylinder(name, loc, radius, depth, mat, group, axis="Z", vertices=12):
    bpy.ops.mesh.primitive_cylinder_add(vertices=vertices, radius=radius, depth=depth, location=loc)
    obj = finish(bpy.context.object, name, mat, group)
    if axis == "X":
        obj.rotation_euler.y = math.pi / 2
    elif axis == "Y":
        obj.rotation_euler.x = math.pi / 2
    if SELECTED:
        soften_curves(obj, .014, vertices != 6)
    return obj


def ico(name, loc, size, mat, group, subdivisions=1):
    bpy.ops.mesh.primitive_ico_sphere_add(subdivisions=subdivisions, radius=1, location=loc)
    obj = finish(bpy.context.object, name, mat, group)
    obj.scale = size
    if SELECTED and name.startswith("Cannonball"):
        for face in obj.data.polygons:face.use_smooth=True
    return obj


def round_core(group):
    bpy.ops.mesh.primitive_uv_sphere_add(segments=48, ring_count=24, radius=1, location=(.24,0,0))
    obj=finish(bpy.context.object,"Rounded orange kinetic core",M["amber"],group)
    obj.scale=(.06,.15,.15)
    for face in obj.data.polygons:face.use_smooth=True
    return obj


def mesh(name, points, faces, mat, group):
    data = bpy.data.meshes.new(name)
    data.from_pydata(points, [], faces)
    data.update()
    obj = bpy.data.objects.new(name, data)
    group.objects.link(obj)
    data.materials.append(mat)
    return obj


def soften_curves(obj, width=.012, smooth=True):
    for face in obj.data.polygons:face.use_smooth=smooth
    obj.data.set_sharp_from_angle(angle=.6)
    bevel=obj.modifiers.new("Machined rounded edge", "BEVEL")
    bevel.width=width
    bevel.segments=3
    bevel.limit_method="ANGLE"
    bevel.angle_limit=.35
    bevel.harden_normals=True
    normal=obj.modifiers.new("Continuous surface normals", "WEIGHTED_NORMAL")
    normal.keep_sharp=True


def tube(name, x0, x1, r0, r1, inner, z, mat, group, n=12):
    # Four rings: the inner wall is real geometry, not a painted end cap.
    points = [(x, r * math.cos(i * TAU / n), z + r * math.sin(i * TAU / n))
              for x, r in [(x0, r0), (x1, r1), (x1, inner), (x0, inner)] for i in range(n)]
    faces = []
    for ring in range(4):
        for i in range(n):
            a, b = ring * n + i, ring * n + (i + 1) % n
            c, d = ((ring + 1) % 4) * n + (i + 1) % n, ((ring + 1) % 4) * n + i
            faces.append((a, b, c, d))
    obj=mesh(name, points, faces, mat, group)
    if SELECTED:soften_curves(obj)
    return obj


def snow_mantle(x0, x1, radius, z, group):
    # A thin top-only arc keeps the muzzle and bore open.
    angles = [math.radians(30 + 20 * i) for i in range(7)]
    points = [(x, radius * math.cos(a), z + radius * math.sin(a) + .055)
              for x in [x0, x1] for a in angles]
    return mesh("Snow mantle / open muzzle", points,
                [(i, i+1, i+8, i+7) for i in range(6)], M["snow"], group)


def bolt(loc, group, axis="Y"):
    return cylinder("Brass hex fastener", loc, .065, .055, M["brass"], group, axis, 6)


def selected_pedestal_cannon(group):
    # Preserve B's three-tier hexagonal mount and crystal shoulders. Only the
    # barrel proportions borrow from a long cannon; the calibre stays fixed.
    for z,r,depth in [(.14,1.10,.24),(.34,.93,.22),(.51,.68,.18)]:
        cylinder("Hexagonal glacier pedestal",(-.45,0,z),r,depth,M["stone"],group,vertices=6)
    cylinder("Turret ice seat",(-.48,0,.57),.46,.20,M["ice"],group,vertices=48)
    for y in [-.65,.65]:
        crystal=ico("Glacier shoulder crystal",(-.63,y,.79),(.25,.23,.57),M["ice"],group)
        crystal.rotation_euler.x=y*.40
    tube("Long cannon barrel",-1.30,1.91,.50,.40,BORE_RADIUS,.8,M["body"],group,BORE_SIDES)
    bpy.ops.mesh.primitive_uv_sphere_add(segments=48,ring_count=16,radius=1,location=(-1.28,0,.8))
    breech=finish(bpy.context.object,"Rounded barrel breech",M["body"],group)
    breech.scale=(.23,.50,.50)
    for face in breech.data.polygons:face.use_smooth=True
    cylinder("Recessed dark bore",(-1.20,0,.8),BORE_RADIUS,.025,M["bore"],group,"X",BORE_SIDES)
    for x,r,width in [(-1.02,.53,.12),(-.32,.50,.12),(1.92,.47,.16)]:
        tube("Cannon retaining ring",x-width/2,x+width/2,r,r,BORE_RADIUS,.8,M["steel"],group,BORE_SIDES)
    tube("Detail retaining ring",1.90,1.92,.476,.476,.45,.8,M["ice"],group,BORE_SIDES)
    for x in [-.74,-.25,.24,.73,1.22]:
        radius=.50+(x+1.30)/3.21*(.40-.50)
        for sign in [-1,1]:
            rune=cube("Inset orange rune",(x,sign*radius,.89),(.12,.035,.12),M["amber"],group,.018)
            rune.rotation_euler.y=math.pi/4
    cylinder("Warning lamp seat",(-.95,0,1.29),.14,.045,M["steel"],group,vertices=48)
    bpy.ops.mesh.primitive_uv_sphere_add(segments=32,ring_count=16,radius=1,location=(-.95,0,1.41))
    lamp=finish(bpy.context.object,"Round warning lamp",M["amber"],group)
    lamp.scale=(.12,.12,.12)
    for face in lamp.data.polygons:face.use_smooth=True
    ico("Small rear snow remnant",(-1.12,0,1.29),(.12,.15,.035),M["snow"],group)
    for x,y in [(-.9,.30),(-.32,-.36)]:
        ico("Pedestal snow patch",(x,y,.60),(.12,.10,.035),M["snow"],group)
    # Seat the barrel in a rounded saddle without letting the pedestal or ice
    # seat intrude into the visible bore. Keep B's original three-tier outline.
    bpy.ops.mesh.primitive_cylinder_add(vertices=64,radius=.51,depth=100,location=(0,0,.8),rotation=(0,math.pi/2,0))
    cutter=bpy.context.object
    cutter.name="Hidden barrel saddle cutter"
    cutter.hide_render=True
    cutter.hide_set(True)
    for obj in list(group.objects):
        if obj.name.startswith(("Hexagonal glacier pedestal","Turret ice seat","Pedestal snow patch")):
            modifier=obj.modifiers.new("Barrel saddle clearance","BOOLEAN")
            modifier.operation="DIFFERENCE"
            modifier.solver="EXACT"
            modifier.object=cutter


def cannon(group, kind):
    if SELECTED and kind=="B":
        selected_pedestal_cannon(group)
        return
    if kind == "A":
        for y in [-.43, .43]:
            cube("Timber carriage rail", (-.25, y, .31), (1.75, .22, .36), M["wood"], group)
            for x in [-.68, .42]:
                cylinder("Faceted iron wheel", (x, y * 1.62, .40), .4, .18, M["dark"], group, "Y")
                cylinder("Wheel inset", (x, y * 1.85, .4), .25, .035, M["steel"], group, "Y")
                bolt((x, y * 1.93, .4), group)
                ico("Wheel snow cap", (x, y * 1.62, .74), (.28, .15, .075), M["snow"], group)
        cube("Cross axle", (-.2, 0, .40), (1.15, 1.3, .17), M["steel"], group)
        for y in [-.45, .45]:
            cube("Barrel cheek support", (-.3, y, .68), (.65, .16, .65), M["steel"], group)
            bolt((-.3, y * 1.22, .83), group)
        tube("Hollow twelve-sided iron barrel", -.92, 1.1, .37, .44, .31, .92, M["steel"], group)
        cylinder("Recessed bore darkness", (-.83, 0, .92), .305, .03, M["bore"], group, "X")
        for x in [-.62, .43, 1.08]:
            tube("Brass reinforcement collar", x-.065, x+.065, .455, .455, .37, .92, M["brass"], group)
        snow_mantle(-.81, .72, .405, .92, group)
        cube("Warning light plinth", (-.63, 0, 1.36), (.24, .28, .1), M["dark"], group)
        ico("Amber warning lamp", (-.63, 0, 1.48), (.12, .13, .1), M["amber"], group)
    elif kind == "B":
        for z, r in [(.12, .92), (.30, .78), (.47, .56)]:
            cylinder("Hexagonal glacier pedestal", (-.18, 0, z), r, .21, M["stone"], group, vertices=6)
        cylinder("Turret ice seat", (-.25, 0, .62), .45, .25, M["ice"], group, vertices=BORE_SIDES)
        for y in [-.55, .55]:
            crystal = ico("Glacier shoulder crystal", (-.38, y, .8), (.23, .23, .58), M["ice"], group)
            crystal.rotation_euler.x = y * .65
        tube("Frost barrel", -.85, 1.14, .40, .44, BORE_RADIUS, .98, M["body"] if SELECTED else M["ice"], group, BORE_SIDES)
        cylinder("Recessed dark ice bore", (-.8, 0, .98), BORE_RADIUS, .03, M["bore"], group, "X", BORE_SIDES)
        for x in [-.65, .48, 1.12]:
            tube("Dark retaining ring", x-.06, x+.06, .48, .48, BORE_RADIUS if SELECTED else .38, .98, M["steel"], group, BORE_SIDES)
        if SELECTED:
            tube("Detail retaining ring", 1.095, 1.115, .485, .485, .46, .98, M["ice"], group, BORE_SIDES)
        for x in [-.3, .05, .4]:
            for y in [-.392, .392]:
                rune = cube("Warm rune inset", (x, y, 1.08), (.13, .035, .12), M["amber"], group, .018)
                rune.rotation_euler.y = math.pi / 4
        if SELECTED:
            # Keep the overhead silhouette dark against the snowfield.
            ico("Small rear snow remnant", (-.66,0,1.40), (.16,.16,.035), M["snow"], group)
        else:
            snow_mantle(-.65, .6, .43, .98, group)
        for x, y in [(-.65, .42), (.12, -.50), (-.3, -.35)]:
            ico("Pedestal snow patch", (x, y, .47), ((.13,.11,.035) if SELECTED else (.30,.25,.08)), M["snow"], group)
        ico("Warning rune crown", (-.55, 0, 1.48), (.12, .12, .15), M["amber"], group)
        if SELECTED:
            for obj in group.objects:
                if not obj.name.startswith(("Hexagonal glacier pedestal", "Pedestal snow patch")):
                    obj.location.z -= .18
    else:
        for y in [-.64, .64]:
            cube("Sled skid", (-.18, y, .13), (2.12, .20, .20), M["steel"], group)
            tip = cube("Raised ski tip", (.96, y, .24), (.40, .2, .18), M["steel"], group)
            tip.rotation_euler.y = -.38
        cube("Snowplough chassis", (-.3, 0, .38), (1.45, 1.22, .43), M["steel"], group, .12)
        cube("Chassis snow blanket", (-.43, 0, .62), (1.05, 1.06, .07), M["snow"], group)
        for y in [-.48, .48]:
            cube("Recoil rail", (-.08, y, .82), (1.62, .13, .16), M["dark"], group)
            cylinder("Side gear boss", (-.42, y*1.33, .70), .32, .13, M["brass"], group, "Y", 10)
            cylinder("Gear cover", (-.42, y*1.47, .70), .22, .045, M["dark"], group, "Y", 10)
            bolt((-.42, y*1.54, .7), group)
            for x in [-.48, -.22, .04]:
                cube("Rear cooling vent", (x, y*1.31, .41), (.12, .025, .16), M["bore"], group, .01)
        tube("Pressure cannon tube", -.88, 1.13, .32, .36, .305, 1.02, M["steel"], group)
        cylinder("Recessed pressure bore", (-.83, 0, 1.02), .302, .03, M["bore"], group, "X")
        for x in [-.63, .30, 1.1]:
            tube("Orange safety collar", x-.08, x+.08, .41, .41, .32, 1.02, M["orange"], group)
        for y in [-.37, .37]:
            cylinder("Recoil piston", (-.12, y, 1.07), .065, 1.25, M["brass"], group, "X", 8)
        snow_mantle(-.65, .40, .35, 1.02, group)
        cylinder("Rear pressure tank", (-1.04, 0, .80), .25, .63, M["dark"], group, vertices=10)
        cylinder("Warning beacon base", (-.62, 0, 1.42), .16, .12, M["dark"], group)
        cylinder("Warning beacon lens", (-.62, 0, 1.56), .125, .17, M["amber"], group)


def projectile(group, kind):
    radius = {"A":.30,"B":.255,"C":.28}[kind]
    ico("Cannonball / physical radius 0.30 m", (0, 0, 0), (radius,)*3, M["dark"], group, 4 if SELECTED else 2)
    if kind == "A":
        for axis in ["X", "Y"]:
            bpy.ops.mesh.primitive_torus_add(major_segments=12, minor_segments=4, location=(0,0,0), major_radius=.284, minor_radius=.017)
            ring = finish(bpy.context.object, "Hot iron seam", M["amber"], group)
            ring.rotation_euler[1 if axis == "X" else 0] = math.pi/2
        ico("Hot leading face", (.28,0,0), (.032,.11,.11), M["amber"], group)
    elif kind == "B":
        for i in range(6):
            a = i*TAU/6
            shard = ico("Ice shell plate", (0,.22*math.cos(a),.22*math.sin(a)), (.20,.075,.075), M["ice"], group)
            shard.rotation_euler.x = a
        if SELECTED:
            round_core(group)
        else:
            ico("Orange kinetic core", (.24,0,0), (.06,.15,.15), M["amber"], group)
        for i in range(4):
            a=i*TAU/4
            ico("Hot seam", (.02,.255*math.cos(a),.255*math.sin(a)), (.18,.015,.015), M["amber"], group)
    else:
        for x in [-.12,.12]:
            tube("Copper pressure band", x-.018,x+.018,.265,.265,.25,0,M["brass"],group)
        ico("Orange impact cap", (.255,0,0), (.045,.13,.13), M["amber"], group)
        for y in [-.26,.26]:
            cube("Luminous pressure slot", (0,y,0), (.18,.025,.045), M["amber"], group, .008)


def aim(camera, loc, target, scale):
    camera.location = loc
    camera.rotation_euler = (Vector(target)-camera.location).to_track_quat("-Z","Y").to_euler()
    camera.data.ortho_scale = scale


def export_selected(gun, ball):
    """Bake evaluated Blender geometry to Unity coordinates (+Z muzzle, +Y up).

    Reversing winding preserves outward normals after this handedness change.
    The source .blend retains every separately editable modeled part.
    """
    groups={"Mount":[],"Barrel":[],"Signals":[],"Projectile":list(ball.objects)}
    for obj in gun.objects:
        if obj.data.materials[0] == M["amber"]:
            role="Signals"
        elif any(word in obj.name.lower() for word in ("barrel","bore","retaining ring","rear snow")):
            role="Barrel"
        else:
            role="Mount"
        groups[role].append(obj)
    fx=bpy.data.collections.new("Authored effect meshes")
    bpy.context.scene.collection.children.link(fx)
    points=[(0,0,0)]+[(0,.38*math.cos(i*TAU/48),.38*math.sin(i*TAU/48)) for i in range(48)]
    groups["MuzzleFlash"]=[mesh("Round muzzle flare",points,[(0,i+1,(i+1)%48+1) for i in range(48)],M["amber"],fx)]
    groups["IceFragment"]=[ico("Ice fragment",(0,0,0),(.055,.035,.045),M["ice"],fx)]
    groups["EmberFragment"]=[ico("Ember fragment",(0,0,0),(.035,)*3,M["amber"],fx)]
    for obj in fx.objects:obj.hide_render=True
    mats=list(dict.fromkeys(obj.data.materials[0] for objects in groups.values() for obj in objects))
    materials=[]
    for mat in mats:
        bsdf=mat.node_tree.nodes.get("Principled BSDF")
        materials.append({"name":mat.name.split(" /")[0],"color":list(bsdf.inputs["Base Color"].default_value),
                          "metallic":bsdf.inputs["Metallic"].default_value,
                          "roughness":bsdf.inputs["Roughness"].default_value,
                          "signal":bsdf.inputs["Emission Strength"].default_value>0})
    meshes=[]
    bpy.context.view_layer.update()
    depsgraph=bpy.context.evaluated_depsgraph_get()
    for name,objects in groups.items():
        points=[]; normals=[]; triangles=[]; slots=[]
        for obj in objects:
            evaluated=obj.evaluated_get(depsgraph)
            data=evaluated.to_mesh()
            data.calc_loop_triangles()
            matrix=obj.matrix_world
            normal_matrix=matrix.to_3x3().inverted().transposed()
            for triangle in data.loop_triangles:
                indices=[]
                for index,loop in zip(triangle.vertices,triangle.loops):
                    p=matrix@data.vertices[index].co
                    if name=="Projectile" and p.length>.30:
                        p=p.normalized()*.30
                    n=(normal_matrix@data.corner_normals[loop].vector).normalized()
                    indices.append(len(points))
                    points.append({"x":-p.y,"y":p.z,"z":p.x})
                    normals.append({"x":-n.y,"y":n.z,"z":n.x})
                triangles+= [indices[0],indices[2],indices[1]]
                slots.append(mats.index(obj.data.materials[0]))
            evaluated.to_mesh_clear()
        meshes.append({"name":name,"vertices":points,"normals":normals,"triangles":triangles,"materialSlots":slots})
    source={"projectileRadius":.30,"muzzleInnerDiameter":.60,"muzzleSides":BORE_SIDES,"muzzle":{"x":0,"y":.8,"z":MUZZLE_FORWARD},"materials":materials,"meshes":meshes}
    with gzip.open(ROOT/"ArtSource/SelectedCannonB.json.gz","wt",encoding="utf8") as stream:
        json.dump(source,stream,separators=(",",":"))


def render(scene, camera, path, loc, target, scale, width=1200, height=900):
    aim(camera, loc, target, scale)
    scene.render.resolution_x = width
    scene.render.resolution_y = height
    scene.render.filepath = str(path)
    bpy.ops.render.render(write_still=True)


def animate(scene, camera, kind, cannon_group, ball_group):
    # Presentation is slowed to 20% so the 0.6 m ball and transient effects can be inspected.
    scene.render.resolution_x, scene.render.resolution_y = 640, 360
    scene.eevee.taa_render_samples = 8
    scene.render.fps = 24
    scene.frame_start, scene.frame_end = 1, 48
    flight_height=.8 if SELECTED else 1.0
    gun_offset=-3.8 if SELECTED else -3.3
    launch_x=gun_offset+MUZZLE_FORWARD if SELECTED else -2.1
    for obj in cannon_group.objects:
        obj.location.x += gun_offset
    for obj in ball_group.objects:
        obj.location.z += flight_height
    anchor = bpy.data.objects.new("Animated projectile / 20 percent speed study", None)
    ball_group.objects.link(anchor)
    for obj in list(ball_group.objects):
        if obj != anchor:
            obj.parent = anchor
    for frame, x in [(1,launch_x),(6,launch_x),(33,2.0),(48,2.0)]:
        anchor.location.x=x
        anchor.keyframe_insert("location",frame=frame)
    for frame, scale in [(1,0),(5,0),(6,1),(32,1),(33,0),(48,0)]:
        anchor.scale=(scale,)*3
        anchor.keyframe_insert("scale",frame=frame)
    fx = bpy.data.collections.new("Presentation FX / decorative, no area damage")
    scene.collection.children.link(fx)
    # Tapered geometry makes the trail reliable under Eevee as well as orthographic review.
    trail = mesh("Hot taper", [(0,0,0),(-2.16,.10,0),(-2.16,0,.10),(-2.16,-.10,0),(-2.16,0,-.10)],
                 [(0,2,1),(0,3,2),(0,4,3),(0,1,4),(1,2,3,4)],M["tail"],fx)
    # Reverse the taper: broad near the ball, narrowing toward the launch position.
    for v in trail.data.vertices:
        if v.co.x == 0:
            v.co.x = -2.16
        else:
            v.co.x = 0
    trail.parent=anchor
    trail.location.z=flight_height
    for f, sx in [(1,0),(6,0),(18,1),(32,1),(33,0),(48,0)]:
        trail.scale=(sx,1,1)
        trail.keyframe_insert("scale",frame=f)
    rng=random.Random(21)
    for i in range(9):
        spark=ico("Impact ember %02d"%i,(2,0,flight_height),(.055,)*3,M["amber"],fx)
        direction=Vector((rng.uniform(-1,1),rng.uniform(-1,1),rng.uniform(-.5,1)))
        for f,t,size in [(1,0,0),(32,0,0),(33,0,1),(39,.6,.8),(48,1.2,0)]:
            spark.location=Vector((2,0,flight_height))+direction*t
            spark.scale=(.055*size,)*3
            spark.keyframe_insert("location",frame=f)
            spark.keyframe_insert("scale",frame=f)
    for x in range(-5,5):
        cube("Motion review ground tick",(x,0,.01),(.02,.28,.012),M["stone"],fx,0)
    aim(camera,(6,-10,6),(-1.3 if SELECTED else -.8,0,.65),10.8 if SELECTED else 10.5)
    scene["presentation_speed"]="20% study; gameplay still uses 18 m/s, radius 0.30 m, one target only"
    scene["impact_effect"]="Visual concept only; no explosive area damage"
    scene.frame_set(16)
    bpy.ops.wm.save_as_mainfile(filepath=str(ROOT/"ArtSource/SelectedCannonB-motion.blend" if SELECTED else SOURCE/f"{kind}-cannon-animated.blend"),compress=True)
    folder=ROOT/("TestResults/SelectedCannonB/BlenderFrames" if SELECTED else f"TestResults/CannonConcepts/Frames/{kind}")
    folder.mkdir(parents=True,exist_ok=True)
    for f in range(1,49):
        scene.frame_set(f)
        scene.render.filepath=str(folder/f"{f:03d}.png")
        bpy.ops.render.render(write_still=True)


report_path=OUT/"model-report.json"
report=json.loads(report_path.read_text(encoding="utf8")) if report_path.exists() else []
report=[r for r in report if r["variant"] not in VARIANTS]
for kind in VARIANTS:
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.context.preferences.filepaths.save_version=0
    bpy.context.preferences.edit.keyframe_new_interpolation_type="LINEAR"
    scene=bpy.context.scene
    scene.unit_settings.system="METRIC"
    scene.render.engine="BLENDER_EEVEE_NEXT"
    scene.eevee.taa_render_samples=32
    scene.render.image_settings.file_format="PNG"
    scene.render.resolution_percentage=100
    scene.view_settings.view_transform="AgX"
    world=bpy.data.worlds.new("Cold neutral studio")
    scene.world=world
    world.use_nodes=True
    world.node_tree.nodes["Background"].inputs[0].default_value=(.30,.39,.52,1)
    world.node_tree.nodes["Background"].inputs[1].default_value=.65
    M={"snow":material("Snow / soft matte",(.82,.90,.98)),
       "steel":material("Cold blue iron",(.075,.16,.23),.65,.32),
       "dark":material("Dense blue-black iron",(.025,.06,.085),.65,.38),
       "bore":material("Bore interior",(.008,.015,.02),0,.9),
       "brass":material("Desaturated old brass",(.36,.23,.095),.70,.32),
       "wood":material("Cold timber",(.16,.12,.10),0,.78),
       "ice":material("Faceted glacier ice",(.16,.55,.72),.25,.22),
       "stone":material("Blue slate pedestal",(.12,.22,.29)),
       "orange":material("Warm safety enamel",(.72,.16,.025),.35,.38),
       "amber":material("Hot amber danger signal",(1,.18,.008),.15,.28,2.0),
       "tail":material("Hot flight tail",(1,.11,.005),0,.5,.65)}
    if SELECTED:
        M["body"]=material("Obsidian blue barrel",(.018,.032,.052),.55,.38)
        for key,color in [("steel",(.035,.06,.085)),("stone",(.025,.045,.065))]:
            M[key].node_tree.nodes["Principled BSDF"].inputs["Base Color"].default_value=(*color,1)
        M["ice"]=material("Deep glacier crystal",(.045,.22,.31),.25,.32)
    stage=bpy.data.collections.new("Studio / presentation only")
    gun=bpy.data.collections.new(f"{kind} / Cannon model")
    ball=bpy.data.collections.new(f"{kind} / Projectile model 0.60 m diameter")
    for col in [stage,gun,ball]:scene.collection.children.link(col)
    cannon(gun,kind)
    projectile(ball,kind)
    if SELECTED:export_selected(gun,ball)
    floor=cube("Matte studio floor",(0,0,-.10),(200,200,.1),material("Studio blue-grey",(.20,.28,.37)),stage,0)
    for name,loc,power,size,color in [("Large softbox",(1,-4,7),1400,5,(.86,.94,1)),
                                     ("Cold rim",(-3,4,5),1700,4,(.55,.78,1)),
                                     ("Muzzle fill",(5,2,3),650,3,(1,.78,.56))]:
        bpy.ops.object.light_add(type="AREA",location=loc)
        light=bpy.context.object
        light.name=name
        light.data.energy=power
        light.data.shape="DISK"
        light.data.size=size
        light.data.color=color
        light.rotation_euler=(-light.location).to_track_quat("-Z","Y").to_euler()
    bpy.ops.object.camera_add()
    camera=bpy.context.object
    camera.name="Consistent model review camera"
    camera.data.type="ORTHO"
    scene.camera=camera
    for obj in ball.objects:obj.hide_render=True
    render(scene,camera,OUT/f"{kind}-hero.png",(4.5,-6,4.1),(.2 if SELECTED else 0,0,.76),5.0 if SELECTED else 3.75)
    render(scene,camera,OUT/f"{kind}-front.png",(8,0,.8),(0,0,.8),3.9 if SELECTED else 3.4,800,600)
    render(scene,camera,OUT/f"{kind}-side.png",(0,-8,.8),(.2 if SELECTED else 0,0,.8),4.7 if SELECTED else 3.4,800,600)
    render(scene,camera,OUT/f"{kind}-top.png",(0,0,8),(.2 if SELECTED else 0,0,0),5.5 if SELECTED else 3.4,800,600)
    render(scene,camera,OUT/f"{kind}-gameplay60.png",(0,-4,7.73),(.2 if SELECTED else 0,0,.8),4.9 if SELECTED else 3.6,1000,750)
    for obj in gun.objects:obj.hide_render=True
    for obj in ball.objects:obj.hide_render=False
    floor.hide_render=True
    render(scene,camera,OUT/f"{kind}-projectile.png",(.95,-1.35,.85),(0,0,0),.95,800,600)
    floor.hide_render=False
    for obj in gun.objects:obj.hide_render=False
    for obj in ball.objects:
        obj.location += Vector((3.0 if SELECTED else 1.7,-.6,.30))
    scene["design_status"]="Selected dark B / production model" if SELECTED else "Concept study awaiting user selection; not applied to Match.unity"
    scene["physical_projectile_radius_m"]=.30
    scene["gameplay_pitch_degrees"]=60
    aim(camera,(4.5,-6,4.1),(.25,0,.7),7.2 if SELECTED else 4.3)
    bpy.ops.wm.save_as_mainfile(filepath=str(ROOT/"ArtSource/SelectedCannonB.blend" if SELECTED else SOURCE/f"{kind}-cannon.blend"),compress=True)
    report.append({"variant":kind,"cannon_meshes":len(gun.objects),"projectile_meshes":len(ball.objects),"source":"ArtSource/SelectedCannonB.blend" if SELECTED else f"ArtSource/CannonConcepts/{kind}-cannon.blend"})
    if ANIMATE:
        for obj in ball.objects:
            obj.location -= Vector((3.0 if SELECTED else 1.7,-.6,.30))
        animate(scene,camera,kind,gun,ball)
report_path.write_text(json.dumps(sorted(report,key=lambda r:r["variant"]),indent=2),encoding="utf8")
print("CANNON CONCEPTS: editable models and matching orthographic views saved.")
