using System.Text.Json;
using Sims4Reader;
using Sims4Reader.Resources;

namespace Sims4MountTest;

[TestClass]
public class CatalogObjectJsonTest
{
	[TestMethod]
	public void CatalogObject_SerializesToJson()
	{
		var packagePath = TestHelper.GetPackagePath();
		using var package = DbpfPackage.Open( packagePath );

		var entry = package.FindAll( ResourceType.CatalogObject ).FirstOrDefault();
		if ( entry.Key.Type == ResourceType.Unknown )
			Assert.Fail( "No COBJ resources found in package." );

		var cobj = package.GetResource<CatalogObjectResource>( entry );
		var json = SerializeCobj( cobj );

		Assert.IsNotNull( json );
		Assert.IsTrue( json.Length > 0 );

		var doc = JsonDocument.Parse( json );
		var root = doc.RootElement;

		Assert.IsTrue( root.GetProperty( "Version" ).GetUInt32() > 0, "Expected version > 0." );
	}

	[TestMethod]
	public void CatalogObject_JsonContainsAllFields()
	{
		var packagePath = TestHelper.GetPackagePath();
		using var package = DbpfPackage.Open( packagePath );

		CatalogObjectResource? source = null;
		foreach ( var entry in package.FindAll( ResourceType.CatalogObject ).Take( 500 ) )
		{
			var cobj = package.GetResource<CatalogObjectResource>( entry );
			if ( cobj.IsFullyParsed && cobj.Tags.Length > 0 )
			{
				source = cobj;
				break;
			}
		}

		if ( source == null )
			Assert.Inconclusive( "No fully-parsed COBJ with tags found." );

		var json = SerializeCobj( source );
		var root = JsonDocument.Parse( json ).RootElement;

		Assert.AreEqual( source.Version, root.GetProperty( "Version" ).GetUInt32() );
		Assert.AreEqual( source.SimoleonPrice, root.GetProperty( "Price" ).GetUInt32() );
		Assert.AreEqual( source.IsFullyParsed, root.GetProperty( "IsFullyParsed" ).GetBoolean() );
		Assert.AreEqual( source.IsStackable, root.GetProperty( "IsStackable" ).GetBoolean() );
		Assert.AreEqual( source.CanDepreciate, root.GetProperty( "CanDepreciate" ).GetBoolean() );
		Assert.AreEqual( source.CatalogFilterColors.Length, root.GetProperty( "CatalogFilterColors" ).GetArrayLength() );
		Assert.AreEqual( source.Tags.Length, root.GetProperty( "Tags" ).GetArrayLength() );
		Assert.AreEqual( source.TgiReferences.Length, root.GetProperty( "TgiReferences" ).GetArrayLength() );
	}

	[TestMethod]
	public void CatalogObject_TagsPreserveCategoryAndValue()
	{
		var packagePath = TestHelper.GetPackagePath();
		using var package = DbpfPackage.Open( packagePath );

		CatalogObjectResource? source = null;
		foreach ( var entry in package.FindAll( ResourceType.CatalogObject ).Take( 500 ) )
		{
			var cobj = package.GetResource<CatalogObjectResource>( entry );
			if ( cobj.Tags.Length > 0 )
			{
				source = cobj;
				break;
			}
		}

		if ( source == null )
			Assert.Inconclusive( "No COBJ with tags found." );

		var json = SerializeCobj( source );
		var tags = JsonDocument.Parse( json ).RootElement.GetProperty( "Tags" );

		for ( int i = 0; i < source.Tags.Length; i++ )
		{
			var tag = tags[i];
			Assert.AreEqual( source.Tags[i].Category, tag.GetProperty( "Category" ).GetUInt16(),
				$"Tag[{i}].Category mismatch." );
			Assert.AreEqual( source.Tags[i].Value, tag.GetProperty( "Value" ).GetUInt16(),
				$"Tag[{i}].Value mismatch." );
		}
	}

	[TestMethod]
	public void CatalogObject_BuyCategoryResolved()
	{
		var packagePath = TestHelper.GetPackagePath();
		using var package = DbpfPackage.Open( packagePath );

		bool anyHasKnownCategory = false;
		foreach ( var entry in package.FindAll( ResourceType.CatalogObject ).Take( 1000 ) )
		{
			var cobj = package.GetResource<CatalogObjectResource>( entry );
			var json = SerializeCobj( cobj );
			var root = JsonDocument.Parse( json ).RootElement;

			var buyCategory = root.GetProperty( "BuyCategory" ).GetString();
			Assert.IsNotNull( buyCategory, "BuyCategory should never be null." );

			if ( buyCategory != "unknown" )
				anyHasKnownCategory = true;
		}

		Assert.IsTrue( anyHasKnownCategory,
			"Expected at least one COBJ to have a resolved buy category." );
	}

