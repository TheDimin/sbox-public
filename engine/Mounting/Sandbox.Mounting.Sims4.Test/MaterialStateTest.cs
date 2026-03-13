using Sims4Reader;
using Sims4Reader.Material;
using Sims4Reader.Mesh;
using Sims4Reader.Rcol;

namespace Sims4MountTest;

/// <summary>
/// Tests that validate MTST (MaterialState) chunks embedded inside MODL RCOLs.
/// MTST maps material states (Default, Dirty, Burnt, etc.) to MATD references.
/// </summary>
[TestClass]
public class MaterialStateTest
{
	/// <summary>
	/// Verify that MTST chunks exist inside MODL RCOLs and can be parsed.
	/// </summary>
	[TestMethod]
	public void MtstChunks_ExistInsideModlRcol()
	{
		var path = TestHelper.GetPackagePath();
		using var pkg = DbpfPackage.Open( path );

		int modlCount = 0;
		int withMtst = 0;

		foreach ( var entry in pkg.FindAll( ResourceType.Model ).Take( 100 ) )
		{
			try
			{
				var rcol = pkg.GetResource<RcolContainer>( entry );
				modlCount++;

				var mtst = rcol.GetChunk<MaterialState>();
				if ( mtst != null )
					withMtst++;
			}
			catch { }
		}

		Console.WriteLine( $"MODLs: {modlCount}, with MTST: {withMtst}" );
		Assert.IsTrue( modlCount > 0, "No MODL resources found." );
		Assert.IsTrue( withMtst > 0, "No MODL RCOLs contain MTST chunks." );
	}

	/// <summary>
	/// Verify that MTST DefaultMatdIndex resolves to a MATD chunk within the same RCOL.
	/// </summary>
	[TestMethod]
	public void Mtst_DefaultStateEntry_ResolvesToMatd()
	{
		var path = TestHelper.GetPackagePath();
		using var pkg = DbpfPackage.Open( path );

		int mtstCount = 0;
		int withDefaultEntry = 0;
		int resolvedToMatd = 0;

		foreach ( var entry in pkg.FindAll( ResourceType.Model ).Take( 100 ) )
		{
			try
			{
				var rcol = pkg.GetResource<RcolContainer>( entry );
				var mtst = rcol.GetChunk<MaterialState>();
				if ( mtst == null ) continue;
				mtstCount++;

				// Find the Default state entry's MatdIndex
				uint matdRef = 0;
				if ( mtst.Entries200 != null )
					foreach ( var e in mtst.Entries200 )
						if ( e.State == MaterialStateType.Default )
						{ matdRef = e.MatdIndex; break; }
				if ( matdRef == 0 && mtst.Entries300 != null )
					foreach ( var e in mtst.Entries300 )
						if ( e.State == MaterialStateType.Default )
						{ matdRef = e.MatdIndex; break; }

				if ( matdRef == 0 ) continue;
				withDefaultEntry++;

				int localIdx = ChunkReference.GetTgiIndex( matdRef );
				if ( localIdx < 0 ) continue;

				if ( ChunkReference.IsDelayed( matdRef ) )
				{
					if ( localIdx < rcol.ExternalReferences.Length )
					{
						var extEntry = pkg.Find( rcol.ExternalReferences[localIdx] );
						if ( extEntry != null )
						{
							var extRcol = pkg.GetResource<RcolContainer>( extEntry.Value );
							if ( extRcol.GetChunk<MaterialDefinition>() != null )
								resolvedToMatd++;
						}
					}
				}
				else
				{
					int absIdx = ChunkReference.ResolveChunkIndex( matdRef, rcol.PublicChunks );
					if ( absIdx >= 0 && absIdx < rcol.ChunkEntries.Count )
					{
						if ( rcol.ChunkEntries[absIdx].Chunk is MaterialDefinition )
							resolvedToMatd++;
					}
				}
			}
			catch { }
		}

		Console.WriteLine( $"MTSTs: {mtstCount}, with Default entry: {withDefaultEntry}, resolved to MATD: {resolvedToMatd}" );
		Assert.IsTrue( mtstCount > 0, "No MTST chunks found." );
		// MTSTs may not all have Default state entries — just verify at least some resolve
		if ( withDefaultEntry > 0 )
			Assert.IsTrue( resolvedToMatd > 0, "No MTST Default state entries resolve to MATD." );
	}

	/// <summary>
	/// Diagnostic: version distribution and state types in MTST chunks from MODL RCOLs.
	/// </summary>
	[TestMethod]
	public void Diagnostic_Mtst_VersionAndStateDistribution()
	{
		var path = TestHelper.GetPackagePath();
		using var pkg = DbpfPackage.Open( path );

		var versionCounts = new Dictionary<uint, int>();
		var stateCounts = new Dictionary<MaterialStateType, int>();
		int totalEntries = 0;

		foreach ( var entry in pkg.FindAll( ResourceType.Model ).Take( 200 ) )
		{
			try
			{
				var rcol = pkg.GetResource<RcolContainer>( entry );
				var mtst = rcol.GetChunk<MaterialState>();
				if ( mtst == null ) continue;

				versionCounts.TryGetValue( mtst.Version, out var vc );
				versionCounts[mtst.Version] = vc + 1;

				if ( mtst.Entries200 != null )
					foreach ( var e in mtst.Entries200 )
					{
						totalEntries++;
						stateCounts.TryGetValue( e.State, out var sc );
						stateCounts[e.State] = sc + 1;
					}
				if ( mtst.Entries300 != null )
					foreach ( var e in mtst.Entries300 )
					{
						totalEntries++;
						stateCounts.TryGetValue( e.State, out var sc );
						stateCounts[e.State] = sc + 1;
					}
			}
			catch { }
		}

		Console.WriteLine( "MTST version distribution:" );
		foreach ( var (ver, count) in versionCounts.OrderBy( kv => kv.Key ) )
			Console.WriteLine( $"  0x{ver:X}: {count}" );

		Console.WriteLine( $"\nState entries: {totalEntries}" );
		foreach ( var (state, count) in stateCounts.OrderByDescending( kv => kv.Value ) )
			Console.WriteLine( $"  {state}: {count}" );

		Assert.IsTrue( versionCounts.Count > 0, "No MTST versions found." );
	}
}
