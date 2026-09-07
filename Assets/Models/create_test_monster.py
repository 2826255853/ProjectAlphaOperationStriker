import bpy
import math
from mathutils import Vector

# Reset scene
bpy.ops.object.select_all(action='SELECT')
bpy.ops.object.delete(use_global=False)

def mat(name, color, metallic=0.0, roughness=0.7):
    m = bpy.data.materials.new(name)
    m.diffuse_color = (*color, 1.0)
    m.metallic = metallic
    m.roughness = roughness
    return m

skin = mat('Monster_Skin', (0.18, 0.48, 0.25), roughness=0.9)
belly = mat('Monster_Belly', (0.35, 0.72, 0.30), roughness=0.85)
dark = mat('Monster_Horns', (0.08, 0.06, 0.04), roughness=0.8)
eye = mat('Monster_Eyes', (0.95, 0.75, 0.08), metallic=0.1, roughness=0.25)
black = mat('Monster_Pupils', (0.005, 0.003, 0.002), roughness=0.3)

def uv(name, loc, scale, material, seg=16, rings=10):
    bpy.ops.mesh.primitive_uv_sphere_add(segments=seg, ring_count=rings, location=loc)
    o = bpy.context.object; o.name = name; o.scale = scale
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    o.data.materials.append(material)
    bpy.ops.object.shade_smooth()
    return o

def cone(name, loc, radius1, radius2, depth, material, rot=(0,0,0), verts=12):
    bpy.ops.mesh.primitive_cone_add(vertices=verts, radius1=radius1, radius2=radius2, depth=depth, location=loc, rotation=rot)
    o = bpy.context.object; o.name = name; o.data.materials.append(material)
    return o

def cyl(name, loc, radius, depth, material, rot=(0,0,0), verts=12):
    bpy.ops.mesh.primitive_cylinder_add(vertices=verts, radius=radius, depth=depth, location=loc, rotation=rot)
    o = bpy.context.object; o.name = name; o.data.materials.append(material)
    bpy.ops.object.shade_smooth()
    return o

# Main silhouette
uv('Monster_Body', (0, 0, 1.45), (0.72, 0.48, 0.95), skin)
uv('Monster_Belly', (0, -0.43, 1.35), (0.48, 0.10, 0.58), belly, 20, 12)
uv('Monster_Head', (0, 0, 2.65), (0.78, 0.58, 0.62), skin)

# Eyes and pupils face toward -Y (front)
for x in (-0.30, 0.30):
    uv('Eye', (x, -0.53, 2.78), (0.19, 0.08, 0.19), eye)
    uv('Pupil', (x, -0.605, 2.78), (0.075, 0.025, 0.105), black, 12, 8)

# Mouth and teeth
uv('Mouth', (0, -0.56, 2.42), (0.38, 0.06, 0.14), black)
for x in (-0.20, 0.0, 0.20):
    cone('Tooth', (x, -0.63, 2.45), 0.045, 0.0, 0.16, belly, rot=(math.pi,0,0), verts=8)

# Horns
for x in (-0.43, 0.43):
    cone('Horn', (x, 0.0, 3.30), 0.16, 0.015, 0.55, dark, rot=(0, (-0.22 if x < 0 else 0.22), 0), verts=10)

# Arms and hands
for x in (-0.82, 0.82):
    cyl('Arm', (x, 0, 1.55), 0.16, 0.75, skin, rot=(0, math.radians(90), 0))
    uv('Hand', (x*1.12, -0.02, 1.20), (0.24, 0.20, 0.24), skin)

# Legs and feet
for x in (-0.33, 0.33):
    cyl('Leg', (x, 0, 0.58), 0.22, 0.70, skin)
    uv('Foot', (x, -0.18, 0.20), (0.32, 0.48, 0.18), dark)

# Tail for a readable silhouette
tail = cone('Tail', (0, 0.48, 1.25), 0.25, 0.04, 1.05, skin, rot=(math.radians(70), 0, 0), verts=12)

# Normalize the complete model to a 2 metre height with its feet on Z=0.
bpy.context.view_layer.update()
mesh_objects = [o for o in bpy.context.scene.objects if o.type == 'MESH']
world_points = [o.matrix_world @ Vector(corner) for o in mesh_objects for corner in o.bound_box]
min_z = min(p.z for p in world_points)
max_z = max(p.z for p in world_points)
height_scale = 2.0 / (max_z - min_z)
for o in mesh_objects:
    o.location.x *= height_scale
    o.location.y *= height_scale
    o.location.z = (o.location.z - min_z) * height_scale
    o.scale *= height_scale

# Export at Unity-ready dimensions.
bpy.ops.object.select_all(action='SELECT')
bpy.context.view_layer.objects.active = bpy.data.objects['Monster_Body']
for o in bpy.context.selected_objects:
    o.select_set(True)

out = r'C:\Unity Project\ProjectAlphaOperationStriker\Assets\Models\TestMonster.fbx'
bpy.ops.export_scene.fbx(filepath=out, use_selection=True, apply_scale_options='FBX_SCALE_ALL', object_types={'MESH'}, bake_space_transform=False)
bpy.ops.wm.save_as_mainfile(filepath=r'C:\Unity Project\ProjectAlphaOperationStriker\Assets\Models\TestMonster.blend')
