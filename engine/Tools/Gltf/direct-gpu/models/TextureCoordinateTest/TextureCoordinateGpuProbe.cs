using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Sandbox.Gltf.DirectGpu.TextureCoordinateTestValidation;

using NMatrix = System.Numerics.Matrix4x4;

/// <summary>Coordinator-only actual GPU orchestration; source materials are retained.</summary>
public static class TextureCoordinateGpuProbe
{
	public sealed record Row( string Form, string Placement, TextureCoordinateGpuChecks.Measurement Gpu, int DrawCalls, long PoseRevision, long SubmittedRevision );
	public static async Task<Row[]> MeasureAll( string checkoutRoot, SceneWorld world, Material borrowedComputeMaterial, Func<Task> awaitCompletedNativeFrame )
	{
		ArgumentNullException.ThrowIfNull( awaitCompletedNativeFrame ); var rows = new List<Row>();
		foreach ( var form in TextureCoordinateReference.Open( checkoutRoot ).Forms() )
		{
			using var document = form.Read(); using var asset = GltfGpuAsset.Create( document, borrowedComputeMaterial );
			using var instance = asset.CreateInstance( world );
			foreach ( var placement in new[]
			{
				(Name: "identity", Matrix: NMatrix.Identity),
				(Name: "full_affine_shear", Matrix: new NMatrix( 1, .15f, 0, 0, 0, 1.25f, .1f, 0, 0, 0, .75f, 0, 5, -3, 2, 1 )),
				(Name: "reflected_y", Matrix: NMatrix.CreateScale( 1, -1, 1 ))
			} )
			{
				instance.Placement = placement.Matrix; instance.SetPose( document.EvaluatePose() ); await awaitCompletedNativeFrame();
				rows.Add( new( form.Name, placement.Name, TextureCoordinateGpuChecks.MeasureCurrent( form, instance, placement.Matrix ), instance.DrawCalls, instance.PoseRevision, instance.SubmittedRevision ) );
			}
		}
		return rows.ToArray();
	}
}
