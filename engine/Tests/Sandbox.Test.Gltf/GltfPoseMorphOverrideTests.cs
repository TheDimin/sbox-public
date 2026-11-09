using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sandbox.Gltf;

namespace Sandbox.Tests.Gltf;

using Assert = Microsoft.VisualStudio.TestTools.UnitTesting.Assert;

/// <summary>Immutable per-instance manual morph snapshots, independent of native controllers.</summary>
[TestClass]
public class GltfPoseMorphOverrideTests
{
	[TestMethod]
	public void OneNodeOverridePreservesOtherNodesAndSourceDefaults()
	{
		using var document = Read(); var original = document.EvaluatePose();
		var updated = Override( original, new Dictionary<int, float[]> { [0] = [-2, 3] } );
		Assert.AreNotSame( original, updated );
		CollectionAssert.AreEqual( new[] { -2f, 3f }, updated.GetMorphWeights( 0 ) );
		CollectionAssert.AreEqual( new[] { .5f, .125f }, updated.GetMorphWeights( 1 ) );
		CollectionAssert.AreEqual( new[] { .25f, .75f }, original.GetMorphWeights( 0 ) );
		CollectionAssert.AreEqual( new[] { .25f, .75f }, document.GetMorphWeights( 0 ) );
	}

	[TestMethod]
	public void EveryWorldMatrixIsPreservedExactly()
	{
		using var document = Read(); var original = document.EvaluatePose();
		var updated = Override( original, new Dictionary<int, float[]> { [1] = [-4, 5] } );
		for ( var node = 0; node < 3; node++ ) Assert.AreEqual( original.GetWorldTransform( node ), updated.GetWorldTransform( node ) );
		var valueCopy = updated.GetWorldTransform( 1 ); valueCopy.M41 = 999;
		Assert.AreNotEqual( valueCopy, updated.GetWorldTransform( 1 ) );
	}

	[TestMethod]
	public void InputDictionaryAndArraysCannotMutateReturnedSnapshot()
	{
		using var document = Read(); var original = document.EvaluatePose();
		var weights = new[] { -2f, 3f }; var inputs = new Dictionary<int, float[]> { [0] = weights };
		var updated = Override( original, inputs );
		weights[0] = 99; inputs[0] = [88, 77]; inputs.Clear();
		CollectionAssert.AreEqual( new[] { -2f, 3f }, updated.GetMorphWeights( 0 ) );
		CollectionAssert.AreEqual( new[] { .25f, .75f }, original.GetMorphWeights( 0 ) );
	}

	[TestMethod]
	public void ReturnedWeightArraysDoNotLeakMutableSnapshotState()
	{
		using var document = Read(); var original = document.EvaluatePose();
		var updated = Override( original, new Dictionary<int, float[]> { [0] = [-2, 3] } );
		var exposed = updated.GetMorphWeights( 0 ); exposed[0] = 77;
		var other = updated.GetMorphWeights( 1 ); other[0] = 66;
		CollectionAssert.AreEqual( new[] { -2f, 3f }, updated.GetMorphWeights( 0 ) );
		CollectionAssert.AreEqual( new[] { .5f, .125f }, updated.GetMorphWeights( 1 ) );
		CollectionAssert.AreEqual( new[] { .5f, .125f }, original.GetMorphWeights( 1 ) );
	}

	[TestMethod]
	public void ChainedOverridesKeepAllEarlierPosesIndependent()
	{
		using var document = Read(); var original = document.EvaluatePose();
		var first = Override( original, new Dictionary<int, float[]> { [0] = [-2, 3] } );
		var second = Override( first, new Dictionary<int, float[]> { [1] = [-4, 5] } );
		var third = Override( second, new Dictionary<int, float[]> { [0] = [6, -7] } );
		CollectionAssert.AreEqual( new[] { .25f, .75f }, original.GetMorphWeights( 0 ) );
		CollectionAssert.AreEqual( new[] { -2f, 3f }, first.GetMorphWeights( 0 ) );
		CollectionAssert.AreEqual( new[] { .5f, .125f }, first.GetMorphWeights( 1 ) );
		CollectionAssert.AreEqual( new[] { -2f, 3f }, second.GetMorphWeights( 0 ) );
		CollectionAssert.AreEqual( new[] { -4f, 5f }, second.GetMorphWeights( 1 ) );
		CollectionAssert.AreEqual( new[] { 6f, -7f }, third.GetMorphWeights( 0 ) );
		CollectionAssert.AreEqual( new[] { -4f, 5f }, third.GetMorphWeights( 1 ) );
	}

