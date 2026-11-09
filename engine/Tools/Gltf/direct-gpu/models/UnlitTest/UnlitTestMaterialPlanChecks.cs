using System;
using System.Collections.Generic;

namespace Sandbox.Gltf.DirectGpu.UnlitTestValidation;

/// <summary>
/// Actual pinned-model coverage for the new installed CPU plan. These require a
/// real Engine producer containing the plan; never compile producer substitutes
/// into the helper. Successful planning is separate from default material draws.
/// </summary>
public static class UnlitTestMaterialPlanChecks
{
	public sealed record Result( string Name, string Form, bool Passed, string Error );
	public const int ExpectedCases = 4;
	public static Result[] RunAll( string checkoutRoot )
	{
		var rows = new List<Result>();
		foreach ( var form in UnlitTestReference.OpenForms( checkoutRoot ) )
		{
			try
			{
				using var document = form.Read();
				for ( var material = 0; material < 2; material++ )
				{
					var plan = global::Sandbox.GltfUnlitMaterialPlan.Create( document.ReadMaterial( material ), material );
					var binding = plan.Bind( document, document.MaterializePrimitiveUpload( material, 0 ) );
					UnlitTestCoreTests.Require( plan.LinearBaseColorFactor == UnlitTestReference.BaseColors[material]
						&& plan.LinearFactorForShader == UnlitTestReference.BaseColors[material], "New unlit plan changed the independently pinned linear factor." );
					UnlitTestCoreTests.Require( plan.CullBackFaces && plan.AlphaMode == global::Sandbox.GltfUnlitMaterialPlan.CoverageMode.Opaque
						&& plan.EffectiveAlphaCutoff is null && plan.EvaluateCoverage( 0 ).OutputAlpha == 1, "New unlit plan changed core opaque coverage/sidedness." );
					UnlitTestCoreTests.Require( binding.Source == plan && binding.SourceMode == 4 && binding.HasAuthoredNormal && binding.ColorSource is null
						&& binding.Uv.Bindings.Count == 0 && binding.Textures.Bindings.Count == 0 && binding.HasResolvedBaseColorImage,
						"New unlit primitive binding invented texture/vertex-color inputs or lost source context." );
					UnlitTestCoreTests.Require( binding.RequiredFeatures == global::Sandbox.GltfUnlitMaterialPlan.Features.LinearFactor,
						"Pinned untextured opaque model acquired an unsupported-feature approximation." );
				}
				rows.Add( new( "installed_unlit_plan_preserves_both_authored_materials", form.Name, true, null ) );
			}
			catch ( Exception error ) { rows.Add( new( "installed_unlit_plan_preserves_both_authored_materials", form.Name, false, error.ToString() ) ); }
		}
		UnlitTestCoreTests.Require( rows.Count == ExpectedCases, "New unlit plan case inventory changed." ); return rows.ToArray();
	}
}
