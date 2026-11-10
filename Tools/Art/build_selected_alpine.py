"""Build the editable Blender master for the selected alpine scene.

Run Blender --background --factory-startup --python Tools/Art/build_selected_alpine.py -- PROJECT_ROOT.
Unity-native assets and the selected scene are under Assets/SnowballArena/Art/Selected01.
The JSON keeps the authored Unity mesh coordinates and vertex colours for reproducible reconstruction.
"""
import gzip
import json
import math
import sys
from pathlib import Path

import bpy
from mathutils import Matrix, Quaternion, Vector

project = Path(sys.argv[sys.argv.index("--") + 1]).resolve()
with gzip.open(project / "ArtSource/SelectedAlpine01.json.gz", "rt", encoding="utf-8") as stream:
    source = json.load(stream)

bpy.ops.object.select_all(action="SELECT")
bpy.ops.object.delete(use_global=False)
bpy.context.scene.unit_settings.system = "METRIC"
bpy.context.preferences.filepaths.save_version = 0

material = bpy.data.materials.new("Alpine authored vertex colours")
material.use_nodes = True
surface = material.node_tree.nodes.get("Principled BSDF")
surface.inputs["Roughness"].default_value = 0.72
colours = material.node_tree.nodes.new("ShaderNodeVertexColor")
colours.layer_name = "Col"
material.node_tree.links.new(colours.outputs["Color"], surface.inputs["Base Color"])

# Unity uses Y up and left-handed mesh coordinates. Reverse the triangle winding
# after exchanging Y and Z to keep Blender's outward faces and edit-mode normals.
basis = Matrix(((1, 0, 0, 0), (0, 0, 1, 0), (0, 1, 0, 0), (0, 0, 0, 1)))
meshes = []
for model in source["meshes"]:
    mesh = bpy.data.meshes.new(model["name"])
    points = [(p["x"], p["z"], p["y"]) for p in model["vertices"]]
    indices = model["triangles"]
    faces = [(indices[i], indices[i+2], indices[i+1]) for i in range(0, len(indices), 3)]
    mesh.from_pydata(points, [], faces)
    mesh.update()
    mesh.materials.append(material)
    colours = mesh.color_attributes.new(name="Col", type="FLOAT_COLOR", domain="CORNER")
    for face in mesh.polygons:
        face.use_smooth = False
        for loop in face.loop_indices:
            tint = model["colors"][mesh.loops[loop].vertex_index]
            colours.data[loop].color = (tint["r"], tint["g"], tint["b"], tint["a"])
    mesh.color_attributes.active_color = colours
    meshes.append(mesh)

collections = {}
for node in source["nodes"]:
    group = node["group"]
    if group not in collections:
        collection = bpy.data.collections.new(group)
        bpy.context.scene.collection.children.link(collection)
        collections[group] = collection
    obj = bpy.data.objects.new(node["name"], meshes[node["mesh"]])
    collections[group].objects.link(obj)
    p, q, s = node["position"], node["rotation"], node["scale"]
    position = Vector((p["x"], p["y"], p["z"]))
    rotation = Quaternion((q["w"], q["x"], q["y"], q["z"]))
    scale = Vector((s["x"], s["y"], s["z"]))
    obj.matrix_world = basis @ Matrix.LocRotScale(position, rotation, scale) @ basis
    obj["unity_layer"] = 0 if group == "Gameplay perspective cliffs" else 2
    obj["selected_layout"] = "01"
    obj["unity_source_mesh"] = source["meshes"][node["mesh"]]["name"]

scene = bpy.context.scene
scene.render.engine = "CYCLES"
scene.cycles.samples = 48
scene.world.color = (0.30, 0.38, 0.47)
bpy.ops.object.light_add(type="SUN", location=(-12, -18, 25))
sun = bpy.context.object
sun.name = "Soft cold-day sun"
sun.data.energy = 2.0
sun.data.angle = math.radians(7)
sun.rotation_euler = (Vector((0, 0, 0)) - sun.location).to_track_quat("-Z", "Y").to_euler()
bpy.ops.object.camera_add(location=(0, -22.25, 37.188))
camera = bpy.context.object
camera.name = "Gameplay 60 degrees"
camera.rotation_euler = (Vector((0, 0, -1.35)) - camera.location).to_track_quat("-Z", "Y").to_euler()
camera.data.type = "PERSP"
camera.data.sensor_fit = "VERTICAL"
camera.data.sensor_height = 24
camera.data.lens = 24 / (2 * math.tan(math.radians(17)))
camera.data.clip_end = 500
scene.camera = camera
scene.render.resolution_x = 1920
scene.render.resolution_y = 1080
scene.render.resolution_percentage = 100
scene["layout"] = "Selected Alpine 01"
scene["gameplay_and_shelf_pitch"] = "60 degrees"
scene["background_composition"] = "Distant mountains are a separate Unity camera layer."
bpy.data.orphans_purge(do_recursive=True)
bpy.ops.wm.save_as_mainfile(filepath=str(project / "ArtSource/SelectedAlpine01.blend"), compress=True)
print(f"SELECTED 01 BLENDER: {len(meshes)} meshes and {len(source['nodes'])} editable objects")
