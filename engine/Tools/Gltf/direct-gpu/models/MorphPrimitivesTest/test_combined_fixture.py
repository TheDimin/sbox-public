#!/usr/bin/env python3
"""Independent literal and buffer-layout controls, not GPU evidence."""
import base64,json,math,struct,unittest
from pathlib import Path
D=json.loads((Path(__file__).resolve().parents[6]/'research/gltf-validation/direct-gpu/models/MorphPrimitivesTest') / 'combined-morph-skin.gltf'.read_text()); B=base64.b64decode(D['buffers'][0]['uri'].split(',')[1])
def read(i,v):
 a=D['accessors'][i]; view=D['bufferViews'][a['bufferView']]; count={'VEC3':3,'VEC4':4}[a['type']]; fmt={5126:'f',5121:'B'}[a['componentType']]; start=view.get('byteOffset',0)+a.get('byteOffset',0)+v*view['byteStride']; return struct.unpack_from('<'+fmt*count,B,start)
def distance(x,y): return math.sqrt(sum((a-b)**2 for a,b in zip(x,y)))
class Fixture(unittest.TestCase):
 def test_layout_and_semantics(self):
  p=D['meshes'][0]['primitives'][0]; self.assertEqual([.5,-.25],D['meshes'][0]['weights']);self.assertEqual([1],D['skins'][0]['joints']);self.assertEqual(2,len(p['targets']))
  for a in D['accessors']: self.assertEqual(4,a['byteOffset']);self.assertGreater(D['bufferViews'][a['bufferView']]['byteStride'],4)
  self.assertEqual((1.,0.,0.),read(p['attributes']['POSITION'],1));self.assertEqual((0.,0.,1.),read(p['attributes']['NORMAL'],0));self.assertEqual((1.,0.,0.,-1.),read(p['attributes']['TANGENT'],0));self.assertEqual((255,0,0,0),read(p['attributes']['WEIGHTS_0'],0));self.assertTrue(D['accessors'][p['attributes']['WEIGHTS_0']]['normalized'])
  self.assertEqual([[(1.,0.,0.),(1.,0.,0.),(0.,1.,0.)],[(0.,1.,0.),(0.,1.,0.),(0.,0.,1.)]],[[read(t[s],0) for s in ('POSITION','NORMAL','TANGENT')] for t in p['targets']])
 def test_wrong_order_sensitive(self):
  for a,b in ((.5,-.25),(-.75,1.5)):
   expected=(-b,a,0);wrong=(a,b,0);self.assertGreater(distance(expected,wrong)/.0254,30)
if __name__=='__main__':unittest.main()