	[TestMethod]
	public void MultipleNodeOverridesKeepExactDocumentOwnership()
	{
		using var document = Read(); using var foreign = Read(); var original = document.EvaluatePose();
		var updated = Override( original, new Dictionary<int, float[]> { [0] = [-2, 3], [1] = [-4, 5] } );
		document.ValidatePoseOwnership( updated );
		Assert.ThrowsException<ArgumentException>( () => foreign.ValidatePoseOwnership( updated ) );
		CollectionAssert.AreEqual( new[] { -2f, 3f }, updated.GetMorphWeights( 0 ) );
		CollectionAssert.AreEqual( new[] { -4f, 5f }, updated.GetMorphWeights( 1 ) );
	}

	[TestMethod]
	public void EmptyOverrideMapReusesItsImmutableSnapshot()
	{
		using var document = Read(); var original = document.EvaluatePose();
		Assert.AreSame( original, Override( original, new Dictionary<int, float[]>() ) );
	}

	[TestMethod]
	public void NodeWithoutTargetsAcceptsOnlyAnEmptyWeightArray()
	{
		using var document = Read(); var original = document.EvaluatePose();
		var updated = Override( original, new Dictionary<int, float[]> { [2] = [] } );
		Assert.AreEqual( 0, updated.GetMorphWeights( 2 ).Length );
		Assert.ThrowsException<GltfException>( () => Override( original, new Dictionary<int, float[]> { [2] = [1] } ) );
	}

	[TestMethod]
	[DataRow( -1 )]
	[DataRow( 3 )]
	[DataRow( int.MaxValue )]
	public void InvalidNodeIsRejectedWithoutChangingSourcePose( int node )
	{
		using var document = Read(); var original = document.EvaluatePose();
		Assert.ThrowsException<GltfException>( () => Override( original, new Dictionary<int, float[]> { [node] = [1, 2] } ) );
		CollectionAssert.AreEqual( new[] { .25f, .75f }, original.GetMorphWeights( 0 ) );
	}

	[TestMethod]
	[DataRow( 0 )]
	[DataRow( 1 )]
	[DataRow( 3 )]
	public void TargetCountMismatchIsNotPaddedOrTruncated( int count )
	{
		using var document = Read(); var original = document.EvaluatePose();
		Assert.ThrowsException<GltfException>( () => Override( original, new Dictionary<int, float[]> { [0] = new float[count] } ) );
		CollectionAssert.AreEqual( new[] { .25f, .75f }, original.GetMorphWeights( 0 ) );
	}

	[TestMethod]
	[DataRow( float.NaN )]
	[DataRow( float.PositiveInfinity )]
	[DataRow( float.NegativeInfinity )]
	public void NonfiniteWeightIsRejectedWithoutPublishingPartialOverrides( float invalid )
	{
		using var document = Read(); var original = document.EvaluatePose();
		Assert.ThrowsException<GltfException>( () => Override( original, new Dictionary<int, float[]> { [0] = [-2, 3], [1] = [4, invalid] } ) );
		CollectionAssert.AreEqual( new[] { .25f, .75f }, original.GetMorphWeights( 0 ) );
		CollectionAssert.AreEqual( new[] { .5f, .125f }, original.GetMorphWeights( 1 ) );
	}

	[TestMethod]
	public void NullOverrideMapIsAnExplicitArgumentError()
	{
		using var document = Read(); var original = document.EvaluatePose();
		Assert.ThrowsException<ArgumentNullException>( () => Override( original, null ) );
	}

	[TestMethod]
	public void NullNodeWeightArrayIsAnExplicitDataError()
	{
		using var document = Read(); var original = document.EvaluatePose();
		Assert.ThrowsException<GltfException>( () => Override( original, new Dictionary<int, float[]> { [0] = null } ) );
	}

