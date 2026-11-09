using System;
using Sandbox;

namespace Editor;

/// <summary>Bounded editor-only bodygroup metadata for a live model.</summary>
public static class ModelBodygroupReadback
{
	public const int MaxGroups = 64;
	public const int MaxChoices = 64;
	public enum Status { Success, WrongThread, InvalidModel, InvalidLimits, LimitExceeded, InvalidData }
	public sealed record Choice( int Index, string Name, ulong Mask );
	public sealed record Group( int Index, string Name, ulong Mask, Choice[] Choices );
	public sealed record Snapshot( ulong DefaultMask, Group[] Groups, int MeshGroupCount );
	public static Status Read( Model model, int maxGroups, int maxChoices, out Snapshot result )
	{
		result = null;
		if ( !ThreadSafe.IsMainThread ) return Status.WrongThread;
		if ( model == null || !model.IsValid || model.IsError ) return Status.InvalidModel;
		if ( maxGroups < 0 || maxGroups > MaxGroups || maxChoices < 0 || maxChoices > MaxChoices ) return Status.InvalidLimits;
		int count = model.native.GetNumBodyParts();
		if ( count < 0 ) return Status.InvalidData;
		if ( count > maxGroups ) return Status.LimitExceeded;
		var sizes = new int[count]; int total = 0;
		for ( int i = 0; i < count; i++ )
		{
			int n = model.native.GetNumBodyPartMeshes( i );
			if ( n < 0 ) return Status.InvalidData;
			if ( n > maxChoices - total ) return Status.LimitExceeded;
			sizes[i] = n; total += n;
		}
		var groups = new Group[count];
		for ( int i = 0; i < count; i++ )
		{
			string name = model.native.GetBodyPartName( i );
			if ( string.IsNullOrWhiteSpace( name ) || name.Length > 1024 ) return Status.InvalidData;
			var choices = new Choice[sizes[i]];
			for ( int j = 0; j < choices.Length; j++ )
			{
				string choice = model.native.GetBodyPartMeshName( i, j );
				if ( string.IsNullOrWhiteSpace( choice ) || choice.Length > 1024 ) return Status.InvalidData;
				choices[j] = new Choice( j, choice, model.native.GetBodyPartMeshMask( i, j ) );
			}
			groups[i] = new Group( i, name, model.native.GetBodyPartMask( i ), choices );
		}
		int meshGroupCount = model.native.GetNumMeshGroups();
		if ( meshGroupCount < 0 || meshGroupCount > 64 ) return Status.InvalidData;
		result = new Snapshot( model.native.GetDefaultMeshGroupMask(), groups, meshGroupCount );
		return Status.Success;
	}
}
