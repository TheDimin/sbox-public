#!/usr/bin/env python3
"""Fit a provisional no-roll camera to eight manually read image landmarks.

Landmarks are approximate silhouette top/bottom centers, not measured exact
correspondences. Source positions come from the pinned glTF node translations
and accessor extrema, mapped from glTF meters to engine inches. The output is a
starting camera for native visual iteration, never an appearance/parity score.
"""
import json
import numpy as np

points = np.array([[-8.65,-8.65,4.484],[-8.65,-8.65,.662],[-8.70,-6.225,4.595],[-8.70,-6.225,.653],[8.65,-8.66,4.53],[8.65,-8.66,.709],[-8.568,-3.555,5.536],[-8.568,-3.555,.653]])
pixels = np.array([[224,72],[215,274],[111,61],[115,245],[369,49],[369,132],[33,18],[33,219]])

def residual(t):
    pos=t[:3]; yaw,pitch=t[3:5]; f=np.exp(t[5]); forward=np.array([np.cos(pitch)*np.cos(yaw),np.cos(pitch)*np.sin(yaw),np.sin(pitch)])
    right=np.cross(forward,[0,0,1]); right/=np.linalg.norm(right); up=np.cross(right,forward); v=points-pos; z=v@forward
    if (z<.1).any(): return np.ones(16)*1e4
    return (np.column_stack((200+f*(v@right)/z,155-f*(v@up)/z))-pixels).ravel()

t=np.array([-20.,-12.,5.,.35,-.18,np.log(640.)]); damping=1.
for _ in range(200):
    r=residual(t); jac=np.column_stack([(residual(t+np.eye(6)[j]*1e-5)-r)/1e-5 for j in range(6)]); delta=np.linalg.solve(jac.T@jac+damping*np.eye(6),-jac.T@r); trial=t+delta
    if np.linalg.norm(residual(trial))<np.linalg.norm(r): t=trial; damping=max(1e-6,damping*.5)
    else: damping=min(1e9,damping*4)
yaw,pitch=t[3:5];f=np.exp(t[5]);forward=[np.cos(pitch)*np.cos(yaw),np.cos(pitch)*np.sin(yaw),np.sin(pitch)]
print(json.dumps({'position_inches':t[:3].tolist(),'forward':forward,'vertical_fov_degrees':float(2*np.degrees(np.arctan(155/f))),'landmark_rms_pixels':float(np.linalg.norm(residual(t))/np.sqrt(16)),'pixel_landmarks':pixels.tolist(),'scope':'Approximate manually annotated fit only; native image comparison required'},indent=2))
