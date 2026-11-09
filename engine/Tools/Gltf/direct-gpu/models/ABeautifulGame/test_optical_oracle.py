#!/usr/bin/env python3
"""Independent pinned sample and transmission/volume expectations; no engine code.

Optical transmission is a BTDF layered under dielectric reflection, not alpha
coverage. Volume thickness is in mesh coordinates; attenuationDistance is in
world-space meters. An absent attenuationDistance is infinity, so this sample's
0.8 attenuationColor does not itself tint the medium. See KHR extension specs.
"""
import hashlib,json,math,pathlib,struct,unittest
ROOT=pathlib.Path(__file__).resolve().parents[6]
MODEL=ROOT/'Temp/gltf-reference/glTF-Sample-Assets/Models/ABeautifulGame'
FORMS=[('glTF','ABeautifulGame.gltf','2e1b63ae34173c6e3251f749b1b8b97a32e92517aab2cfd4bfffd9158f4dd278'),('glTF-Binary','ABeautifulGame.glb','bd7133b4b322aae97c589b8839dae8155ad2546acb35ae32a127e722a959d007'),('glTF-Binary-KTX-ETC1S-Draco','ABeautifulGame.glb','d950162d41e0064be9dfac0c09d80c7a23719a37af37f08cd79385b8af0af0c1')]
def load(v,name,sha):
 b=(MODEL/v/name).read_bytes();assert hashlib.sha256(b).hexdigest()==sha
 if name.endswith('.glb'): n=struct.unpack_from('<I',b,12)[0];b=b[20:20+n]
 return json.loads(b)
def attenuation(color,distance,traveled):
 if math.isinf(distance):return (1.,1.,1.)
 return tuple(x**(traveled/distance) for x in color)
def f0(ior):return ((ior-1)/(ior+1))**2
class OpticalOracle(unittest.TestCase):
 def test_two_authored_pawn_materials_in_all_real_forms(self):
  for v,name,sha in FORMS:
   with self.subTest(form=v):
    j=load(v,name,sha);mats=j['materials'];self.assertEqual(len(mats),15)
    for index,color in [(5,[1.,1.,.828,1.]),(7,[.2995,.5,.45,1.])]:
     m=mats[index]; self.assertEqual(m['name'],['Pawn_Top_White','Pawn_Top_Black'][index==7]); self.assertEqual(m.get('alphaMode','OPAQUE'),'OPAQUE');self.assertFalse(m.get('doubleSided',False))
     ext=m['extensions']; t=ext['KHR_materials_transmission'];vdata=ext['KHR_materials_volume'];self.assertEqual(t.get('transmissionFactor',0),1);self.assertNotIn('transmissionTexture',t);self.assertAlmostEqual(vdata.get('thicknessFactor',0),.22,places=6);self.assertNotIn('thicknessTexture',vdata);self.assertNotIn('attenuationDistance',vdata)
     for x in vdata['attenuationColor']:self.assertAlmostEqual(x,.8,places=6)
     for x,y in zip(m['pbrMetallicRoughness']['baseColorFactor'],color):self.assertAlmostEqual(x,y,places=6)
     self.assertNotIn('KHR_materials_ior',ext);self.assertIn('normalTexture',m);self.assertIn('metallicRoughnessTexture',m['pbrMetallicRoughness'])
    tops=[x for x in j['nodes'] if x.get('name','').startswith('Pawn_Top_')];self.assertEqual(len(tops),16)
    bodies=[x for x in j['nodes'] if x.get('name','').startswith('Pawn_Body_')];self.assertEqual(len(bodies),16)
    self.assertTrue(all('scale' not in x and 'matrix' not in x for x in tops+bodies))
 def test_defaults_and_wrong_tint_negative(self):
  self.assertEqual(attenuation((.8,.8,.8),math.inf,.22),(1.,1.,1.));self.assertNotEqual(attenuation((.8,.8,.8),math.inf,.22),(.8,.8,.8))
  self.assertEqual(attenuation((.8,.8,.8),1.,0.),(1.,1.,1.))
  for x in attenuation((.8,.8,.8),1.,1.):self.assertAlmostEqual(x,.8)
  for x in attenuation((.8,.8,.8),1.,2.):self.assertAlmostEqual(x,.64)
 def test_default_ior_and_reflection_are_not_alpha_coverage(self):
  self.assertAlmostEqual(f0(1.5),.04);self.assertGreater(f0(1.5),0)
if __name__=='__main__':unittest.main(verbosity=2)
