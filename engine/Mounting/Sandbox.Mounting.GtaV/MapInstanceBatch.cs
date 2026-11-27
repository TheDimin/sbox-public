using System;
using System.IO;
using Sandbox;

namespace GtaV;

internal sealed record MapInstanceBatch( string ModelPath, BBox Bounds, float Distance, Transform[] Transforms )
{
	internal static MapInstanceBatch Read( BinaryReader packet )
	{
		var path = packet.Text();
		var bounds = new BBox( packet.Vector(), packet.Vector() );
		float distance = packet.ReadSingle();
		var transforms = new Transform[packet.ReadInt32()];
		for ( int i = 0; i < transforms.Length; i++ )
		{
			var position = packet.Vector() * PacketReader.UnitsPerMetre;
			var forward = packet.Vector();
			var left = packet.Vector();
			var up = packet.Vector();
			var rotation = forward.LengthSquared > 0 && up.LengthSquared > 0
				? Rotation.LookAt( forward.Normal, up.Normal ) : Rotation.Identity;
			transforms[i] = new Transform( position, rotation, new Vector3(
				Vector3.Dot( forward, rotation.Forward ),
				Vector3.Dot( left, rotation.Left ),
				Vector3.Dot( up, rotation.Up ) ) );
		}
		return new( path, bounds, distance, transforms );
	}

	internal sealed class RenderBatch : SceneCustomObject
	{
		readonly Model model;
		readonly Transform[] transforms;

		internal RenderBatch( SceneWorld world, MapInstanceBatch batch, Model model ) : base( world )
		{
			this.model = model;
			transforms = batch.Transforms;
			var bounds = batch.Bounds;
			Bounds = new BBox( bounds.Mins * PacketReader.UnitsPerMetre, bounds.Maxs * PacketReader.UnitsPerMetre );
			Flags.IsOpaque = true;
		}

		public override void RenderSceneObject()
		{
			if ( model.IsValid() && transforms.Length > 0 )
				Graphics.DrawModelInstanced( model, transforms.AsSpan() );
		}
	}
}
