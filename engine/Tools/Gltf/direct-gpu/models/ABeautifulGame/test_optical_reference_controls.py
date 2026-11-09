#!/usr/bin/env python3
"""Literal independent BTDF/refraction/attenuation expectations, no engine code."""
import math, unittest

def refract(incidence,normal,eta):
 dot=sum(a*b for a,b in zip(incidence,normal));k=1-eta*eta*(1-dot*dot)
 if k<0:return (0.,0.,0.)
 return tuple(eta*i-(eta*dot+math.sqrt(k))*n for i,n in zip(incidence,normal))
def beer(color, distance, path):
 if path==0 or math.isinf(distance):return (1.,1.,1.)
 return tuple(c**(path/distance) for c in color)
def lobe(diffuse,specular,backdrop,base,transmission,metallic,fresnel):
 return tuple(d*(1-transmission)+s+b*c*transmission*(1-metallic)*(1-fresnel) for d,s,b,c in zip(diffuse,specular,backdrop,base))

class IndependentOpticalControls(unittest.TestCase):
 def test_thin_unchanged_screen_direction_and_reflection(self):
  zero=lobe((.2,.3,.4),(.04,.04,.04),(.9,.8,.7),(1,1,1),0,0,.04)
  one=lobe((.2,.3,.4),(.04,.04,.04),(.9,.8,.7),(1,1,1),1,0,.04)
  for got,want in zip(zero,(.24,.34,.44)):self.assertAlmostEqual(got,want,places=12)
  for got,want in zip(one,(.904,.808,.712)):self.assertAlmostEqual(got,want,places=12)
  self.assertGreater(min(one),.04)
 def test_metal_blocks_background_and_preserves_specular(self):
  x=lobe((0,0,0),(.7,.5,.3),(1,1,1),(1,1,1),1,1,.04);self.assertEqual(x,(.7,.5,.3))
 def test_ior_15_oblique_vector(self):
  # incidence 60 degrees to outward normal; Snell gives sin(theta_t)=sqrt(3)/3.
  x,y,z=refract((math.sqrt(3)/2,0,-.5),(0,0,1),2/3)
  self.assertAlmostEqual(x,math.sqrt(3)/3,places=12);self.assertEqual(y,0);self.assertAlmostEqual(z,-math.sqrt(2/3),places=12)
  self.assertGreater(abs(x-math.sqrt(3)/2),.28) # wrong IOR=1 negative
 def test_beer_world_path_and_zero_channel(self):
  c=(0,.5,.8);self.assertEqual(beer(c,math.inf,4),(1,1,1));self.assertEqual(beer(c,1,0),(1,1,1));self.assertEqual(beer(c,2,2),c);
  for got,want in zip(beer(c,2,4),(0,.25,.64)):self.assertAlmostEqual(got,want,places=12)
 def test_direction_sensitive_rotated_nonuniform_scale(self):
  # Local X has scale 2 and rotates onto world +Y; local Y has scale 3
  # and rotates onto world -X. A 1 m thickness spans 2 or 3 m respectively.
  inverse_rows=((0,.5,0),(-1/3,0,0),(0,0,1/4))
  def distance(direction):
   local=tuple(sum(row[i]*direction[i] for i in range(3)) for row in inverse_rows)
   return 1/math.sqrt(sum(x*x for x in local))
  self.assertAlmostEqual(distance((0,1,0)),2,places=12)
  self.assertAlmostEqual(distance((-1,0,0)),3,places=12)
  self.assertAlmostEqual(distance((0,0,1)),4,places=12)
  self.assertNotEqual(distance((0,1,0)),3) # wrong unrotated componentwise scale
 def test_texture_channels_not_interchangeable(self):
  tex=(.25,.75,.5,1);self.assertEqual(.4*tex[0],.1);self.assertAlmostEqual(.8*tex[1],.6);self.assertNotEqual(tex[0],tex[1])
if __name__=='__main__':unittest.main(verbosity=2)
