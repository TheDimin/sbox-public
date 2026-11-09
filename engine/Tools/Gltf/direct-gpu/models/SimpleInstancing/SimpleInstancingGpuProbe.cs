using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Threading.Tasks;

namespace Sandbox.Gltf.DirectGpu.SimpleInstancingValidation;

using static SimpleInstancingReference;

/// <summary>Coordinator supplies actual frames/compute material. No editor launch, shader compilation or native Model/Mesh.</summary>
internal static class SimpleInstancingGpuProbe
{
	public const int ExpectedMeasurements = 20;
	public sealed record Result( string Name, string Form, SimpleInstancingGpuChecks.Measurement Readback, string MaterialScope, string SharingScope );
	public static async Task<IReadOnlyList<Result>> Run( string checkoutRoot, SceneWorld world, Material computeMaterial, Func<Task> waitForActualFrames, Func<int?, Material> materialResolver = null )
	{
		var reference = new SimpleInstancingReference( checkoutRoot ); var rows = new List<Result>();
		foreach ( var form in reference.Forms )
		{
			using var document = form.Read(); using var asset = GltfGpuAsset.Create( document, computeMaterial, materialResolver ); using var a = asset.CreateInstance( world ); using var b = asset.CreateInstance( world );
			Sharing( asset, a, b ); b.Placement = AffinePlacement;
			await waitForActualFrames(); Record( "rest_A_all125", a ); Record( "affine_B_all125", b );
			var stableB = b.Readback().Select( x => x.Bytes.ToArray() ).ToArray(); var beforeA = a.DrawCalls; var beforeB = b.DrawCalls;
			a.Placement = MirroredPlacement; await waitForActualFrames(); Require( a.DrawCalls > beforeA && b.DrawCalls > beforeB, "Instances did not draw new real frames." );
			Record( "mirrored_A_all125", a ); Record( "affine_B_unchanged_after_A_update", b );
			Require( b.Readback().Select( x => x.Bytes ).Zip( stableB, (x, y) => x.SequenceEqual( y ) ).All( same => same ), "A placement update corrupted independently owned B output." );
			var disposedBCalls = b.DrawCalls; b.Dispose(); a.Placement = Matrix4x4.Identity; await waitForActualFrames(); Require( b.DrawCalls == disposedBCalls, "Disposed instance B continued drawing." ); Record( "A_restored_after_B_dispose", a );
			void Record( string name, GltfGpuInstance instance )
			{
				Require( instance.DrawCalls > 0 && instance.SubmittedRevision == instance.PoseRevision && instance.CallbackErrors.Length == 0, "Current instance revision has not completed GPU submission: " + string.Join( "\n", instance.CallbackErrors ) );
				rows.Add( new( name, form.Name, SimpleInstancingGpuChecks.AssertSnapshots( reference, instance.Readback(), instance.Placement ), materialResolver is null ? "default stock glTF material requested; pixel fidelity pending" : "explicit caller resolver; diagnostics must be identified in receipt", "one shared immutable primitive, separate mutable instance output/placement; draw-call batching efficiency pending" ) );
			}
		}
		Require( rows.Count == ExpectedMeasurements, "Frozen GPU measurement inventory changed." ); return rows.AsReadOnly();
	}
	private static void Sharing( GltfGpuAsset asset, GltfGpuInstance a, GltfGpuInstance b )
	{
		// This checks the agreed backend's existing ownership shape, without
		// requiring one SceneObject or one output buffer per authored EXT instance.
		var primitives = ((IEnumerable)Field( asset, "Primitives" )).Cast<object>().ToArray(); Require( primitives.Length == 1, "125 instances must share the one immutable mesh/primitive, not duplicate input GPU resources." );
		foreach ( var name in new[] { "Input", "InputDescriptors", "OutputDescriptors", "SourceVertices", "Material" } ) Require( Field( primitives[0], name ) is not null, "Shared primitive resource missing: " + name );
		Require( ReferenceEquals( Field( a, "asset" ), asset ) && ReferenceEquals( Field( b, "asset" ), asset ), "Two runtime instances must borrow the same immutable asset." );
		Require( !ReferenceEquals( Field( a, "placementBuffer" ), Field( b, "placementBuffer" ) ), "Placement buffer must be instance owned." );
		foreach ( var instance in new[] { a, b } ) foreach ( var draw in ((IEnumerable)Field( instance, "draws" )).Cast<object>() )
		{
			var node = Field( draw, "Node" ); var property = node.GetType().GetProperty( "Primitive", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic );
			Require( property is not null && ReferenceEquals( property.GetValue( node ), primitives[0] ), "Draw must borrow the sole canonical immutable primitive rather than a duplicated resource set." );
		}
		var outputsA = Outputs( a ); var outputsB = Outputs( b ); Require( outputsA.Length > 0 && outputsB.Length > 0 && !outputsA.Any( x => outputsB.Any( y => ReferenceEquals( x, y ) ) ), "Output buffers must not alias across independently placed runtime instances." );
		object[] Outputs( GltfGpuInstance instance ) => ((IEnumerable)Field( instance, "draws" )).Cast<object>().Select( draw => Field( draw, "Output" ) ).Where( x => x is not null ).ToArray();
	}
	private static object Field( object value, string name )
	{
		var field = value.GetType().GetField( name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic ); Require( field is not null, "Pending ownership instrumentation: backend field " + name + " changed; review probe rather than assuming sharing." ); return field.GetValue( value );
	}
}
