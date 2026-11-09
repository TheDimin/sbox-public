#!/usr/bin/env python3
"""Measure whether the selected screenshot is a crop of documented Babylon media."""
import hashlib,json,pathlib
import numpy as np
from PIL import Image
root=pathlib.Path(__file__).resolve().parents[6]/'Temp/gltf-reference/glTF-Sample-Assets/Models/ABeautifulGame/screenshot'
a=root/'screenshot.jpg';b=root/'screenshot-babylonjs.jpg'
def sha(p):return hashlib.sha256(p.read_bytes()).hexdigest()
assert sha(a)=='03da38faeddee145e21a9e2f539f5879c725db7b8ebdf730d6d77345111d4baf'
assert sha(b)=='aaa2a3ed449e067c57117e8b324fb3be89a6ac9e4730ba2b6c21afd9b735d472'
t=np.asarray(Image.open(a).convert('L').resize((200,155)),dtype=np.float64);original=Image.open(b).convert('L');t=t-t.mean();tn=np.sqrt((t*t).sum());best=[]
for k in np.arange(.153,.1621,.001):
 w,h=round(original.width*k),round(original.height*k);im=np.asarray(original.resize((w,h)),dtype=np.float64);shape=(h+154,w+199)
 corr=np.fft.irfftn(np.fft.rfftn(im,s=shape,axes=(0,1))*np.fft.rfftn(t[::-1,::-1],s=shape,axes=(0,1)),s=shape,axes=(0,1))[154:h,199:w]
 integral=np.pad(im.cumsum(0).cumsum(1),((1,0),(1,0)));squares=np.pad((im*im).cumsum(0).cumsum(1),((1,0),(1,0)))
 sm=integral[155:,200:]-integral[:-155,200:]-integral[155:,:-200]+integral[:-155,:-200];sq=squares[155:,200:]-squares[:-155,200:]-squares[155:,:-200]+squares[:-155,:-200]
 corr/=np.sqrt(np.maximum(1e-9,sq-sm*sm/31000))*tn;y,x=np.unravel_index(np.argmax(corr),corr.shape);best.append((float(corr[y,x]),float(k),int(x),int(y)))
score,k,x,y=max(best);assert score>.98
print(json.dumps({'selected_sha256':sha(a),'babylon_sha256':sha(b),'grayscale_normalized_correlation':score,'estimated_original_crop_xywh':[x/k,y/k,200/k,155/k],'method':'200x155 downsample; full translations at scales 0.153..0.162 in steps .001','scope':'Evidence selected screenshot is a crop/rescale of documented Babylon image; not a camera or render parity test'},indent=2))
