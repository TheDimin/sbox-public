using System.Reflection;
using Sandbox.Gltf.DirectGpu.ABeautifulGameValidation;
using Microsoft.VisualStudio.TestTools.UnitTesting;

var test = new OpticalReaderTests(); int passed=0, failed=0;
foreach (var method in typeof(OpticalReaderTests).GetMethods().Where(m=>m.GetCustomAttribute<TestMethodAttribute>()!=null))
{
 var rows=method.GetCustomAttributes<DataRowAttribute>().ToArray();
 foreach(var row in rows.Length==0 ? new[]{new DataRowAttribute()} : rows)
 {
  try { method.Invoke(test,row.Values); Console.WriteLine($"PASS {method.Name} [{string.Join(",",row.Values)}]"); passed++; }
  catch(Exception error){failed++;Console.WriteLine($"FAIL {method.Name}: {(error.InnerException??error)}");}
 }
}
Console.WriteLine($"TOTAL passed={passed} failed={failed}");return failed==0?0:1;

namespace Microsoft.VisualStudio.TestTools.UnitTesting
{
 [AttributeUsage(AttributeTargets.Class)] sealed class TestClassAttribute:Attribute { }
 [AttributeUsage(AttributeTargets.Method)] sealed class TestMethodAttribute:Attribute { }
 [AttributeUsage(AttributeTargets.Method,AllowMultiple=true)] sealed class DataRowAttribute(params object[] values):Attribute { public object[] Values=values; }
 static class Assert
 {
  public static void IsTrue(bool x){if(!x)throw new Exception("expected true");}
  public static void IsFalse(bool x){if(x)throw new Exception("expected false");}
  public static void IsNull(object x){if(x!=null)throw new Exception("expected null");}
  public static void AreEqual<T>(T x,T y){if(!EqualityComparer<T>.Default.Equals(x,y))throw new Exception($"expected {x}, actual {y}");}
  public static T ThrowsException<T>(Action action) where T:Exception {try{action();}catch(T e){return e;}throw new Exception($"expected {typeof(T).Name}");}
 }
}
