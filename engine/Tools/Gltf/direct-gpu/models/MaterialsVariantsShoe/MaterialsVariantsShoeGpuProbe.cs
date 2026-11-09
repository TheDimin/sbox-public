using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

namespace Sandbox.Gltf.DirectGpu.MaterialsVariantsShoeValidation;

using NMatrix = System.Numerics.Matrix4x4;
using static MaterialsVariantsShoeReference;

/// <summary>Coordinator-owned real-frame driver. Does not launch an editor or compile shaders.</summary>
public static class MaterialsVariantsShoeGpuProbe
{
	public static MaterialsVariantsShoeCoreTests.Result[] RunContract()
	{
		var rows = new List<MaterialsVariantsShoeCoreTests.Result>();
		Run( "per_instance_SetVariant_nullable_index_API", () => Require( Setter()?.ReturnType == typeof( void ), "Pending: public void GltfGpuInstance.SetVariant(int?) is not implemented." ) );
		Run( "per_instance_ActiveVariant_readonly_API", () => Require( ReadonlyProperty( Property() ), "Pending: public readonly GltfGpuInstance.ActiveVariant of type int? is not implemented." ) );
		return rows.ToArray();
		void Run( string name, Action body ) { try { body(); rows.Add( new( name, "shared backend reflection contract", true, null ) ); } catch ( Exception error ) { rows.Add( new( name, "shared backend reflection contract", false, error.ToString() ) ); } }
	}

	public static async Task<IReadOnlyList<object>> Run( string checkoutRoot, string formName, SceneWorld world, Material computeMaterial,
		Func<Task> waitForActualFrames, Func<int?, Material> materialResolver = null )
	{
		var setter = Setter(); var property = Property();
		Require( setter?.ReturnType == typeof( void ) && ReadonlyProperty( property ), "Variant runtime API is pending; do not label default-material rendering as variant acceptance." );
		var inputs = new Inputs( checkoutRoot ); var form = inputs.Forms.Single( x => x.Name == formName );
		using var document = form.Read(); using var asset = GltfGpuAsset.Create( document, computeMaterial, materialResolver );
		using var a = asset.CreateInstance( world ); using var b = asset.CreateInstance( world );
		b.Placement = NMatrix.CreateTranslation( 3, -5, 7 );
		await waitForActualFrames(); RequireSubmitted( a ); RequireSubmitted( b );
		Require( Active( a ) is null && Active( b ) is null, "New instances must start with no active variant." );
		var stableA = a.Readback().Single().Bytes.ToArray(); var stableB = b.Readback().Single().Bytes.ToArray();
		var rows = new List<object>();
		foreach ( var state in new[] { (A: (int?)0, B: (int?)2), (A: (int?)1, B: (int?)2), (A: (int?)2, B: (int?)0), (A: (int?)null, B: (int?)1) } )
		{
			Set( a, state.A ); Set( b, state.B ); var drawA = a.DrawCalls; var drawB = b.DrawCalls;
			await waitForActualFrames(); RequireSubmitted( a ); RequireSubmitted( b );
			Require( a.DrawCalls > drawA && b.DrawCalls > drawB && Active( a ) == state.A && Active( b ) == state.B, "Variant instance state did not survive a real draw." );
			var snapshotA = a.Readback().Single(); var snapshotB = b.Readback().Single();
			Require( snapshotA.Bytes.SequenceEqual( stableA ) && snapshotB.Bytes.SequenceEqual( stableB ), "Material variant changed shared geometry or another instance's output." );
			rows.Add( new { form = formName, activeA = Active( a ), activeB = Active( b ), expectedMaterialA = state.A ?? 0, expectedMaterialB = state.B ?? 0,
				geometryA = MaterialsVariantsShoeGpuChecks.AssertSnapshot( snapshotA, form.Oracle ), geometryB = MaterialsVariantsShoeGpuChecks.AssertSnapshot( snapshotB, form.Oracle, 3, -5, 7 ),
				materialScope = materialResolver is null ? "source stock materials requested" : "explicit caller material resolver; receipt must identify diagnostics",
				materialImageAcceptance = "pending matched independent image/sampling evidence; ActiveVariant alone is not actual material binding proof" } );
		}
		b.Dispose(); Set( a, 1 ); await waitForActualFrames(); RequireSubmitted( a ); Require( Active( a ) == 1, "Disposing instance B invalidated surviving A state." );
		return rows.AsReadOnly();
		void Set( GltfGpuInstance instance, int? variant )
		{
			try { setter.Invoke( instance, new object[] { variant } ); }
			catch ( TargetInvocationException error ) { throw new InvalidOperationException( "Variant selection failed.", error.InnerException ?? error ); }
		}
		int? Active( GltfGpuInstance instance ) => (int?)property.GetValue( instance );
	}
	private static MethodInfo Setter() => typeof( GltfGpuInstance ).GetMethod( "SetVariant", BindingFlags.Public | BindingFlags.Instance, null, new[] { typeof( int? ) }, null );
	private static PropertyInfo Property()
	{
		var property = typeof( GltfGpuInstance ).GetProperty( "ActiveVariant", BindingFlags.Public | BindingFlags.Instance );
		return property?.PropertyType == typeof( int? ) ? property : null;
	}
	private static bool ReadonlyProperty( PropertyInfo property ) => property?.GetMethod is { IsPublic: true } && property.SetMethod is not { IsPublic: true };
	private static void RequireSubmitted( GltfGpuInstance instance ) => Require( instance.DrawCalls > 0 && instance.SubmittedRevision == instance.PoseRevision && instance.CallbackErrors.Length == 0, "No completed current GPU draw: " + string.Join( "\n", instance.CallbackErrors ) );
}
