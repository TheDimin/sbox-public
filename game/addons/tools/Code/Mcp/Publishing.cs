using Sandbox;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace Editor.Mcp;

[McpToolset( "publish", "Local package file previews. Never uploads or publishes." )]
public static class PublishTools
{
	/// <summary>Builds the real local file list for a registered library source package, including its PublishExclude rules. Returns file names and sizes without source contents. Does not upload, publish, compile assets or change scenes. Use read_console after a failure.</summary>
	/// <param name="project">Full library ident, such as org.library.</param>
	/// <param name="limit">Maximum file entries to return (1–500).</param>
	/// <param name="offset">File entries to skip for paging.</param>
	[McpTool.ReadOnly( "publish_source_preview" )]
	public static async Task<PublishSourcePreview> Preview( string project, int limit = 100, int offset = 0 )
	{
		var source = EditorUtility.Projects.GetAll().FirstOrDefault( x => string.Equals( x.Config.FullIdent, project, StringComparison.OrdinalIgnoreCase ) )
			?? throw new InvalidOperationException( $"No registered project named '{project}'." );
		if ( !source.IsSourcePublish() ) throw new InvalidOperationException( "Source previews require a library project." );
		var publisher = await ProjectPublisher.FromProject( source );
		var files = publisher.Manifest.Assets.OrderBy( x => x.Name, StringComparer.OrdinalIgnoreCase ).ToArray();
		limit = Math.Clamp( limit, 1, 500 );
		offset = Math.Max( offset, 0 );
		var page = files.Skip( offset ).Take( limit ).Select( x => new PublishSourceFile { Name = x.Name, Bytes = x.Size } ).ToArray();
		return new PublishSourcePreview
		{
			Project = source.Config.FullIdent,
			PublishExclude = source.Config.PublishExclude,
			Total = files.Length,
			TotalBytes = files.Sum( x => (long)x.Size ),
			Offset = offset,
			Showing = page.Length,
			Files = page,
			Errors = publisher.Manifest.Errors.ToArray()
		};
	}
}

public sealed class PublishSourcePreview
{
	public string Project { get; set; }
	public string PublishExclude { get; set; }
	public int Total { get; set; }
	public long TotalBytes { get; set; }
	public int Offset { get; set; }
	public int Showing { get; set; }
	public PublishSourceFile[] Files { get; set; }
	public string[] Errors { get; set; }
}

public sealed class PublishSourceFile
{
	public string Name { get; set; }
	public long Bytes { get; set; }
}
