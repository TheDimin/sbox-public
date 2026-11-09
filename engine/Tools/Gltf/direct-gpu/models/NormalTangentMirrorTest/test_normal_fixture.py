import base64
import math
import struct
import unittest
from author_normal_fixture import build, expected, TEXELS


class NormalFixtureTests(unittest.TestCase):
    def test_baked_accessor_matches_independent_literal_directions(self):
        document,oracle=build(True)
        raw=base64.b64decode(document['buffers'][0]['uri'].split(',')[1])
        accessor=document['accessors'][1]; view=document['bufferViews'][accessor['bufferView']]
        for cell in oracle['cells']:
            n=struct.unpack_from('<3f',raw,view['byteOffset']+cell['cell']*4*12)
            actual=[n[2],n[0],n[1]]
            self.assertLess(max(abs(a-b) for a,b in zip(actual,cell['front_engine_normal'])),6e-8)
        self.assertNotIn('normalTexture',document['materials'][0])

    def test_uv_override_cannot_silently_use_uv0(self):
        document,_=build()
        normal=document['materials'][0]['normalTexture']
        self.assertEqual(normal['extensions']['KHR_texture_transform'],{'texCoord':1,'offset':[0,1],'scale':[1,-1]})
        self.assertEqual(normal['scale'],.5)
        self.assertEqual(document['samplers'][0]['magFilter'],9728)

    def test_green_and_v_mistakes_do_not_cancel(self):
        for name,mutate in [('green',lambda i:(TEXELS[i][0],255-TEXELS[i][1],TEXELS[i][2])),
                           ('vflip',lambda i:TEXELS[i^2]),
                           ('both',lambda i:(TEXELS[i^2][0],255-TEXELS[i^2][1],TEXELS[i^2][2]))]:
            self.assertGreater(max(max(abs(a-b) for a,b in zip(expected(TEXELS[i],w),expected(mutate(i),w))) for i in range(4) for w in (-1,1)),.2,name)

    def test_wrong_tangent_w_and_scale_are_independently_sensitive(self):
        self.assertGreater(max(max(abs(a-b) for a,b in zip(expected(TEXELS[i],-1),expected(TEXELS[i],1))) for i in range(4)),.4)
        self.assertGreater(max(max(abs(a-b) for a,b in zip(expected(TEXELS[i],1),expected(TEXELS[i],1,scale=1))) for i in range(4)),.2)

    def test_backface_reverses_all_three_world_components(self):
        for texel in TEXELS:
            for w in (-1,1):
                front=expected(texel,w); back=expected(texel,w,True)
                self.assertEqual(back,[-x for x in front])
                self.assertTrue(all(abs(x)>.05 for x in front))


if __name__=='__main__': unittest.main()
