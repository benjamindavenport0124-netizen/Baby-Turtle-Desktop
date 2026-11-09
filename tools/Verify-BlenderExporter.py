"""Run with Blender --factory-startup --disable-autoexec --background --python this_file --python-exit-code 1.
Uses disposable source copies; never saves the bundled Blender project.
"""
import bpy
import hashlib
import json
import os
from pathlib import Path
import runpy
import shutil
import sys
import tempfile

root=Path(__file__).resolve().parent.parent
api=runpy.run_path(str(root/'tools/Export-BlenderModel.py'))
resolve=api['material_color']

def digest(path): return hashlib.sha256(path.read_bytes()).hexdigest()
def expect_error(action,contains):
    try: action()
    except RuntimeError as error:
        assert contains in str(error),str(error)
    else: raise AssertionError('Expected refusal: '+contains)

def material():
    m=bpy.data.materials.new('Disposable material');m.use_nodes=True
    shader=m.node_tree.nodes.get('Principled BSDF')
    shader.inputs['Base Color'].default_value=(.1,.7,.2,1)
    return m,shader

# Disconnected RGB must not override the actual shader.
m,shader=material()
rgb=m.node_tree.nodes.new('ShaderNodeRGB');rgb.outputs[0].default_value=(1,0,0,1)
assert tuple(resolve(m))==tuple(shader.inputs['Base Color'].default_value)
# Supported linked solid RGB follows the actual connection.
rgb.outputs[0].default_value=(.2,.8,.1,1)
m.node_tree.links.new(rgb.outputs[0],shader.inputs['Base Color'])
assert tuple(resolve(m))==tuple(rgb.outputs[0].default_value)
# An inactive output/shader must not override the active output.
extra=m.node_tree.nodes.new('ShaderNodeOutputMaterial')
other=m.node_tree.nodes.new('ShaderNodeBsdfPrincipled')
other.inputs['Base Color'].default_value=(1,0,0,1)
m.node_tree.links.new(other.outputs['BSDF'],extra.inputs['Surface'])
extra.is_active_output=False
original=next(n for n in m.node_tree.nodes if n.type=='OUTPUT_MATERIAL' and n!=extra)
original.is_active_output=True
assert tuple(resolve(m))==tuple(rgb.outputs[0].default_value)
# Mixed shaders are refused rather than guessed.
mix=m.node_tree.nodes.new('ShaderNodeMixShader')
m.node_tree.links.new(shader.outputs['BSDF'],mix.inputs[1])
m.node_tree.links.new(other.outputs['BSDF'],mix.inputs[2])
m.node_tree.links.new(mix.outputs[0],original.inputs['Surface'])
expect_error(lambda:resolve(m),'unsupported')

with tempfile.TemporaryDirectory(prefix='turtle-export-test-') as temporary:
    temp=Path(temporary);source_dir=temp/'input';source_dir.mkdir()
    source=source_dir/'baby-turtle.blend'
    shutil.copyfile(root/'assets/baby-turtle.blend',source)
    bpy.ops.wm.open_mainfile(filepath=str(source))
    before=digest(source)
    sentinel=source_dir/'baby-turtle.mesh.json';sentinel.write_text('unchanged')
    sys.argv=['blender','--','--output-dir',str(source_dir)]
    expect_error(api['main'],'Input would be overwritten')
    assert digest(source)==before and sentinel.read_text()=='unchanged'
    # Rename every material, and add an unused red node to every supported graph.
    expected={}
    for index,m in enumerate(bpy.data.materials):
        m.name='Renamed '+str(index)
        expected[m.name]=tuple(resolve(m))
        if m.use_nodes:
            unused=m.node_tree.nodes.new('ShaderNodeRGB')
            unused.outputs[0].default_value=(1,0,0,1)
        assert tuple(resolve(m))==expected[m.name]
    allowed_colors={tuple(api['srgb'](v) for v in color[:3]) for color in expected.values()}
    output=temp/'exported' 
    sys.argv=['blender','--','--output-dir',str(output)]
    api['main']()
    data=json.loads((output/'baby-turtle.mesh.json').read_text())
    assert len(data['parts'])==6 and data['inputSourceSha256']==before
    assert all(tuple(t['color']) in allowed_colors for part in data['parts'] for t in part['triangles'])
    assert data['preparedSourceSha256']==digest(output/'baby-turtle.blend')
    assert digest(source)==before
    assert data['eyeBackingColor'][1]>data['eyeBackingColor'][0]
    # A failing Blender save must leave both existing artifacts unchanged.
    previous={name:digest(output/name) for name in ('baby-turtle.blend','baby-turtle.mesh.json')}
    class FailingSave:
        def __call__(self,**kwargs): raise RuntimeError('dummy save failure')
    expect_error(lambda:api['publish_pair'](output,data,FailingSave()),'dummy save failure')
    assert previous=={name:digest(output/name) for name in previous}
    # A publication error after the new blend is installed rolls both files back.
    real_replace=os.replace
    def fail_json(source,destination):
        if Path(source).name=='baby-turtle.mesh.json' and Path(destination).parent==output:
            raise OSError('dummy publication failure')
        return real_replace(source,destination)
    os.replace=fail_json
    try:
        try: api['publish_pair'](output,data)
        except OSError as error: assert 'dummy publication failure' in str(error)
        else: raise AssertionError('Expected publication failure')
    finally: os.replace=real_replace
    assert previous=={name:digest(output/name) for name in previous}

print('PASS: input preservation, active/unused/linked/mixed material graphs, renamed materials, paired hashes, failed-save preservation.')
