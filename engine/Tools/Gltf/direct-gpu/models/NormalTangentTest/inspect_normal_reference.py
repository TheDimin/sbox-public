"""Independent source triangle, UV and image arithmetic against a real normal-debug capture.

No renderer evaluator is used. Nearest source texels approximate the native
bilinear/mipmap sample; an eight-byte channel tolerance covers interpolation and
pixel-centre/raster differences. This does not infer the intent of the PNG.
"""
import hashlib, json, math, struct, sys
from pathlib import Path
from PIL import Image

root=Path(__file__).resolve().parents[6]
model=root/'Temp/gltf-reference/glTF-Sample-Assets/Models/NormalTangentTest/glTF'
b=(model/'NormalTangentTest0.bin').read_bytes(); png=(model/'NormalTangentTest_Normal.png').read_bytes()
def sha(x):return hashlib.sha256(x).hexdigest()
assert sha(b)=='2d0fc6d43cb11b61a746bc80218b5e9e6c60dcc40f86d4a3f4fdbf11909cd990'
assert sha(png)=='9cebea6c513ddfb1f5f5c761c08bca1468d2283a3cd9772bba3a57e484862ead'
im=Image.open(model/'NormalTangentTest_Normal.png').convert('RGB'); pixels=Image.open(sys.argv[1]).convert('RGB'); assert pixels.size==(512,483)
indices=struct.unpack_from('<23322H',b,127456)
def vec(offset,i,n):return struct.unpack_from('<'+'f'*n,b,offset+i*n*4)
def unit(a):q=math.sqrt(sum(x*x for x in a));return [x/q for x in a]
def bary(x,y,p):
 a,c,d=p; den=(c[1]-d[1])*(a[0]-d[0])+(d[0]-c[0])*(a[1]-d[1])
 if abs(den)<1e-14:return None
 u=((c[1]-d[1])*(x-d[0])+(d[0]-c[0])*(y-d[1]))/den;v=((d[1]-a[1])*(x-d[0])+(a[0]-d[0])*(y-d[1]))/den;return u,v,1-u-v
results=[]
# Recorded camera is axis-aligned, centred at source y=-.125, with vertical
# extent 2.28m over 483 pixels. Each sample lies away from a triangle edge.
for screen_y in (36,56):
 x=(120-256)*(2.28/483);y=-.125+(241.5-screen_y)*(2.28/483)
 for j in range(0,len(indices),3):
  ii=indices[j:j+3];p=[vec(47796,i,3) for i in ii];n=[vec(0,i,3) for i in ii]
  if not all(a[2]>.999 for a in n):continue
  w=bary(x,y,p)
  if w is None or min(w)<-1e-6:continue
  uv=[vec(95592,i,2) for i in ii];u=sum(w[k]*uv[k][0] for k in range(3));v=sum(w[k]*uv[k][1] for k in range(3));pixel=(round(u*2047),round(v*2047));rgb=im.getpixel(pixel);mapped=unit([a/255*2-1 for a in rgb]);
  e1=[p[1][k]-p[0][k] for k in range(3)];e2=[p[2][k]-p[0][k] for k in range(3)];du1=uv[1][0]-uv[0][0];dv1=uv[1][1]-uv[0][1];du2=uv[2][0]-uv[0][0];dv2=uv[2][1]-uv[0][1];det=du1*dv2-dv1*du2
  if abs(det)<1e-15:continue
  t=unit([(e1[k]*dv2-e2[k]*dv1)/det for k in range(3)]);sign=1 if det>0 else -1;B=[-sign*t[1],sign*t[0],0];source=[mapped[0]*t[k]+mapped[1]*B[k]+mapped[2]*n[0][k] for k in range(3)];engine=[source[2],source[0],source[1]];predicted=[round((c*.5+.5)*255) for c in engine];actual=pixels.getpixel((120,screen_y));error=max(abs(a-z) for a,z in zip(predicted,actual));assert error<=8,(predicted,actual,error)
  results.append({'screen':[120,screen_y],'source_position':[x,y],'triangle':ii,'triangle_corner_offset':j,'uv':[u,v],'nearest_png_pixel':pixel,'source_rgb':rgb,'tangent':t,'sign':sign,'predicted_world_normal':engine,'predicted_rgb':predicted,'observed_gpu_rgb':actual,'max_rgb_byte_error':error});break
 else:raise RuntimeError('Source point not found')
print(json.dumps({'scope':'Independent source arithmetic versus actual native normal visualization; bound texture GetPixels remains pending. Nearest texel is approximate, not a direct shader sampling proof.','buffer_sha256':sha(b),'png_sha256':sha(png),'capture_sha256':sha(Path(sys.argv[1]).read_bytes()),'results':results},indent=2))
