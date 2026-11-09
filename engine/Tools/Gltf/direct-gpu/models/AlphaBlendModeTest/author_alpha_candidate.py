#!/usr/bin/env python3
"""Small alpha delta over Encoding owner's provided Surface graph family."""
import hashlib
import importlib.util
import json
import sys
sys.dont_write_bytecode = True
from pathlib import Path
base = Path(__file__).resolve().parent
shared = base.parent/'TextureEncodingTest/author_surface_orm_candidate.py'
spec = importlib.util.spec_from_file_location('provided_surface_family',shared)
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)
for mode in ('Opaque','Masked','Translucent'):
    graph = module.graph()
    graph['Description'] = 'AlphaBlendModeTest provided Surface candidate: '+mode+'; generated source requires reviewed exact-cutoff/sidedness delta'
    graph['BlendMode'] = mode
    if mode != 'Opaque':
        graph['nodes'] += [
          {'_class':'Float','Identifier':'alpha_factor','Name':'GltfAlphaFactor','Value':1,'Min':0,'Max':0,'IsAttribute':False,'UI':{'Type':'Default'}},
          {'_class':'Saturate','Identifier':'vertex_alpha','Input':{'Identifier':'vertex_color','Output':'Alpha'}},
          {'_class':'Multiply','Identifier':'texture_factor_alpha','A':{'Identifier':'basecolor_effective','Output':'W'},'B':{'Identifier':'alpha_factor','Output':'Result'}},
          {'_class':'Multiply','Identifier':'complete_alpha','A':{'Identifier':'texture_factor_alpha','Output':'Result'},'B':{'Identifier':'vertex_alpha','Output':'Result'}}]
        next(n for n in graph['nodes'] if n['Identifier']=='surface')['Opacity'] = {'Identifier':'complete_alpha','Output':'Result'}
    path=base/('surface_alpha_'+mode.lower()+'.shdrgrph')
    path.write_text(json.dumps(graph,indent=2)+'\n')
    print(path.name,hashlib.sha256(path.read_bytes()).hexdigest())
