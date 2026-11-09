"""Run with Blender --disable-autoexec --background model.blend --python this_file.

Exports the six colored rigid parts for the desktop software renderer and saves
a cleaned animation-source copy. The input .blend is never overwritten.
"""
import bpy
import bmesh
import json
import math
import hashlib
from pathlib import Path
from mathutils import Vector

root=Path(__file__).resolve().parent.parent
assets=root/'assets'
assets.mkdir(exist_ok=True)
source=Path(bpy.data.filepath)
source_hash=hashlib.sha256(source.read_bytes()).hexdigest()
if bpy.context.object and bpy.context.object.mode!='OBJECT':
    bpy.ops.object.mode_set(mode='OBJECT')
aliases=[('shell',0),('head',1),('frontleftflipper',2),('frontrightflipper',3),('backleftflipper',4),('backrightflipper',5)]
parts=[]
for name,part_id in aliases:
    matches=[o for o in bpy.context.scene.objects if o.type=='MESH' and o.name.lower().replace('_','').replace(' ','')==name]
    if len(matches)!=1: raise RuntimeError('Expected one mesh named '+name)
    parts.append(matches[0])

def material_color(material):
    if material is None: raise RuntimeError('Unassigned material')
    tree=material.node_tree
    if tree:
        for node in tree.nodes:
            if node.type=='RGB': return tuple(node.outputs[0].default_value)
        for node in tree.nodes:
            if node.type=='BSDF_PRINCIPLED' and not node.inputs['Base Color'].is_linked:
                return tuple(node.inputs['Base Color'].default_value)
        raise RuntimeError('Unsupported color nodes in '+material.name)
    return tuple(material.diffuse_color)

def srgb(value):
    value=max(0,min(1,value))
    return round(255*(value*12.92 if value<=.0031308 else 1.055*value**(1/2.4)-.055))

linear_colors={m.name:material_color(m) for m in bpy.data.materials if m.users}
def color_for(material): return [srgb(v) for v in linear_colors[material.name][:3]]

def bounds(obj):
    vertices=[obj.matrix_world@v.co for v in obj.data.vertices]
    return Vector([min(p[i] for p in vertices) for i in range(3)]),Vector([max(p[i] for p in vertices) for i in range(3)])

shell_min,shell_max=bounds(parts[0]);head_min,head_max=bounds(parts[1])
all_bounds=[bounds(o) for o in parts]
floor=min(pair[0].z for pair in all_bounds)
scale=1.18/(max(pair[1].x for pair in all_bounds)-min(pair[0].x for pair in all_bounds))
center_y=(shell_min.y+shell_max.y)/2
def normalize(p): return [(p.x-head_min.x)*scale+.1,(p.y-center_y)*scale,(p.z-floor)*scale+.025]

exported=[];capped={}
for index,obj in enumerate(parts):
    low,high=bounds(obj)
    if index==0: pivot=(low+high)/2
    elif index==1: pivot=Vector((low.x,(low.y+high.y)/2,(low.z+high.z)/2))
    else: pivot=Vector(((low.x+high.x)/2,low.y if index%2==0 else high.y,(low.z+high.z)/2))
    mesh=bmesh.new();mesh.from_mesh(obj.data)
    boundary=[edge for edge in mesh.edges if edge.is_boundary]
    if boundary:
        result=bmesh.ops.holes_fill(mesh,edges=boundary,sides=0)
        for face in result.get('faces',[]):
            face.material_index=0
        capped[obj.name]=len(result.get('faces',[]))
    bmesh.ops.recalc_face_normals(mesh,faces=list(mesh.faces))
    mesh.to_mesh(obj.data);mesh.free();obj.data.update()
    obj.data.calc_loop_triangles()
    vertices=[normalize(obj.matrix_world@v.co) for v in obj.data.vertices]
    triangles=[]
    for triangle in obj.data.loop_triangles:
        material=obj.material_slots[triangle.material_index].material
        color=color_for(material)
        eye=index==1 and (material.name.lower()=='black' or max(color)<65)
        triangles.append({'indices':list(triangle.vertices),'color':color,'eye':eye})
    exported.append({'id':index,'name':obj.name,'pivot':normalize(pivot),'vertices':vertices,'triangles':triangles})
    # Move only the origin in the saved copy; keep all world-space vertices fixed.
    old_matrix=obj.matrix_world.copy();new_matrix=old_matrix.copy();new_matrix.translation=pivot
    transform=new_matrix.inverted()@old_matrix
    for vertex in obj.data.vertices: vertex.co=transform@vertex.co
    obj.matrix_world=new_matrix

eye_z=[exported[1]['vertices'][i][2] for triangle in exported[1]['triangles'] if triangle['eye'] for i in triangle['indices']]
if not eye_z: raise RuntimeError('No head eye faces found')
model={'formatVersion':1,'source':'baby-turtle.blend','sourceSha256':source_hash,
    'axes':{'forward':'+X','left':'+Y','up':'+Z'},'eyeCenterZ':(min(eye_z)+max(eye_z))/2,
    'eyeBackingColor':color_for(next(m for m in bpy.data.materials if m.name=='middlegreen')),
    'parts':exported,'capped_attachment_faces':capped}
(assets/'baby-turtle.mesh.json').write_text(json.dumps(model,separators=(',',':')),encoding='utf-8')

# Build ordinary editable flat-color materials in the source copy. This also
# keeps future exports from depending on the RGB-to-output node shortcut.
for material in bpy.data.materials:
    if material.name not in linear_colors: continue
    value=linear_colors[material.name]
    material.diffuse_color=value
    material.use_nodes=True
    nodes=material.node_tree.nodes;nodes.clear()
    output=nodes.new('ShaderNodeOutputMaterial');shader=nodes.new('ShaderNodeBsdfPrincipled')
    shader.inputs['Base Color'].default_value=value;shader.inputs['Roughness'].default_value=1
    material.node_tree.links.new(shader.outputs['BSDF'],output.inputs['Surface'])
for obj in list(bpy.data.objects):
    if obj not in parts: bpy.data.objects.remove(obj,do_unlink=True)
for image in list(bpy.data.images): bpy.data.images.remove(image,do_unlink=True)
for material in list(bpy.data.materials):
    if not material.users: bpy.data.materials.remove(material)
bpy.ops.wm.save_as_mainfile(filepath=str(assets/'baby-turtle.blend'),copy=True)
print(json.dumps({'parts':[{'name':p['name'],'triangles':len(p['triangles']),'pivot':p['pivot']} for p in exported],
    'material_rgb':{name:[srgb(v) for v in value[:3]] for name,value in linear_colors.items()},'capped_attachment_faces':capped,
    'outputs':['assets/baby-turtle.mesh.json','assets/baby-turtle.blend']}))
