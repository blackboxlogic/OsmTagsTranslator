# OsmTagsTranslator
## Do you need to convert third party data into OpenStreetMap tags, but your programming language is clunky? This tool lets you express OSM tag transformations as SQLite queries.

[This file](https://github.com/blackboxlogic/OsmTagsTranslator/blob/master/OsmTagsTranslator.Tests/SampleE911Addresses.osm) has the address fields from Maine's department of transportation. These tags are not suitable for OSM :unamused:
```xml
<?xml version='1.0' encoding='UTF-8'?>
<osm version='0.6'>
  <node id='-101753' lat='43.73086183589' lon='-70.33776262438'>
    <tag k='ADDRESS_NUMBER' v='18' />
    <tag k='UNIT' v='' />
    <tag k='PREDIR' v='E' />
    <tag k='STREETNAME' v='Lillian' />
    <tag k='SUFFIX' v='Pl' />
    <tag k='POSTDIR' v='' />
    <tag k='POSTAL_COMMUNITY' v='Westbrook' />
    <tag k='STATE' v='ME' />
    <tag k='ZIPCODE' v='04092' />
    <tag k='PLACE_TYPE' v='fire station' />
	...
  </node>
	...
</osm>
```

Each tag needs some work. For example, `addr:street=East Lillian Place` must be composed from { `PREDIR=E`, `STREETNAME=Lillian`, `SUFFIX=Pl`}. We can express these transformations using a [SQLite query](https://github.com/blackboxlogic/OsmTagsTranslator/blob/master/OsmTagsTranslator/Queries/E911AddressesToOsmSchema.sql) :open_mouth:
```sql
SELECT
		-- These first two columns are required to identify elements
		xid,
		xtype,
		-- Every other column becomes a tag
		ADDRESS_NUMBER as [addr:housenumber],
		-- Some tags just need their key changed
		UNIT as [addr:unit],
		-- Other tags need lookups and string manipulation
		COALESCE(pre.Value || ' ', '')
			|| COALESCE(STREETNAME, '')
			|| COALESCE(' ' || suf.Value, '')
			|| COALESCE(' ' || post.Value, '') as [addr:street],
		POSTAL_COMMUNITY as [addr:city],
		STATE as [addr:state],
		ZIPCODE as [addr:postcode],
		-- Null or empty fields don't become tags
		LANDMARK as [name],
		-- Arbitrary tags can be added from more complicated lookup tables
		moreDetails.*
	FROM Elements
	-- Lookup are case-insensitive
	LEFT JOIN Directions as pre
		ON pre.ID = PREDIR
	LEFT JOIN Directions as post
		ON post.ID = POSTDIR
	LEFT JOIN StreetSuffixes as suf
		ON suf.ID = SUFFIX
	LEFT JOIN PlaceTypes as moreDetails
		ON moreDetails.ID = PLACE_TYPE
	-- Filter too, if you'd like. Those elements won't be in the result
	WHERE ADDRESS_NUMBER != '0'
```

JSON files can be loaded as tables with columns "ID" and "Value", like [this one](https://github.com/blackboxlogic/OsmTagsTranslator/blob/master/OsmTagsTranslator/Lookups/StreetSuffixes.json), which expands `SUFFIX=Pl` into `Place` :relieved:
```javascript
{
	...
    "PIKES": "Pike",
    "PINE": "Pine",
    "PINES": "Pines",
    "PNES": "Pines",
    "PL": "Place",
    "PLAIN": "Plain",
    "PLN": "Plain",
    "PLAINS": "Plains",
	...
}
```

Run `> OsmTagsTranslatorConsole.exe SampleE911Addresses.osm Lookups\Directions.json Lookups\StreetSuffixes.json Lookups\PlaceTypes.json Quieries\E911AddressesToOsmSchema.sql`

The [resulting file](https://github.com/blackboxlogic/OsmTagsTranslator/blob/master/OsmTagsTranslator.Tests/E911AddressesToOsmSchema.sql%2BSampleE911Addresses.osm) is transformed into OSM conformant tags! :mage::tophat::rabbit2:
```xml
<?xml version='1.0' encoding='UTF-8'?>
<osm version='0.6'>
  <node id='-101753' lat='43.73086183589' lon='-70.33776262438'>
    <tag k='addr:housenumber' v='18' />
    <tag k='addr:city' v='Westbrook' />
    <tag k='addr:state' v='ME' />
    <tag k='addr:street' v='East Lillian Place' />
    <tag k='addr:postcode' v='04092' />
    <tag k="amenity" v="fire_station" />
  <node>
  ...
</osm>
```

This project is an executable, interactive command line tool, and a nuget package. Running in a command prompt without a sql script like `> OsmTagsTranslatorConsole.exe SampleE911Addresses.osm` lets you do data analysis
```SQL
SELECT POSTAL_COMMUNITY, count(1) FROM Elements GROUP BY POSTAL_COMMUNITY ORDER BY 2 DESC
```