using System.Collections.Generic;
using System.Linq;

namespace Sandbox.Mounting;

/// <summary>
/// Represents a directory in the resource tree structure.
/// Contains child directories and resource files.
/// </summary>
public class ResourceFolder
{
	/// <summary>
	/// The name of this directory
	/// </summary>
	public string Name { get; private set; }

	/// <summary>
	/// The full path to this directory
	/// </summary>
	public string Path { get; private set; }

	/// <summary>
	/// Parent directory. Null if this is the root.
	/// </summary>
	public ResourceFolder Parent { get; private set; }

	/// <summary>
	/// Child directories
	/// </summary>
	public List<ResourceFolder> Folders { get; private set; } = new();

	/// <summary>
	/// Resource files in this directory
	/// </summary>
	public List<ResourceLoader> Files { get; private set; } = new();

	/// <summary>
	/// Check if this is the root directory
	/// </summary>
	public bool IsRoot => Parent == null;

	/// <summary>
	/// Get the depth level of this directory (0 for root)
	/// </summary>
	public int Depth
	{
		get
		{
			int depth = 0;
			var current = Parent;
			while ( current != null )
			{
				depth++;
				current = current.Parent;
			}
			return depth;
		}
	}

	private ResourceFolder( string name, string path, ResourceFolder parent )
	{
		Name = name;
		Path = path;
		Parent = parent;
	}

	/// <summary>
	/// Create a root directory
	/// </summary>
	internal static ResourceFolder CreateRoot()
	{
		return new ResourceFolder( "", "", null );
	}

	/// <summary>
	/// Build the directory tree for a new mount. Discard the index once the
	/// tree is complete so callers can continue to edit the public folder lists.
	/// </summary>
	internal void AddResources( IEnumerable<ResourceLoader> resources )
	{
		var children = new Dictionary<ResourceFolder, Dictionary<string, ResourceFolder>>();
		foreach ( var resource in resources )
		{
			var parts = resource.RelativePath.Split( '/', StringSplitOptions.RemoveEmptyEntries );
			if ( parts.Length == 0 ) continue;

			var directory = this;
			for ( int i = 0; i < parts.Length - 1; i++ )
			{
				if ( !children.TryGetValue( directory, out var index ) )
				{
					index = new Dictionary<string, ResourceFolder>( StringComparer.OrdinalIgnoreCase );
					foreach ( var existing in directory.Folders )
						index.TryAdd( existing.Name, existing );
					children.Add( directory, index );
				}

				var name = parts[i];
				if ( !index.TryGetValue( name, out var child ) )
				{
					var path = directory.IsRoot ? name : $"{directory.Path}/{name}";
					child = new ResourceFolder( name, path, directory );
					directory.Folders.Add( child );
					index.Add( name, child );
				}
				directory = child;
			}

			directory.Files.Add( resource );
			resource.Folder = directory;
		}
	}
	/// <summary>
	/// Get all files recursively from this directory and all subdirectories
	/// </summary>
	public IEnumerable<ResourceLoader> GetAllFilesRecursive()
	{
		foreach ( var file in Files )
			yield return file;

		foreach ( var folder in Folders )
		{
			foreach ( var file in folder.GetAllFilesRecursive() )
				yield return file;
		}
	}

	/// <summary>
	/// Build a directory tree from a collection of resource loaders
	/// </summary>
	internal static ResourceFolder BuildTree( IEnumerable<ResourceLoader> resources )
	{
		var root = CreateRoot();

		root.AddResources( resources );

		return root;
	}

	/// <summary>
	/// Returns true if this directory or any subdirectory contains a resource of the specified type
	/// </summary>
	public bool ContainsType( ResourceType type )
	{
		return Files.Any( f => f.Type == type ) || Folders.Any( d => d.ContainsType( type ) );
	}

}
