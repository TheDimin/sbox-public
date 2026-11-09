using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sandbox;

namespace Sandbox.Gltf.DirectGpu.TextureBenchmark;

[TestClass]
internal sealed class MipEquivalenceTests
{
 [TestMethod]
 public void MipBytesMatchIndependentFormulaForOddAndEvenDimensions()
 {
  var random=new Random(20261106);
  foreach(var size in new[]{(1,1),(2,1),(1,3),(3,5),(4,4),(7,9),(32,17),(257,129)})
  foreach(bool srgb in new[]{false,true})
  {
   int width=size.Item1, height=size.Item2;
   var pixels=new byte[width*height*4]; random.NextBytes(pixels);
   // Include all byte values and alpha channels as well as arbitrary inputs.
   if(pixels.Length>=1024) for(int i=0;i<1024;i++) pixels[i]=(byte)(i/4);
   while(width>1 || height>1)
   {
    var expected=Reference(pixels,width,height,srgb);
    var actual=GltfTextureCompiler.Downsample(pixels,width,height,srgb);
    CollectionAssert.AreEqual(expected,actual,$"{width}x{height} sRGB={srgb}");
    pixels=actual; width=Math.Max(1,width/2); height=Math.Max(1,height/2);
   }
  }
 }

 static byte[] Reference(byte[] source,int width,int height,bool srgb)
 {
  int w=Math.Max(1,width/2),h=Math.Max(1,height/2); var output=new byte[w*h*4];
  for(int y=0;y<h;y++) for(int x=0;x<w;x++) for(int c=0;c<4;c++)
  {
   double sum=0; int n=0;
   for(int sy=y*height/h;sy<(y+1)*height/h;sy++) for(int sx=x*width/w;sx<(x+1)*width/w;sx++)
   {
    double value=source[(sy*width+sx)*4+c]/255.0;
    if(srgb && c<3) value=value<=.04045?value/12.92:Math.Pow((value+.055)/1.055,2.4);
    sum+=value; n++;
   }
   double average=sum/n;
   if(srgb && c<3) average=average<=.0031308?average*12.92:1.055*Math.Pow(average,1/2.4)-.055;
   output[(y*w+x)*4+c]=(byte)Math.Clamp((int)Math.Round(average*255),0,255);
  }
  return output;
 }
}
