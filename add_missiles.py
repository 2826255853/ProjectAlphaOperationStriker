import bpy, math
from mathutils import Vector
bpy.ops.wm.open_mainfile(filepath=r'C:\Unity Project\ProjectAlphaOperationStriker\Trap_Base_Disc_Dual_Launcher.blend')
# collections
loaded=bpy.data.collections.get('Loaded_Missiles') or bpy.data.collections.new('Loaded_Missiles')
if loaded.name not in bpy.context.scene.collection.children: bpy.context.scene.collection.children.link(loaded)
# materials
def getmat(name,color,metal=0.5,rough=0.3):
 m=bpy.data.materials.get(name) or bpy.data.materials.new(name); m.diffuse_color=(*color,1); m.metallic=metal; m.roughness=rough; return m
bodymat=getmat('Missile Body',(0.22,0.25,0.29),0.75,0.25)
nosemat=getmat('Missile Nose',(0.38,0.42,0.48),0.65,0.22)
finmat=getmat('Missile Fins',(0.7,0.16,0.035),0.45,0.3)
# helper move object to collection
def move_to(obj,col):
 for c in list(obj.users_collection): c.objects.unlink(obj)
 col.objects.link(obj)
def missile(idx,y):
 root=bpy.data.objects.new(f'Missile_{idx:02d}',None); loaded.objects.link(root); root.empty_display_type='PLAIN_AXES'; root.location=(0,0,0)
 bpy.ops.mesh.primitive_cylinder_add(vertices=48, radius=0.16, depth=2.05, location=(0,y,2.53), rotation=(0,math.radians(90),0))
 b=bpy.context.object; b.name=f'Missile_{idx:02d}_Body'; b.data.materials.append(bodymat); move_to(b,loaded); b.parent=root
 bev=b.modifiers.new('Body bevel','BEVEL'); bev.width=0.05; bev.segments=3; bpy.context.view_layer.objects.active=b; bpy.ops.object.modifier_apply(modifier=bev.name)
 bpy.ops.mesh.primitive_uv_sphere_add(segments=48, ring_count=24, location=(1.12,y,2.53), scale=(0.34,0.16,0.16))
 n=bpy.context.object; n.name=f'Missile_{idx:02d}_Rounded_Nose'; n.data.materials.append(nosemat); move_to(n,loaded); n.parent=root
 # tail fins, four around X axis, thin wedges as cubes
 for j,(dy,dz) in enumerate(((0,0.22),(0,-0.22),(0.22,0),(-0.22,0)),1):
  # fin plate centered near rear, oriented according to radial direction
  bpy.ops.mesh.primitive_cube_add(location=(-0.82,y+dy,2.53+dz), scale=(0.28,0.035 if dz else 0.12,0.12 if dz else 0.035))
  f=bpy.context.object; f.name=f'Missile_{idx:02d}_TailFin_{j}'; f.data.materials.append(finmat); move_to(f,loaded); f.parent=root
  bev=f.modifiers.new('Fin bevel','BEVEL'); bev.width=0.025; bev.segments=2; bpy.context.view_layer.objects.active=f; bpy.ops.object.modifier_apply(modifier=bev.name)
 return root
missile(1,-0.23); missile(2,0.23)
# reserve state markers (non-rendering empties)
states=bpy.data.collections.get('Missile_State_Markers') or bpy.data.collections.new('Missile_State_Markers')
if states.name not in bpy.context.scene.collection.children: bpy.context.scene.collection.children.link(states)
for name,desc in [('State_One_Missile_Remaining','Hide Missile_02 for one remaining'),('State_Empty_After_Firing','Hide both missiles after firing')]:
 e=bpy.data.objects.new(name,None); states.objects.link(e); e.empty_display_type='CIRCLE'; e.hide_render=True; e.hide_viewport=True; e['usage']=desc
bpy.ops.wm.save_as_mainfile(filepath=r'C:\Unity Project\ProjectAlphaOperationStriker\Trap_Base_Disc_Dual_Launcher_Loaded.blend')

