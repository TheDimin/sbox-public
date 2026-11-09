using System;
using System.Buffers.Binary;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sandbox.Gltf;

namespace Sandbox.Gltf.DirectGpu.TextureEncodingTestValidation;

[TestClass]
public sealed class Png16BudgetBoundaryTests
{
	[TestMethod]
	public void RepresentativeDimensionsHonorWorkingSetBudget()
	{
		var method = typeof( global::Sandbox.GltfTextureCompiler ).GetMethod( "ValidatePng16WorkingSet", BindingFlags.NonPublic | BindingFlags.Static );
		Assert.IsNotNull( method );
		byte[] Header( int size )
		{
			byte[] bytes = [137,80,78,71,13,10,26,10, 0,0,0,13, 73,72,68,82, 0,0,0,0, 0,0,0,0, 16,6,0,0,0];
			BinaryPrimitives.WriteUInt32BigEndian( bytes.AsSpan( 16 ), (uint)size ); BinaryPrimitives.WriteUInt32BigEndian( bytes.AsSpan( 20 ), (uint)size ); return bytes;
		}
		void Accept( int size, bool mips, long budget ) => method.Invoke( null, [Header( size ), mips, budget] );
		void Reject( int size, bool mips, long budget )
		{
			try { Accept( size, mips, budget ); Assert.Fail( "Budget should reject image dimensions." ); }
			catch ( TargetInvocationException e ) when ( e.InnerException is GltfException ) { }
		}
		Accept( 1024, true, 256L * 1024 * 1024 );
		Reject( 2048, false, 256L * 1024 * 1024 );
		Reject( 2048, true, 256L * 1024 * 1024 );
		Accept( 2048, true, 1024L * 1024 * 1024 );
	}
}