	[TestMethod]
	public void InvalidLaterNodeDoesNotMutateEarlierNodeOrAnExistingDerivedPose()
	{
		using var document = Read(); var original = document.EvaluatePose();
		var previous = Override( original, new Dictionary<int, float[]> { [0] = [-2, 3] } );
		Assert.ThrowsException<GltfException>( () => Override( previous, new Dictionary<int, float[]> { [0] = [9, 8], [1] = [7] } ) );
		CollectionAssert.AreEqual( new[] { -2f, 3f }, previous.GetMorphWeights( 0 ) );
		CollectionAssert.AreEqual( new[] { .25f, .75f }, original.GetMorphWeights( 0 ) );
	}

	[TestMethod]
	public void DisposedOwnerKeepsReadablePoseSnapshotOverrideSemantics()
	{
		var document = Read(); var original = document.EvaluatePose(); var world = original.GetWorldTransform( 1 ); document.Dispose();
		Assert.ThrowsException<ObjectDisposedException>( () => document.EvaluatePose() );
		var updated = Override( original, new Dictionary<int, float[]> { [0] = [-2, 3] } );
		Assert.AreEqual( world, updated.GetWorldTransform( 1 ) ); document.ValidatePoseOwnership( updated );
		CollectionAssert.AreEqual( new[] { -2f, 3f }, updated.GetMorphWeights( 0 ) );
		CollectionAssert.AreEqual( new[] { .25f, .75f }, original.GetMorphWeights( 0 ) );
	}

	[TestMethod]
	[DataRow( -2f, 3f )]
	[DataRow( float.MinValue, float.MaxValue )]
	[DataRow( float.Epsilon, -float.Epsilon )]
	public void FiniteSignedUnboundedWeightsAreNotClampedOrNormalized( float first, float second )
	{
		using var document = Read();
		var updated = Override( document.EvaluatePose(), new Dictionary<int, float[]> { [0] = [first, second] } );
		CollectionAssert.AreEqual( new[] { first, second }, updated.GetMorphWeights( 0 ) );
	}

	[TestMethod]
	public void SignedZeroBitsSurviveSnapshotCopy()
	{
		using var document = Read(); var negativeZero = BitConverter.Int32BitsToSingle( unchecked((int)0x80000000) );
		var updated = Override( document.EvaluatePose(), new Dictionary<int, float[]> { [0] = [negativeZero, 0] } );
		var weights = updated.GetMorphWeights( 0 );
		Assert.AreEqual( unchecked((int)0x80000000), BitConverter.SingleToInt32Bits( weights[0] ) );
		Assert.AreEqual( 0, BitConverter.SingleToInt32Bits( weights[1] ) );
	}

	// Reflection intentionally lets this exact test source execute a real missing-
	// API red against the preserved pre-install reader, then exercise the actual
	// installed producer. It never compiles replacement producer implementation.
	private static GltfPose Override( GltfPose source, IReadOnlyDictionary<int, float[]> overrides )
	{
		var method = typeof( GltfPose ).GetMethod( "WithMorphWeights", BindingFlags.Instance | BindingFlags.Public,
			null, new[] { typeof( IReadOnlyDictionary<int, float[]> ) }, null );
		if ( method is null || method.ReturnType != typeof( GltfPose ) )
			throw new MissingMethodException( "The loaded GltfPose must expose public WithMorphWeights(IReadOnlyDictionary<int,float[]>) returning GltfPose." );
		try { return (GltfPose)method.Invoke( source, new object[] { overrides } ); }
		catch ( TargetInvocationException error ) when ( error.InnerException is not null )
		{
			ExceptionDispatchInfo.Capture( error.InnerException ).Throw(); throw;
		}
	}

	private static GltfDocument Read() => GltfReader.Read( Encoding.UTF8.GetBytes( """
		{"asset":{"version":"2.0"},"accessors":[{"componentType":5126,"count":1,"type":"VEC3","min":[0,0,0],"max":[0,0,0]}],
		"meshes":[{"weights":[0.25,0.75],"primitives":[{"mode":0,"attributes":{"POSITION":0},"targets":[{},{}]}]}],
		"nodes":[{"mesh":0,"translation":[1,2,3],"children":[1]},{"mesh":0,"weights":[0.5,0.125],"scale":[2,3,4]},{"translation":[4,5,6]}]}
		""" ) );
}
