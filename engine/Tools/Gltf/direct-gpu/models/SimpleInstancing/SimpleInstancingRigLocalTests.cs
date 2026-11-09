using System;
using System.Collections.Generic;
using System.Numerics;
using System.Reflection;

namespace Sandbox.Gltf.DirectGpu.SimpleInstancingValidation;

using static SimpleInstancingReference;

/// <summary>12 CPU integration cases for the applied Rig instance-local palette/winding API, without GPU resources.</summary>
internal static class SimpleInstancingRigLocalTests
{
	public const int ExpectedCases = 12;
	public static SimpleInstancingCoreTests.Result[] RunAll( string checkoutRoot )
	{
		var reference = new SimpleInstancingReference( checkoutRoot ); var rows = new List<SimpleInstancingCoreTests.Result>();
		foreach ( var form in reference.Forms )
		{
			Run( "all125_Rig_palettes_include_instance_before_node_and_basis", () =>
			{
				using var d = form.Read(); var rig = new GltfGpuRig( d, 0, null, 0, GltfGpuPrimitivePlan.Create( d, 0, 0 ) ); var pose = d.EvaluatePose(); var method = EvaluateMethod(); var locals = d.ReadInstances( 0 );
				for ( var i = 0; i < InstanceCount; i++ )
				{
					var matrices = (GltfGpuMatrix[])method.Invoke( rig, new object[] { pose, (Matrix4x4?)locals[i] } ); Require( matrices.Length == 1, "Unskinned instance must use one full affine palette." );
					for ( var v = 0; v < VertexCount; v++ ) { var p = reference.Value( 1, v ); var actual = matrices[0].Position( new System.Numerics.Vector3( (float)p[0], (float)p[1], (float)p[2] ) ); var expected = reference.Engine( i, v, Matrix4x4.Identity ); Close( expected[0], actual.X, .0003, "Rig palette X" ); Close( expected[1], actual.Y, .0003, "Rig palette Y" ); Close( expected[2], actual.Z, .0003, "Rig palette Z" ); }
				}
			} );
			Run( "Rig_local_reflection_tiny_and_zero_winding", () =>
			{
				using var d = form.Read(); var rig = new GltfGpuRig( d, 0, null, 0, GltfGpuPrimitivePlan.Create( d, 0, 0 ) ); var pose = d.EvaluatePose(); var method = WindingMethod();
				foreach ( var (local, sign) in new[] { (Matrix4x4.Identity, 1), (Matrix4x4.CreateScale( -1, 2, 3 ), -1), (Matrix4x4.CreateScale( -1e-30f, 2e-30f, 3e-30f ), -1), (Matrix4x4.CreateScale( 1e-30f, 2e-20f, 3e-10f ), 1), (Matrix4x4.CreateScale( 0, 2, 3 ), 0), (Matrix4x4.CreateScale( 0, 0, 0 ), 0) } )
					foreach ( var (placement, placementSign) in new[] { (AffinePlacement, 1), (MirroredPlacement, -1) } )
						Require( (bool)method.Invoke( rig, new object[] { pose, placement, (Matrix4x4?)local } ) == (sign != 0 && sign != placementSign), "Instance determinant sign lost or reflection cancellation failed." );
			} );
			Run( "Rig_rejects_projective_and_nonfinite_local_before_submission", () =>
			{
				using var d = form.Read(); var rig = new GltfGpuRig( d, 0, null, 0, GltfGpuPrimitivePlan.Create( d, 0, 0 ) ); var pose = d.EvaluatePose(); var method = EvaluateMethod();
				// Obtain/check the API outside expected rejection: absence cannot
				// masquerade as a valid negative-control failure.
				var projective = Matrix4x4.Identity; projective.M14 = .5f; var invalid = Matrix4x4.Identity; invalid.M11 = float.NaN;
				foreach ( var local in new[] { projective, invalid } )
				{
					try { method.Invoke( rig, new object[] { pose, (Matrix4x4?)local } ); }
					catch ( TargetInvocationException e ) when ( e.InnerException is GltfException ge ) { Require( ge.Message.Contains( "affine", StringComparison.OrdinalIgnoreCase ), "Useful affine validation reason required." ); continue; }
					throw new InvalidOperationException( "Invalid instance-local transform was accepted." );
				}
			} );
			void Run( string name, Action body ) { try { body(); rows.Add( new( name, form.Name, true, null ) ); } catch ( Exception error ) { rows.Add( new( name, form.Name, false, error.ToString() ) ); } }
		}
		Require( rows.Count == ExpectedCases, "Rig-local inventory changed." ); return rows.ToArray();
	}
	private static MethodInfo EvaluateMethod() { var m = typeof( GltfGpuRig ).GetMethod( "Evaluate", BindingFlags.Instance | BindingFlags.NonPublic, null, new[] { typeof( GltfPose ), typeof( Matrix4x4? ) }, null ); Require( m is not null, "Pending instance-local Rig.Evaluate pose/matrix API." ); return m; }
	private static MethodInfo WindingMethod() { var m = typeof( GltfGpuRig ).GetMethod( "ReverseWinding", BindingFlags.Instance | BindingFlags.NonPublic, null, new[] { typeof( GltfPose ), typeof( Matrix4x4 ), typeof( Matrix4x4? ) }, null ); Require( m is not null, "Pending instance-local winding API." ); return m; }
}
