using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Sandbox.Gltf.DirectGpu;

using Assert = Microsoft.VisualStudio.TestTools.UnitTesting.Assert;

[TestClass]
public sealed class GltfGpuPublicTexturedCaptureTests
{
	private static string checkout;
	private static bool controlsPassed;
	public static List<object> Measurements { get; } = new();
	public static void Configure( string root ) { checkout = root; Measurements.Clear(); }

	public static BoxTexturedValidation.BoxTexturedCoreTests.Result[] RunControls()
	{
		var rows = BoxTexturedValidation.BoxTexturedImageControlTests.RunAll();
		controlsPassed = rows.Length == 8 && rows.All( row => row.Passed );
		return rows;
	}

	[TestMethod]
	public Task PublicBoxTexturedSource() => Capture( "glTF/source" );

	[TestMethod]
	public Task PublicBoxTexturedCompiled() => Capture( "glTF/compiled_glb" );

	internal static Task CaptureVerifiedForm( string form ) => Capture( form );

	private static async Task Capture( string form )
	{
		Assert.IsTrue( controlsPassed, "All eight independent image-checker controls must pass before capture." );
		await MainThread.Wait();
		var output = Path.Combine( checkout, "Temp/gltf-validation/direct-gpu/public-model-smokes/BoxTextured", Guid.NewGuid().ToString( "N" ) );
		Directory.CreateDirectory( output );
		GltfGpuTriangleGateTests.Measurements.Clear();
		using var capture = new GltfGpuTriangleGateTests.NativeCapture( output );
		try
		{
			var result = await BoxTexturedValidation.BoxTexturedPublicCapture.Run( checkout, capture.Scene, capture.Camera,
				capture.WaitFrames, capture.Take, 1e-4f, 1e-5f, 1, 0, 0, form );
			Measurements.Add( new { model = "BoxTextured", form, output, result,
				limits = new { position_inches = 1e-4, normal = 1e-5, uv = 0, minimum_iou = 1, mean_rgb = 0, maximum_rgb = 0 },
				scope = "Public Model.Load and ordinary ModelRenderer, default source materials, actual GPU/readback and same-material independent literal geometry with V-flip negative. No independent sampler/sRGB/PBR fidelity claim." } );
			Assert.IsTrue( result.Passed, result.Error );
		}
		finally
		{
			Measurements.Add( new { model = "BoxTextured", form,
				retained_native_captures = GltfGpuTriangleGateTests.Measurements.ToArray() } );
		}
	}
}

/// <summary>The four additional actual upstream and derived forms, after the first two passed.</summary>
[TestClass]
public sealed class GltfGpuPublicAdditionalTexturedCaptureTests
{
	public static List<object> Measurements => GltfGpuPublicTexturedCaptureTests.Measurements;
	public static void Configure( string root ) => GltfGpuPublicTexturedCaptureTests.Configure( root );
	public static BoxTexturedValidation.BoxTexturedCoreTests.Result[] RunControls() => GltfGpuPublicTexturedCaptureTests.RunControls();

	[TestMethod]
	[DataRow( "glTF-Embedded/source" )]
	[DataRow( "glTF-Embedded/compiled_glb" )]
	[DataRow( "glTF-Binary/source" )]
	[DataRow( "glTF-Binary/compiled_glb" )]
	public Task RemainingActualBoxForms( string form ) => GltfGpuPublicTexturedCaptureTests.CaptureVerifiedForm( form );
}
