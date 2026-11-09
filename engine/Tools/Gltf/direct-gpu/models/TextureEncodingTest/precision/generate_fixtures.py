"""Independent literal PNG encoder used only to author deterministic decoder fixtures."""
import base64, json, pathlib, struct, zlib
out=(pathlib.Path(__file__).resolve().parents[7]/'research/gltf-validation/direct-gpu/models/TextureEncodingTest/precision') / 'managed-fixtures.json'
passes=((0,0,8,8),(4,0,8,8),(0,4,4,8),(2,0,4,4),(0,2,2,4),(1,0,2,2),(0,1,1,2))
def chunk(kind,data): return struct.pack('>I',len(data))+kind+data+struct.pack('>I',zlib.crc32(kind+data))
def paeth(a,b,c):
 p=a+b-c; d=[abs(p-a),abs(p-b),abs(p-c)];return (a,b,c)[d.index(min(d))]
def make(typ,filt,inter,trns=False):
 w,h=9,7;count={0:1,2:3,4:2,6:4}[typ]
 def vals(x,y):
  r=(257+x*109+y*1033)%65536;g=(258+x*113+y*911)%65536;b=(32769+x*107+y*797)%65536;a=(12345+x*73+y*509)%65536
  if trns and x==1 and y==1:r,g,b=257,258,32769
  return {0:(r,),2:(r,g,b),4:(r,a),6:(r,g,b,a)}[typ]
 expected=[]
 for y in range(h):
  for x in range(w):
   v=vals(x,y);r=v[0];g=v[1] if typ in (2,6) else r;b=v[2] if typ in (2,6) else r;a=v[-1] if typ in (4,6) else (0 if trns and (v==(257,) if typ==0 else v==(257,258,32769)) else 65535)
   expected.extend((r,g,b,a))
 rows=bytearray()
 for sx,sy,dx,dy in passes if inter else ((0,0,1,1),):
  xs=list(range(sx,w,dx));ys=list(range(sy,h,dy));prev=bytes(len(xs)*count*2)
  if not xs:continue
  for y in ys:
   raw=b''.join(struct.pack('>'+str(count)+'H',*vals(x,y)) for x in xs);encoded=bytearray()
   for i,v in enumerate(raw):
    a=raw[i-count*2] if i>=count*2 else 0;b=prev[i];c=prev[i-count*2] if i>=count*2 else 0
    predictor=(0,a,b,(a+b)//2,paeth(a,b,c))[filt];encoded.append((v-predictor)&255)
   rows.append(filt);rows+=encoded;prev=raw
 png=b'\x89PNG\r\n\x1a\n'+chunk(b'IHDR',struct.pack('>IIBBBBB',w,h,16,typ,0,0,int(inter)))
 if trns:png+=chunk(b'tRNS',struct.pack('>H',257) if typ==0 else struct.pack('>HHH',257,258,32769))
 payload=zlib.compress(rows); split=len(payload)//2; png+=chunk(b'IDAT',payload[:split])+chunk(b'IDAT',payload[split:])+chunk(b'IEND',b'')
 return {'name':f'type{typ}_filter{filt}_interlace{int(inter)}_trns{int(trns)}','png':base64.b64encode(png).decode(),'expected':expected}
fixtures=[make(t,f,i) for t in (0,2,4,6) for f in range(5) for i in (False,True)] + [make(t,4,True,True) for t in (0,2)]
out.write_text(json.dumps(fixtures,separators=(',',':'))+'\n')
print(len(fixtures),out)
