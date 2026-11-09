"""Run with Blender --disable-autoexec --background model.blend --python this_file.

Exports the six colored rigid parts for the desktop software renderer and saves
a cleaned animation-source copy. The input .blend is never overwritten.
"""
import bpy
import bmesh
import json
import hashlib
import argparse
import os
import sys
import tempfile
from pathlib import Path
from mathutils import Vector

def material_color(material):
    """Resolve only the active surface's supported solid color, never unused nodes."""
    if material is None:
        raise RuntimeError('Unassigned material')
    if not material.use_nodes:
        return tuple(material.diffuse_color)
    tree=material.node_tree
    engine='CYCLES' if bpy.context.scene.render.engine=='CYCLES' else 'EEVEE'
    outputs=[n for n in tree.nodes if n.type=='OUTPUT_MATERIAL' and n.is_active_output]
    specific=[n for n in outputs if n.target==engine]
    outputs=specific or [n for n in outputs if n.target=='ALL']
    def fail():
        raise RuntimeError('Material '+material.name+': use one active Material Output with a solid Principled Base Color or linked RGB; mixed/textured shader graphs are unsupported')
    def upstream(socket):
        seen=set()
        while True:
            if len(socket.links)!=1: fail()
            link=socket.links[0];node=link.from_node
            if node.as_pointer() in seen: fail()
            seen.add(node.as_pointer())
            if node.type!='REROUTE': return node,link.from_socket
            socket=node.inputs[0]
    if len(outputs)!=1: fail()
    node,socket=upstream(outputs[0].inputs['Surface'])
    if node.type=='RGB': return tuple(socket.default_value)
    if node.type!='BSDF_PRINCIPLED' or socket!=node.outputs['BSDF']: fail()
    base=node.inputs['Base Color']
    if not base.is_linked: return tuple(base.default_value)
    node,socket=upstream(base)
    if node.type!='RGB': fail()
    return tuple(socket.default_value)


def validate_paths(source,assets):
    source=Path(source).resolve()
    if not source.is_file(): raise RuntimeError('Save the input Blender project before exporting')
    for name in ('baby-turtle.blend','baby-turtle.mesh.json'):
        target=assets/name
        if source==target.resolve() or (target.exists() and os.path.samefile(source,target)):
            raise RuntimeError('Input would be overwritten: choose a separate --output-dir (for example --output-dir exported)')
    return source


def publish_pair(assets,model,save_project=None):
    # Stage both outputs before touching either existing artifact, with rollback
    # for ordinary publication errors. This is not a crash-atomic two-file commit.
    assets.mkdir(parents=True,exist_ok=True)
    with tempfile.TemporaryDirectory(prefix='.turtle-export-',dir=assets) as temporary:
        stage=Path(temporary)
        blend=stage/'baby-turtle.blend'
        (save_project or bpy.ops.wm.save_as_mainfile)(filepath=str(blend),copy=True)
        model['preparedSourceSha256']=hashlib.sha256(blend.read_bytes()).hexdigest()
        (stage/'baby-turtle.mesh.json').write_text(json.dumps(model,separators=(',',':')),encoding='utf-8')
        names=('baby-turtle.blend','baby-turtle.mesh.json')
        moved=[];published=[]
        try:
            for name in names:
                destination=assets/name
                if destination.exists():
                    os.replace(destination,stage/(name+'.previous'));moved.append(name)
            for name in names:
                os.replace(stage/name,assets/name);published.append(name)
        except Exception:
            for name in published: (assets/name).unlink()
            for name in moved: os.replace(stage/(name+'.previous'),assets/name)
            raise


def srgb(value):
    value=max(0,min(1,value))
    return round(255*(value*12.92 if value<=.0031308 else 1.055*value**(1/2.4)-.055))

def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--output-dir',type=Path,default=Path(__file__).resolve().parent.parent/'assets')
    arguments=parser.parse_args(sys.argv[sys.argv.index('--')+1:] if '--' in sys.argv else [])
    assets=arguments.output_dir.resolve()
    source=validate_paths(bpy.data.filepath,assets)
    source_hash=hashlib.sha256(source.read_bytes()).hexdigest()
    names=['shell','head','frontleftflipper','frontrightflipper','backleftflipper','backrightflipper']
    parts=[]
    for name in names:
        matches=[o for o in bpy.context.scene.objects if o.type=='MESH' and o.name.lower().replace('_','').replace(' ','')==name]
        if len(matches)!=1: raise RuntimeError('Expected one mesh named '+name)
        parts.append(matches[0])
    linear_colors={m.name:material_color(m) for obj in parts for m in obj.data.materials if m}
    # Validate every assigned slot before changing geometry or writing any output.
    for obj in parts:
        for face in obj.data.polygons:
            if face.material_index>=len(obj.material_slots) or obj.material_slots[face.material_index].material is None:
                raise RuntimeError('Assign a material to every face of '+obj.name)
    if bpy.context.object and bpy.context.object.mode!='OBJECT':
        bpy.ops.object.mode_set(mode='OBJECT')
    def color_for(material): return [srgb(v) for v in linear_colors[material.name][:3]]

    def bounds(obj):
        vertices=[obj.matrix_world@v.co for v in obj.data.vertices]
        return Vector([min(p[i] for p in vertices) for i in range(3)]),Vector([max(p[i] for p in vertices) for i in range(3)])

    shell_min,shell_max=bounds(parts[0]);head_min,_=bounds(parts[1])
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
            eye=index==1 and max(color)<65
            triangles.append({'indices':list(triangle.vertices),'color':color,'eye':eye})
        exported.append({'id':index,'name':obj.name,'pivot':normalize(pivot),'vertices':vertices,'triangles':triangles})
        # Move only the origin in the saved copy; keep all world-space vertices fixed.
        old_matrix=obj.matrix_world.copy();new_matrix=old_matrix.copy();new_matrix.translation=pivot
        transform=new_matrix.inverted()@old_matrix
        for vertex in obj.data.vertices: vertex.co=transform@vertex.co
        obj.matrix_world=new_matrix

    eye_z=[exported[1]['vertices'][i][2] for triangle in exported[1]['triangles'] if triangle['eye'] for i in triangle['indices']]
    if not eye_z: raise RuntimeError('No head eye faces found')
    # Pick the dominant green head surface by area; material names are arbitrary.
    green_areas={}
    for triangle in exported[1]['triangles']:
        color=tuple(triangle['color'])
        if triangle['eye'] or not (color[1]>color[0] and color[1]>color[2]): continue
        a,b,c=[Vector(exported[1]['vertices'][i]) for i in triangle['indices']]
        green_areas[color]=green_areas.get(color,0)+(b-a).cross(c-a).length
    if not green_areas: raise RuntimeError('Head needs a green skin material for eye backing')
    backing=max(green_areas,key=green_areas.get)
    model={'formatVersion':1,'source':'baby-turtle.blend','inputSourceSha256':source_hash,
        'axes':{'forward':'+X','left':'+Y','up':'+Z'},'eyeCenterZ':(min(eye_z)+max(eye_z))/2,
        'eyeBackingColor':list(backing),
        'parts':exported,'capped_attachment_faces':capped}

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
    publish_pair(assets,model)
    print(json.dumps({'parts':[{'name':p['name'],'triangles':len(p['triangles']),'pivot':p['pivot']} for p in exported],
        'material_rgb':{name:[srgb(v) for v in value[:3]] for name,value in linear_colors.items()},'capped_attachment_faces':capped,
        'outputs':[str(assets/'baby-turtle.mesh.json'),str(assets/'baby-turtle.blend')]}))

if __name__=='__main__':
    main()
