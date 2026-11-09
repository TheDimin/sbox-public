using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;

namespace Sandbox.Gltf.DirectGpu.MeshPrimitiveModesValidation;

using NMatrix = global::System.Numerics.Matrix4x4;
using NVector3 = global::System.Numerics.Vector3;
using static MeshPrimitiveModesReference;

/// <summary>84 independent CPU/data cases: 7 modes × (4 upstream/packaged forms + 8 supplemental layout forms).</summary>
public static class MeshPrimitiveModesCoreTests
{
	public sealed record Result( string Name, string Form, bool Passed, string Error );
	public static Result[] RunAll( string checkoutRoot )
	{
		var inputs = new Inputs( checkoutRoot ); var rows = new List<Result>();
		foreach ( var form in inputs.Forms ) for ( var mode = 0; mode < 7; mode++ )
		{
			try { Check( form, mode ); rows.Add( new( ModeNames[mode], form.Name, true, null ) ); }
			catch ( Exception error ) { rows.Add( new( ModeNames[mode], form.Name, false, error.ToString() ) ); }
		}
		return rows.ToArray();
	}
	private static void Check( Form form, int mode )
	{
		using var document = form.Read(); var root = document.Root;
		Require( document.GetDefaultScene() == 0 && root.GetProperty( "scenes" )[0].GetProperty( "nodes" ).GetArrayLength() == 7 &&
			root.GetProperty( "nodes" ).GetArrayLength() == 7 && root.GetProperty( "meshes" ).GetArrayLength() == 7, "Pinned seven-mode scene changed." );
		foreach ( var absent in new[] { "materials", "animations", "skins", "extensionsUsed", "extensionsRequired" } )
			Require( !root.TryGetProperty( absent, out _ ), "Unexpected source feature: " + absent );
		var primitive = document.ReadPrimitive( mode, 0 );
		Require( primitive.Mode == mode && primitive.Material is null && primitive.VertexCount == form.LocalVertexCount( mode ) &&
			primitive.Attributes.Count == 1 && primitive.Attributes.ContainsKey( "POSITION" ), "Primitive mode/count/material/attributes changed." );
		Require( primitive.Indices.SequenceEqual( form.LocalIndices( mode ) ), "Authored or generated index sequence changed." );
		var upload = document.MaterializePrimitiveUpload( mode, 0 );
		Require( upload.Layout.SourceMode == mode && upload.Layout.DecodedMode == mode && !upload.Layout.RequiresDracoIndices &&
			upload.Layout.RequiresGeneratedIndices == form.Nonindexed && upload.Layout.RequiresTopologyExpansion == (mode is 2 or 3 or 5 or 6), "Upload topology provenance changed." );
		var p = upload.Attributes["POSITION"];
		Require( p.ComponentType == 5126 && p.Components == 3 && p.Count == form.LocalVertexCount( mode ) && !p.Normalized && !p.IsMaterialized,
			"POSITION must retain actual FLOAT VEC3 format." );
		Require( p.ByteOffset == (form.Repacked ? 8 : 0) && p.ByteStride == (form.Repacked ? 28 : 12) &&
			p.Binding.AccessorByteOffset == p.ByteOffset && p.Binding.ByteStride == p.ByteStride,
			"Accessor-relative offset or stride was replaced by a fixed vertex layout." );
		Require( p.Binding.ViewByteOffset == form.ViewOrigins( mode ).Position,
			"Source view origin was lost or conflated with owned-view offset." );
		if ( !form.Repacked ) Require( p.Storage.Bytes.Length == 84, "Source position view must retain all 84 bytes." );
		else Require( p.Storage.Bytes.Span[0] == 0xcd && p.Storage.Bytes.Span[7] == 0xcd && p.Storage.Bytes.Span[20] == 0xcd,
			"Poisoned padding disappeared; bad-offset sensitivity was lost." );
		if ( form.Nonindexed ) Require( upload.Indices is null, "Nonindexed fixture acquired an authored index stream." );
		else
		{
			var indices = upload.Indices; var width = form.IndexType == 5121 ? 1 : form.IndexType == 5123 ? 2 : 4;
			Require( indices is not null && indices.ComponentType == form.IndexType && indices.Components == 1 && !indices.Normalized && !indices.IsMaterialized &&
				indices.ByteStride == width && indices.Count == AuthoredIndices[mode].Length && indices.ByteOffset == (form.Repacked ? 4 : SourceIndexOffsets[mode]),
				"Actual index width/count/stride/shared-view accessor offset changed." );
			Require( indices.Binding.ViewByteOffset == form.ViewOrigins( mode ).Index &&
				indices.Binding.AccessorByteOffset == indices.ByteOffset && !ReferenceEquals( p.Storage, indices.Storage ), "Index/view provenance changed." );
			if ( !form.Repacked ) Require( indices.Storage.Bytes.Length == 130, "Shared index view must retain all 130 bytes." );
			for ( var index = 0; index < indices.Count; index++ )
			{
				var bytes = indices.Storage.Bytes.Span[(indices.ByteOffset + index * indices.ByteStride)..];
				var value = width == 1 ? bytes[0] : width == 2 ? BinaryPrimitives.ReadUInt16LittleEndian( bytes ) : BinaryPrimitives.ReadUInt32LittleEndian( bytes );
				Require( value == AuthoredIndices[mode][index], "Independent raw index decode disagrees." );
			}
		}
		var ownedBytes = p.Storage.Bytes.Length + (upload.Indices?.Storage.Bytes.Length ?? 0);
		Require( upload.OwnedBytes == ownedBytes, "Distinct owned view budget changed." );
		try { document.MaterializePrimitiveUpload( mode, 0, ownedBytes - 1 ); throw new InvalidOperationException( "Under-budget upload was accepted." ); }
		catch ( GltfException ) { }
		Require( document.MaterializePrimitiveUpload( mode, 0, ownedBytes ).OwnedBytes == ownedBytes, "Exact owned budget was rejected." );
		var pose = document.EvaluatePose(); var draw = document.CreateDrawData( mode, 0 );
		Require( draw.Mode == DrawMode( mode ) && draw.Normals.IsEmpty && draw.Tangents.IsEmpty && !draw.HasSkinning && !draw.HasMorphTargets && draw.Material is null,
			"Reader must preserve absence of authored normals/tangents/material rather than inventing source attributes." );
		var expandedHexagon = draw.Indices.ToArray().Select( index => form.HexagonVertex( mode, index ) ).ToArray();
		Require( expandedHexagon.SequenceEqual( DrawHexagonIndices[mode] ), "Expanded topology loses loop closure, strip winding or fan topology." );
		for ( var vertex = 0; vertex < p.Count; vertex++ )
		{
			var hexagon = form.HexagonVertex( mode, (uint)vertex ); var at = p.ByteOffset + vertex * p.ByteStride;
			var raw = new NVector3( BinaryPrimitives.ReadSingleLittleEndian( p.Storage.Bytes.Span[at..] ),
				BinaryPrimitives.ReadSingleLittleEndian( p.Storage.Bytes.Span[(at + 4)..] ), BinaryPrimitives.ReadSingleLittleEndian( p.Storage.Bytes.Span[(at + 8)..] ) );
			Near( Hexagon[hexagon], raw, 0, "Every literal source position component" );
			Near( Hexagon[hexagon] + Translations[mode], document.EvaluateVertexPosition( mode, 0, vertex, pose ), 0, "Every authored world vertex" );
			Near( ToEngine( Hexagon[hexagon], NMatrix.Identity ), draw.Positions.Span[vertex], 0, "Exactly one local basis/unit conversion" );
		}
		var frozen = p.Storage.Bytes.ToArray(); document.Dispose();
		Require( frozen.SequenceEqual( p.Storage.Bytes.ToArray() ), "Owned upload bytes changed when document was disposed." );
	}
}
