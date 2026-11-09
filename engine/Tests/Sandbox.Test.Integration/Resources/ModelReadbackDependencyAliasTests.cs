using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Editor;
using NativeEngine;
using Sandbox.Compression;
namespace ResourceTests;

[TestClass]
public class ModelReadbackDependencyAliasTests
{
	[TestMethod]
	public void RewritesNativeReferenceStringsWithoutChangingNumericLanes()
	{
		var kv=KeyValues3.Create();
		try
		{
			kv.SetMemberString("reference","models/source.vmesh");kv.SetMemberInt("sentinel",314159);
			using var buffer=kv.Save();var original=buffer.ToArray();var held=(byte[])original.Clone();
			byte[] Plain(byte[] data){var result=new byte[BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(48))];if(BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(20))==0)data.AsSpan(64).CopyTo(result);else Assert.AreEqual(result.Length,LZ4.DecompressBlock(data.AsSpan(64),result));return result;}
			var plainOriginal=Plain(original);var packed=LZ4.CompressBlock(plainOriginal);var compressed=new byte[64+packed.Length];original.AsSpan(0,64).CopyTo(compressed);packed.CopyTo(compressed,64);BinaryPrimitives.WriteInt32LittleEndian(compressed.AsSpan(20),1);BinaryPrimitives.WriteUInt32LittleEndian(compressed.AsSpan(24),0x40000000);BinaryPrimitives.WriteInt32LittleEndian(compressed.AsSpan(52),packed.Length);
			foreach(var input in new[]{original,compressed})
			{
				var output=ModelReadbackAlias.RewriteReferenceStrings(input,new Dictionary<string,string>{{"models/source.vmesh","geometrynodes/gib-readback/0123456789abcdef0123456789abcdef/0000.vmesh"}});
				var before=Plain(input);var after=Plain(output);int c1=BinaryPrimitives.ReadInt32LittleEndian(input.AsSpan(28)),c4=BinaryPrimitives.ReadInt32LittleEndian(input.AsSpan(32)),c8=BinaryPrimitives.ReadInt32LittleEndian(input.AsSpan(36));int start=checked(((((c1+3)&~3)+4*c4+7)&~7)+8*c8);
				CollectionAssert.AreEqual(before.Take(start).ToArray(),after.Take(start).ToArray());Assert.AreEqual(BinaryPrimitives.ReadInt32LittleEndian(input.AsSpan(20)),BinaryPrimitives.ReadInt32LittleEndian(output.AsSpan(20)));
				Assert.IsTrue(Encoding.UTF8.GetString(after.AsSpan(start)).Contains("geometrynodes/gib-readback/0123456789abcdef0123456789abcdef/0000.vmesh"));Assert.IsFalse(Encoding.UTF8.GetString(after.AsSpan(start)).Contains("models/source.vmesh"));
				CollectionAssert.AreEqual(input,ModelReadbackAlias.RewriteReferenceStrings(input,new Dictionary<string,string>()));
			}
			CollectionAssert.AreEqual(held,original);var unsupported=(byte[])original.Clone();BinaryPrimitives.WriteInt32LittleEndian(unsupported.AsSpan(20),2);Assert.ThrowsException<System.IO.InvalidDataException>(()=>ModelReadbackAlias.RewriteReferenceStrings(unsupported,new Dictionary<string,string>()));

		}
		finally{kv.DeleteThis();}
	}
	[TestMethod]
	public void RejectsNonPrivateFrozenClosureBeforeWriting()
	{
		Assert.ThrowsException<InvalidOperationException>(()=>ModelReadbackAlias.CreateFrozen(new("outside/source.vmdl",Guid.NewGuid(),new byte[16]),Array.Empty<ModelReadbackAlias.FrozenFile>(),new string('0',64)));
		Assert.ThrowsException<System.IO.InvalidDataException>(()=>ModelReadbackAlias.RewriteReferenceStrings(new byte[64],new Dictionary<string,string>()));
	}
}
