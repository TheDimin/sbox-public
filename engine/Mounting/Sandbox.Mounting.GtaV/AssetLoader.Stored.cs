#if GTAV_BOREALIS_GEOMETRY
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using GtaV.ManagedReader;
using NativeEngine;
using Sandbox;
using NumericsMatrix = System.Numerics.Matrix4x4;

namespace GtaV;

internal sealed partial class AssetLoader
{
	const int PaintPrimary = 0x100;
	const int HasSpecular = 0x200;
	static readonly Matrix ModelUnits = Matrix.CreateScale( new Vector3( PacketReader.UnitsPerMetre ) );

	Queue<AssetLoader> ExternalTextures( DrawableAsset drawable, bool colorOnly )
	{
		var result = new Queue<AssetLoader>();
		var seen = new HashSet<AssetLoader>();
		var materials = new HashSet<DrawableAsset.Material>();
		foreach ( var section in drawable.Sections )
		{
			var material = section.Material;
			if ( !materials.Add( material ) ) continue;
			if ( material.Bucket != 2 || !IsBuildingOverlay( material.Name ) )
			{
				Queue( material.Diffuse );
				if ( !colorOnly ) Queue( material.Normal );
			}
			if ( !colorOnly ) Queue( material.Specular );
		}
		return result;

		void Queue( DrawableAsset.Binding binding )
		{
			if ( binding.Embedded.Length != 0 || FindTexture( binding.Name, drawable.TextureDictionaries ) is not { } texture ) return;
			if ( !texture.HasStreamingTexture && seen.Add( texture ) ) result.Enqueue( texture );
		}
	}

	Model BuildStoredModel( MountedDrawable data )
	{
		var builder = Model.Builder.WithName( Path );
		var drawable = data.Geometry;
		var materials = new Dictionary<MaterialKey, Material>();
		var bySource = new Dictionary<(DrawableAsset.Material, bool MissingColor), Material>();
		List<(Material Material, MaterialKey Key)> paintSlots = entry.Kind == Kind.VehicleBody ? new() : null;
		long retainedBytes = data.Metadata.BaseStream.Length;
		int previousLod = 0;
		bool drawableLods = entry.Kind is Kind.Model or Kind.DictionaryModel;
		Span<DrawableGeometry.Field> fields = stackalloc DrawableGeometry.Field[52];
		var rawFields = new RawVertexField[fields.Length];
		for ( int index = 0; index < drawable.Sections.Count; index++ )
		{
			var section = drawable.Sections[index];
			var source = section.Geometry;
			int count = source.ReadFields( fields );
			int position = -1;
			bool hasColor = false;
			for ( int i = 0; i < count; i++ )
			{
				var field = fields[i];
				if ( field.Slot == 0 && field.Format == 6 ) position = field.Offset;
				hasColor |= field.Slot == 24;
				rawFields[i] = new RawVertexField( field.Semantic, field.Index, field.Offset, (ColorFormat)field.Format );
			}

			var authored = section.Material;
			bool overlay = authored.Bucket == 2 && IsBuildingOverlay( authored.Name );
			var materialKey = (authored, MissingColor: overlay && !hasColor);
			if ( !bySource.TryGetValue( materialKey, out var material ) )
			{
				bool vehiclePaint = authored.VehiclePaint;
				bool hasSpecular = vehiclePaint || !string.IsNullOrEmpty( authored.Specular.Name );
				int flags = authored.Bucket | (authored.PrimaryPaint ? PaintPrimary : 0) | (hasSpecular ? HasSpecular : 0)
					| (vehiclePaint ? ModelRenderPacket.VehiclePaintFlag : 0)
					| (materialKey.MissingColor ? MissingDecalColor : 0);
				var key = new MaterialKey( flags, authored.Bucket == 2 ? authored.Name : 0, authored.Bucket == 2 ? authored.Preset : 0,
					authored.DecalMask, Resolve( authored.Diffuse ), Resolve( authored.Normal ), Resolve( authored.Specular ),
					authored.SpecularFalloff, authored.SpecularResponse );
				material = GetMaterial( key, overlay, materials, paintSlots );
				bySource.Add( materialKey, material );
			}

			int lod = drawableLods ? section.Lod : -1;
			if ( lod > 0 && lod != previousLod )
			{
				if ( float.IsFinite( section.Distance ) && section.Distance > 0 )
					builder.WithLodDistance( lod, section.Distance * PacketReader.UnitsPerMetre );
				previousLod = lod;
			}

			Matrix geometryToModel = section.Transform == 0 ? ModelUnits : (Matrix)drawable.TransformAt( section.Transform ) * ModelUnits;
			var vertices = source.Vertices;
			var indices = MemoryMarshal.Cast<byte, ushort>( source.Indices );
			var mesh = new Mesh( overlay ? $"GTA decal {index} | {OverlayName( authored.Name )} | {authored.Diffuse.Name}" : "mesh", material )
			{
				Bounds = ModelBounds( section.Min, section.Max, geometryToModel )
			};
			mesh.CreateRawVertexBuffer( source.Stride, source.VertexCount, vertices, rawFields.AsSpan( 0, count ), geometryToModel );
			mesh.CreateRawIndexBuffer( indices );
			if ( data.UploadSlices is { } slices ) mesh.StoredUpload = slices[index];
			if ( !overlay && lod <= 0 && position >= 0 ) builder.AddStoredTraceMesh( vertices, source.Stride, position, indices, geometryToModel );
			if ( lod < 0 ) builder.AddMesh( mesh );
			else builder.AddMesh( mesh, lod );
			retainedBytes += vertices.Length + (long)source.Indices.Length;
		}
		return FinishModel( builder, data.Metadata, paintSlots, retainedBytes );

		TextureBinding Resolve( DrawableAsset.Binding binding )
		{
			var embedded = binding.Embedded;
			if ( embedded.Length == 0 ) return new( FindTexture( binding.Name, drawable.TextureDictionaries )?.Path, null );
			var bytes = GC.AllocateUninitializedArray<byte>( embedded.Length );
			embedded.CopyTo( bytes );
			return new( null, bytes );
		}
	}

	static BBox ModelBounds( Vector3 min, Vector3 max, in Matrix geometryToModel )
	{
		var center = geometryToModel.Transform( (min + max) * 0.5f );
		var size = (max - min) * 0.5f;
		NumericsMatrix matrix = geometryToModel;
		var extents = new Vector3(
			MathF.Abs( matrix.M11 ) * size.x + MathF.Abs( matrix.M21 ) * size.y + MathF.Abs( matrix.M31 ) * size.z,
			MathF.Abs( matrix.M12 ) * size.x + MathF.Abs( matrix.M22 ) * size.y + MathF.Abs( matrix.M32 ) * size.z,
			MathF.Abs( matrix.M13 ) * size.x + MathF.Abs( matrix.M23 ) * size.y + MathF.Abs( matrix.M33 ) * size.z );
		return new BBox( center - extents, center + extents );
	}

	AssetLoader FindTexture( string name, string[] dictionaries )
	{
		if ( string.IsNullOrEmpty( name ) ) return null;
		foreach ( var dictionary in dictionaries )
			if ( Host.TextureAssets.TryGetValue( $"{dictionary}/{name}", out var texture ) )
				return texture;
		return null;
	}
}
#endif
