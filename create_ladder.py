import bpy, math, os
from mathutils import Vector
# clear
bpy.ops.object.select_all(action='SELECT')
bpy.ops.object.delete(use_global=False)
for d in (bpy.data.meshes, bpy.data.curves, bpy.data.materials, bpy.data.cameras, bpy.data.lights):
    pass
# materials
def mat(name, color, metallic=0.0, rough=0.45):
    m=bpy.data.materials.get(name) or bpy.data.materials.new(name)
    m.diffuse_color=(*color,1)
    m.use_nodes=True
    bs=next((n for n in m.node_tree.nodes if n.type=='BSDF_PRINCIPLED'),None)
    if bs:
        bs.inputs['Base Color'].default_value=(*color,1)
        bs.inputs['Metallic'].default_value=metallic
        bs.inputs['Roughness'].default_value=rough
    return m
orange=mat('Ladder_Rails_Painted',(0.8,0.12,0.025),0.25,0.3)
yellow=mat('Ladder_Rungs_Metal',(0.95,0.55,0.06),0.65,0.25)
black=mat('Ladder_Foot_Rubber',(0.025,0.03,0.035),0.0,0.75)

def cube(name, loc, scale, material, bevel=0.06, rot=(0,0,0)):
    bpy.ops.mesh.primitive_cube_add(location=loc, rotation=rot)
    o=bpy.context.object; o.name=name; o.scale=(scale[0]/2,scale[1]/2,scale[2]/2)
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    o.data.materials.append(material)
    if bevel:
        mod=o.modifiers.new('Soft_Edges','BEVEL'); mod.width=bevel; mod.segments=3
        bpy.context.view_layer.objects.active=o; bpy.ops.object.modifier_apply(modifier=mod.name)
    return o
# ladder dimensions, slight lean toward +Z at top
H=3.2; W=1.35; depth=0.12; lean=math.radians(-8)
# rails as beams rotated around X, center offset so feet at z=0
for x in (-W/2, W/2):
    zc=0.28
    o=cube('Ladder_Rail_L' if x<0 else 'Ladder_Rail_R',(x, H/2, zc+0.0),(depth,H,depth),orange,0.07,rot=(lean,0,0))
# rungs distributed, horizontal X, shifted z with lean
for i,y in enumerate([0.35,0.82,1.29,1.76,2.23,2.70,3.12]):
    z=0.28 + math.tan(-lean)*(y) # top leans +z
    cube(f'Ladder_Rung_{i+1:02d}',(0,y,z),(W-0.06,0.11,0.11),yellow,0.045,rot=(0,0,0))
# feet pads
for x in (-W/2, W/2):
    cube('Ladder_Foot_L' if x<0 else 'Ladder_Foot_R',(x,0.08,0.28),(0.28,0.16,0.42),black,0.08,rot=(lean,0,0))
# top cap
cube('Ladder_Top_Cap',(0,H+0.02,0.28+math.tan(-lean)*H),(W+0.10,0.14,0.16),orange,0.05)
# parent empty
bpy.ops.object.empty_add(type='PLAIN_AXES', location=(0,0,0)); root=bpy.context.object; root.name='Ladder_Root'
for o in list(bpy.context.scene.objects):
    if o!=root and o.parent is None: o.parent=root
# custom props
root['asset_type']='Gameplay Ladder'; root['height_m']=H; root['width_m']=W
# ground origin: keep root at origin
# save/export
outdir=r'C:\Unity Project\ProjectAlphaOperationStriker\Assets\Models'
os.makedirs(outdir,exist_ok=True)
bpy.ops.wm.save_as_mainfile(filepath=os.path.join(outdir,'GameplayLadder.blend'))
bpy.ops.object.select_all(action='SELECT')
bpy.context.view_layer.objects.active=root
bpy.ops.export_scene.fbx(filepath=os.path.join(outdir,'GameplayLadder.fbx'), use_selection=True, object_types={'EMPTY','MESH'}, apply_unit_scale=True, axis_forward='-Z', axis_up='Y', bake_space_transform=False, add_leaf_bones=False, path_mode='AUTO')
print('Created GameplayLadder')

