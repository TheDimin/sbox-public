using System;
using System.Collections.Generic;

namespace GtaV;

internal readonly record struct LodEntityId( uint Map, int Index );

/// <summary>Selects one representation on each resident branch and signals replacement handoffs.</summary>
internal sealed class LodVisibility<T>( Action<T, bool> setVisible, Action<T> showReplacement = null )
{
	sealed record Node( T Object, uint RequiredChildren )
	{
		internal bool? Selected;
	}
	readonly List<T> reveal = new();
	readonly Dictionary<LodEntityId, Node> resident = new();
	readonly Dictionary<LodEntityId, LodEntityId?> ancestry = new();
	readonly Dictionary<LodEntityId, HashSet<LodEntityId>> children = new();

	internal Action<LodEntityId, string> SelectionEnded { get; set; }

	internal bool IsSelected( LodEntityId id ) => resident.TryGetValue( id, out var node ) && node.Selected == true;

	internal void Add( LodEntityId id, T obj, uint requiredChildren, LodEntityId? parent )
	{
		for ( var ancestor = parent; ancestor.HasValue; )
		{
			if ( ancestor == id )
			{
				parent = null;
				break;
			}
			ancestor = ancestry.GetValueOrDefault( ancestor.Value );
		}

		ancestry[id] = parent;
		resident.Add( id, new( obj, requiredChildren ) );
		if ( parent.HasValue )
		{
			if ( !children.TryGetValue( parent.Value, out var siblings ) ) children[parent.Value] = siblings = new();
			siblings.Add( id );
		}
		RefreshBranch( id );
	}

	internal void Remove( LodEntityId id )
	{
		if ( !resident.Remove( id, out var node ) ) return;
		setVisible( node.Object, false );
		RefreshBranch( id, node.Selected == true );
		if ( node.Selected == true ) SelectionEnded?.Invoke( id, "removed" );
		while ( !resident.ContainsKey( id ) && !children.ContainsKey( id ) && ancestry.Remove( id, out var link ) && link is { } parent )
		{
			var siblings = children[parent];
			siblings.Remove( id );
			if ( siblings.Count == 0 ) children.Remove( parent );
			id = parent;
		}
	}

	// Called by the timing observer after a removal refresh, before ancestry is pruned.
	internal string ReplacementAfterRemoval( LodEntityId id )
	{
		var parent = ancestry.GetValueOrDefault( id );
		while ( parent.HasValue )
		{
			if ( IsSelected( parent.Value ) ) return "coarser";
			parent = ancestry.GetValueOrDefault( parent.Value );
		}
		return HasSelectedDescendant( id ) ? "finer" : "removed";
	}

	bool HasSelectedDescendant( LodEntityId id )
	{
		if ( children.TryGetValue( id, out var group ) )
			foreach ( var child in group )
				if ( IsSelected( child ) || HasSelectedDescendant( child ) ) return true;
		return false;
	}

	void RefreshBranch( LodEntityId id, bool replacing = false )
	{
		while ( ancestry.GetValueOrDefault( id ) is { } parent ) id = parent;
		Apply( id, true, ref replacing );
		foreach ( var obj in reveal )
		{
			if ( replacing && showReplacement is not null ) showReplacement( obj );
			else setVisible( obj, true );
		}
		reveal.Clear();
	}

	void Apply( LodEntityId id, bool allowed, ref bool replacing )
	{
		resident.TryGetValue( id, out var node );
		children.TryGetValue( id, out var group );
		bool useChildren = node is null;
		if ( node is not null )
		{
			int ready = 0;
			if ( group is not null )
				foreach ( var child in group )
					if ( resident.ContainsKey( child ) ) ready++;
			useChildren = ready >= Math.Max( 1u, node.RequiredChildren );
			bool selected = allowed && !useChildren;
			if ( selected != node.Selected )
			{
				if ( node.Selected == true ) SelectionEnded?.Invoke( id, allowed ? "finer" : "coarser" );
				replacing |= node.Selected == true;
				node.Selected = selected;
				if ( selected ) reveal.Add( node.Object );
				else setVisible( node.Object, false );
			}
		}
		if ( group is not null )
			foreach ( var child in group ) Apply( child, allowed && useChildren, ref replacing );
	}
}
