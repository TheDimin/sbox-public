using System;
using System.Buffers.Binary;
using System.IO;
using NumericsMatrix = System.Numerics.Matrix4x4;
using NumericsVector = System.Numerics.Vector4;

namespace GtaV.ManagedReader;

/// <summary>Converts stored geometry for renderers that require the engine vertex format.</summary>
internal static class ModelRenderPacket
{
	internal const int VehiclePaintFlag = 0x800;

	internal static BinaryReader Read( MountedDrawable drawable )
	{
		var model = drawable.Geometry;
		using var packet = new MountPacket();
		var writer = packet.Writer;
		writer.Write( model.TextureDictionaries.Length );
		foreach ( var dictionary in model.TextureDictionaries ) MountPacket.WriteText( writer, dictionary );
		Span<DrawableGeometry.Field> fields = stackalloc DrawableGeometry.Field[52];
		foreach ( var section in model.Sections )
		{
			var geometry = section.Geometry;
			var material = section.Material;
			bool decal = material.Bucket == 2;
			bool vehiclePaint = material.VehiclePaint;
			bool specular = vehiclePaint || !string.IsNullOrEmpty( material.Specular.Name );
			writer.Write( geometry.VertexCount );
			writer.Write( material.Bucket | (material.PrimaryPaint ? 0x100 : 0) | (specular ? 0x200 : 0)
				| (vehiclePaint ? VehiclePaintFlag : 0) | section.Lod << 12 );
			if ( section.Lod > 0 ) writer.Write( section.Distance );
			if ( decal )
			{
				writer.Write( material.Name );
				writer.Write( material.Preset );
				WriteVector( writer, material.DecalMask );
			}
			WriteTexture( writer, material.Diffuse );
			WriteTexture( writer, material.Normal );
			if ( specular )
			{
				writer.Write( material.SpecularFalloff );
				WriteVector( writer, material.SpecularResponse );
				WriteTexture( writer, material.Specular );
			}
			var layout = fields[..geometry.ReadFields( fields )];
			var position = Find( layout, 0 );
			var normal = Find( layout, 4 );
			var tangent = Find( layout, 8 );
			var color = Find( layout, 24 );
			var uv = Find( layout, 28 );
			var transform = model.TransformAt( section.Transform );
			var vertices = geometry.Vertices;
			for ( int i = 0; i < geometry.VertexCount; i++ )
			{
				var vertex = vertices.Slice( i * geometry.Stride, geometry.Stride );
				WriteTransformed( writer, Decode( vertex, position ), transform, 1 );
				WriteTransformed( writer, Decode( vertex, normal ), transform, 0 );
				var coordinates = Decode( vertex, uv );
				writer.Write( coordinates.X );
				writer.Write( coordinates.Y );
				var direction = Decode( vertex, tangent );
				WriteTransformed( writer, direction, transform, 0 );
				writer.Write( direction.W );
				if ( decal )
				{
					var tint = color.Format == 0 ? NumericsVector.One : Decode( vertex, color );
					writer.Write( ColorByte( tint.X ) );
					writer.Write( ColorByte( tint.Y ) );
					writer.Write( ColorByte( tint.Z ) );
					writer.Write( ColorByte( tint.W ) );
				}
			}
			writer.Write( geometry.IndexCount );
			writer.Write( geometry.Indices );
		}
		writer.Write( 0 );
		drawable.Metadata.BaseStream.CopyTo( writer.BaseStream );
		return packet.Finish();
	}

	static void WriteTexture( BinaryWriter writer, DrawableAsset.Binding binding )
	{
		MountPacket.WriteText( writer, binding.Name ?? string.Empty );
		binding.Embedded.Write( writer );
	}

	static DrawableGeometry.Field Find( ReadOnlySpan<DrawableGeometry.Field> fields, byte slot )
	{
		foreach ( var field in fields )
			if ( field.Slot == slot ) return field;
		return default;
	}

	static NumericsVector Decode( ReadOnlySpan<byte> vertex, DrawableGeometry.Field field )
	{
		var data = vertex[field.Offset..];
		return field.Format switch
		{
			0 => default,
			2 => new( Single( data, 0 ), Single( data, 4 ), Single( data, 8 ), Single( data, 12 ) ),
			6 => new( Single( data, 0 ), Single( data, 4 ), Single( data, 8 ), 0 ),
			10 => new( Half( data, 0 ), Half( data, 2 ), Half( data, 4 ), Half( data, 6 ) ),
			16 => new( Single( data, 0 ), Single( data, 4 ), 0, 0 ),
			28 or 30 => new( data[0] / 255f, data[1] / 255f, data[2] / 255f, data[3] / 255f ),
			31 => new( (sbyte)data[0] / 127f, (sbyte)data[1] / 127f, (sbyte)data[2] / 127f, (sbyte)data[3] / 127f ),
			34 => new( Half( data, 0 ), Half( data, 2 ), 0, 0 ),
			41 => new( Single( data, 0 ), 0, 0, 0 ),
			_ => throw new InvalidDataException( $"Unsupported GTA vertex format {field.Format}." )
		};

		static float Single( ReadOnlySpan<byte> data, int offset ) => BinaryPrimitives.ReadSingleLittleEndian( data[offset..] );
		static float Half( ReadOnlySpan<byte> data, int offset ) => (float)BinaryPrimitives.ReadHalfLittleEndian( data[offset..] );
	}

	static void WriteVector( BinaryWriter writer, NumericsVector value )
	{
		writer.Write( value.X );
		writer.Write( value.Y );
		writer.Write( value.Z );
		writer.Write( value.W );
	}

	static void WriteTransformed( BinaryWriter writer, NumericsVector value, in NumericsMatrix transform, float w )
	{
		writer.Write( value.X * transform.M11 + value.Y * transform.M21 + value.Z * transform.M31 + w * transform.M41 );
		writer.Write( value.X * transform.M12 + value.Y * transform.M22 + value.Z * transform.M32 + w * transform.M42 );
		writer.Write( value.X * transform.M13 + value.Y * transform.M23 + value.Z * transform.M33 + w * transform.M43 );
	}

	static byte ColorByte( float value ) => (byte)(Math.Clamp( value, 0f, 1f ) * 255f + 0.5f);
}
