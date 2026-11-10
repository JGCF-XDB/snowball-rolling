"""Bake a genuinely volumetric cloud to a transparent, mobile-friendly Unity card.

Blender 4.5 LTS --background --factory-startup --python this_file -- PROJECT_ROOT
"""

import math
import random
import sys
from pathlib import Path

import bpy
from mathutils import Vector

project = Path(sys.argv[sys.argv.index("--") + 1]).resolve()
output = project / "Assets/SnowballArena/Art/Concept/ValleyCloud.png"
output.parent.mkdir(parents=True, exist_ok=True)
bpy.ops.object.select_all(action="SELECT")
bpy.ops.object.delete(use_global=False)
bpy.context.preferences.filepaths.save_version = 0

material = bpy.data.materials.new("Cold alpine cloud volume")
material.use_nodes = True
nodes, links = material.node_tree.nodes, material.node_tree.links
nodes.clear()
result = nodes.new("ShaderNodeOutputMaterial")
volume = nodes.new("ShaderNodeVolumePrincipled")
volume.inputs["Color"].default_value = (0.92, 0.97, 1.0, 1)
volume.inputs["Anisotropy"].default_value = 0.15
noise = nodes.new("ShaderNodeTexNoise")
noise.inputs["Scale"].default_value = 3.7
noise.inputs["Detail"].default_value = 4.0
noise.inputs["Roughness"].default_value = 0.7
ramp = nodes.new("ShaderNodeValToRGB")
ramp.color_ramp.elements[0].position = 0.32
ramp.color_ramp.elements[0].color = (0, 0, 0, 1)
ramp.color_ramp.elements[1].position = 0.78
ramp.color_ramp.elements[1].color = (2.0, 2.0, 2.0, 1)
coordinates = nodes.new("ShaderNodeTexCoord")
distance = nodes.new("ShaderNodeVectorMath")
distance.operation = "DISTANCE"
distance.inputs[1].default_value = (0.5, 0.5, 0.5)
falloff = nodes.new("ShaderNodeMapRange")
falloff.inputs["From Min"].default_value = 0.10
falloff.inputs["From Max"].default_value = 0.50
falloff.inputs["To Min"].default_value = 1.0
falloff.inputs["To Max"].default_value = 0.0
falloff.interpolation_type = "SMOOTHERSTEP"
density = nodes.new("ShaderNodeMath")
density.operation = "MULTIPLY"
links.new(coordinates.outputs["Generated"], distance.inputs[0])
links.new(distance.outputs["Value"], falloff.inputs["Value"])
links.new(falloff.outputs["Result"], density.inputs[1])
links.new(noise.outputs["Fac"], ramp.inputs["Fac"])
links.new(ramp.outputs["Color"], density.inputs[0])
links.new(density.outputs[0], volume.inputs["Density"])
links.new(volume.outputs["Volume"], result.inputs["Volume"])

rng = random.Random(1147)
for i in range(17):
    x = -4.1 + i % 7 * 1.32 + rng.uniform(-0.30, 0.30)
    y = rng.uniform(-0.8, 0.8)
    z = math.exp(-x * x / 7) * 0.75 + rng.uniform(-0.20, 0.45)
    if i >= 7:
        x *= 0.8
        z += 0.5
    bpy.ops.mesh.primitive_ico_sphere_add(subdivisions=3, radius=1, location=(x, y, z))
    puff = bpy.context.object
    puff.name = "Volume puff %02d" % i
    puff.scale = (rng.uniform(1.0, 1.55), rng.uniform(0.75, 1.20), rng.uniform(0.65, 1.0))
    puff.data.materials.append(material)

scene = bpy.context.scene
scene.render.engine = "CYCLES"
scene.cycles.samples = 128
scene.cycles.use_denoising = True
scene.cycles.max_bounces = 5
scene.cycles.volume_bounces = 2
scene.cycles.transparent_max_bounces = 16
scene.cycles.volume_step_rate = 0.65
preferences = bpy.context.preferences.addons["cycles"].preferences
try:
    preferences.compute_device_type = "OPTIX"
    preferences.get_devices()
    devices = [device for device in preferences.devices if device.type != "CPU"]
    if devices:
        for device in preferences.devices:
            device.use = device.type != "CPU"
        scene.cycles.device = "GPU"
except Exception:
    pass

world = bpy.data.worlds.new("Cold diffuse sky")
world.use_nodes = True
world.node_tree.nodes["Background"].inputs["Color"].default_value = (0.67, 0.79, 1.0, 1)
world.node_tree.nodes["Background"].inputs["Strength"].default_value = 0.65
scene.world = world
bpy.ops.object.light_add(type="SUN", location=(-6, -10, 12))
sun = bpy.context.object
sun.data.energy = 3.0
sun.data.angle = 0.20
sun.rotation_euler = (Vector((0, 0, 0)) - sun.location).to_track_quat("-Z", "Y").to_euler()
bpy.ops.object.camera_add(location=(0, -18, 7.2))
camera = bpy.context.object
camera.rotation_euler = (Vector((0, 0, 0.5)) - camera.location).to_track_quat("-Z", "Y").to_euler()
camera.data.type = "ORTHO"
camera.data.ortho_scale = 12.8
scene.camera = camera
scene.render.resolution_x = 1024
scene.render.resolution_y = 512
scene.render.resolution_percentage = 100
scene.render.film_transparent = True
scene.render.image_settings.file_format = "PNG"
scene.render.image_settings.color_mode = "RGBA"
scene.render.image_settings.color_depth = "16"
scene.view_settings.view_transform = "AgX"
scene.render.filepath = str(output)
bpy.data.orphans_purge(do_recursive=True)
bpy.ops.wm.save_as_mainfile(filepath=str(project / "ArtSource/ValleyCloud.blend"), compress=True)
bpy.ops.render.render(write_still=True)
print("CONCEPT_CLOUD", output, flush=True)