	[TestMethod]
	public void CatalogObject_TgiReferencesFormatted()
	{
		var packagePath = TestHelper.GetPackagePath();
		using var package = DbpfPackage.Open( packagePath );

		string? json = null;
		foreach ( var entry in package.FindAll( ResourceType.CatalogObject ).Take( 500 ) )
		{
			var cobj = package.GetResource<CatalogObjectResource>( entry );
			if ( cobj.IsFullyParsed && cobj.TgiReferences.Length > 0 )
			{
				json = SerializeCobj( cobj );
				break;
			}
		}

		if ( json == null )
			Assert.Inconclusive( "No fully-parsed COBJ with TGI references found." );

		var tgiRefs = JsonDocument.Parse( json ).RootElement.GetProperty( "TgiReferences" );
		Assert.IsTrue( tgiRefs.GetArrayLength() > 0 );

		foreach ( var tgi in tgiRefs.EnumerateArray() )
		{
			var str = tgi.GetString()!;
			Assert.IsFalse( string.IsNullOrWhiteSpace( str ),
				"TGI reference string should not be empty." );
			Assert.IsTrue( str.Contains( '_' ),
				$"TGI reference should contain underscore separator: {str}" );
		}
	}

	[TestMethod]
	public void CatalogObject_MultipleSerializeSuccessfully()
	{
		var packagePath = TestHelper.GetPackagePath();
		using var package = DbpfPackage.Open( packagePath );

		var entries = package.FindAll( ResourceType.CatalogObject ).Take( 50 ).ToList();
		if ( entries.Count == 0 )
			Assert.Fail( "No COBJ resources found in package." );

		int successCount = 0;
		foreach ( var entry in entries )
		{
			var cobj = package.GetResource<CatalogObjectResource>( entry );
			var json = SerializeCobj( cobj );
			if ( json != null )
			{
				var root = JsonDocument.Parse( json ).RootElement;
				if ( root.GetProperty( "Version" ).GetUInt32() > 0 )
					successCount++;
			}
		}

		Assert.AreEqual( entries.Count, successCount,
			"All COBJs should serialize to JSON successfully." );
	}

	[TestMethod]
	public void CatalogObject_ResolvesNameFromStringTable()
	{
		var packagePath = TestHelper.GetPackagePath();
		using var package = DbpfPackage.Open( packagePath );

		var stbl = package.GetStringTable();
		if ( stbl == null )
			Assert.Inconclusive( "No string table found in test package." );

		string? resolvedName = null;
		foreach ( var entry in package.FindAll( ResourceType.CatalogObject ).Take( 500 ) )
		{
			var cobj = package.GetResource<CatalogObjectResource>( entry );
			resolvedName = stbl.GetString( cobj.NameHash );
			if ( resolvedName != null )
				break;
		}

		Assert.IsNotNull( resolvedName, "Expected at least one COBJ to have a resolvable name in the string table." );
		Assert.IsTrue( resolvedName.Length > 0, "Resolved name should not be empty." );
	}

	/// <summary>
	/// Mirrors CatalogObjectLoader.ToJsonObject + JsonSerializer.Serialize
	/// to produce the same output without needing the loader infrastructure.
	/// </summary>
	private static string SerializeCobj( CatalogObjectResource cobj )
	{
		var tags = new List<object>();
		foreach ( var tag in cobj.Tags )
		{
			tags.Add( new
			{
				Category = tag.Category,
				Value = tag.Value,
				CategoryName = BuyCategoryTag.GetCategoryName( tag.Category ),
				ValueName = BuyCategoryTag.GetCategoryName( tag.Value ),
			} );
		}

		var tgiRefs = new List<string>();
		foreach ( var tgi in cobj.TgiReferences )
		{
			tgiRefs.Add( $"{tgi.Type} {tgi.Group:X}_{tgi.Instance:X}" );
		}

		var catalogFilterColors = new List<string>();
		foreach ( var argb in cobj.CatalogFilterColors )
		{
			catalogFilterColors.Add( $"#{argb:X8}" );
		}

		return JsonSerializer.Serialize( new
		{
			Version = cobj.Version,
			Price = cobj.SimoleonPrice,
			BuyCategory = BuyCategoryTag.GetCategory( cobj.Tags ) ?? "unknown",
			PackId = cobj.PackId,
			SwatchSortPriority = cobj.SwatchColorsSortPriority,
			IsStackable = cobj.IsStackable,
			CanDepreciate = cobj.CanDepreciate,
			CatalogFilterColors = catalogFilterColors,
			Tags = tags,
			TgiReferences = tgiRefs,
			IsFullyParsed = cobj.IsFullyParsed,
		}, new JsonSerializerOptions
		{
			WriteIndented = true,
			DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
		} );
	}
}
