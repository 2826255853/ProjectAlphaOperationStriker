import bpy
from mathutils import Vector

blend = r'C:\Unity Project\ProjectAlphaOperationStriker\Assets\Models\TestMonster.blend'
fbx = r'C:\Unity Project\ProjectAlphaOperationStriker\Assets\Models\TestMonster.fbx'
bpy.ops.wm.open_mainfile(filepath=blend)

objs = [o for o in bpy.context.scene.objects if o.type == 'MESH']
if not objs:
    raise RuntimeError('No mesh objects found')

min_z = min((o.matrix_world @ Vector(c)).z for o in objs for c in o.bound_box)
max_z = max((o.matrix_world @ Vector(c)).z for o in objs for c in o.bound_box)
height = max_z - min_z
scale = 1.5 / height

for o in objs:
    o.scale = (o.scale.x * scale, o.scale.y * scale, o.scale.z * scale)

bpy.ops.object.select_all(action='DESELECT')
for o in objs:
    o.select_set(True)
bpy.context.view_layer.objects.active = objs[0]
bpy.ops.export_scene.fbx(filepath=fbx, use_selection=True, apply_scale_options='FBX_SCALE_ALL', object_types={'MESH'}, bake_space_transform=False)
bpy.ops.wm.save_as_mainfile(filepath=blend)
print('Original height:', height, 'New height:', height * scale, 'Scale:', scale)
