# OsmTagsTranslator
## Have you ever wanted to run Sql against an `.osm` file? Me neither. But now you can!

Use SQLite scripts to transform element tags. Pass it a path to an `.osm` file which has JSON dictionaries and SQL queries in the same folder.

[This file](https://github.com/blackboxlogic/OsmTagsTranslator/blob/master/OsmTagsTranslatorConsole/Data/SampleE911Addresses.osm) has the fields from Maine's E911 data, not suitable for OSM :frowning_face:
```xml
<node id='-101753' lat='43.73086183589' lon='-70.33776262438'>
    <tag k='ADDRESS' v='18 Lillian Pl' />
    <tag k='ADDRESS_NUMBER' v='18' />
    <tag k='LOC' v='' />
    <tag k='Latitude' v='43.730854' />
    <tag k='Longitude' v='-70.337763' />
    <tag k='POSTAL_COMMUNITY' v='Westbrook' />
    <tag k='POSTDIR' v='' />
    <tag k='PREDIR' v='E' />
    <tag k='PSAP' v='Westbrook PD' />
    <tag k='STATE' v='ME' />
    <tag k='STREETNAME' v='Lillian' />
    <tag k='SUFFIX' v='Pl' />
    <tag k='TPL' v='' />
    <tag k='UNIT' v='' />
    <tag k='ZIPCODE' v='04092' />
	...
</node>
```

Every JSON file in the same folder gets loaded as a look-up table like [this one](https://github.com/blackboxlogic/OsmTagsTranslator/blob/master/OsmTagsTranslatorConsole/Data/Directions.json], which expands `PREDIR` :confused:
```javascript
{
	"N": "North",
	"NE": "North East",
	"E": "East",
	"SE": "South East",
	"S": "South",
	"SW": "South West",
	"W": "West",
	"NW": "North West"
}
```

Write a [SQLite transformation](https://github.com/blackboxlogic/OsmTagsTranslator/blob/master/OsmTagsTranslatorConsole/Data/E911AddressesToOsmSchema.sql) :open_mouth:
```sql
SELECT
		id, -- Required first column
		type, -- Required second column
		ADDRESS_NUMBER as [addr:housenumber], -- Every other column becomes a tag with that name
		POSTAL_COMMUNITY as [addr:city],
		ZIPCODE as [addr:postcode],
		STATE as [addr:state],
		COALESCE(pre.LookupValue || ' ', '')
			|| COALESCE(STREETNAME, '')
			|| COALESCE(' ' || suf.LookupValue, '')
			|| COALESCE(' ' || post.LookupValue, '') as [addr:street],
		UNIT as [addr:unit],
		LANDMARK as [name] -- Null or empty tags get thrown out
	FROM Elements
	-- JSON dictionary files become joinable tables (with the same name as the file) with columns: "LookupKey" and "LookupValue"
	LEFT JOIN Directions as pre
		ON pre.LookupKey = PREDIR -- These lookup are case-insensitive
	LEFT JOIN Directions as post
		ON post.LookupKey = POSTDIR
	LEFT JOIN StreetSuffix as suf
		ON suf.LookupKey = SUFFIX
	WHERE ADDRESS_NUMBER != '0' -- Filter too, because why not?
```

Run > `OsmTagsTranslatorConsole.exe Data\SampleE911Addresses.osm`
The [resulting file](https://github.com/blackboxlogic/OsmTagsTranslator/blob/master/OsmTagsTranslatorConsole/Data/E911AddressesToOsmSchema.sql%2BSampleE911Addresses.osm) has tags transformed by SQL into the OSM schema!
```xml
<node id='-101753' lat='43.73086183589' lon='-70.33776262438'>
    <tag k='addr:housenumber' v='18' />
    <tag k='addr:city' v='Westbrook' />
    <tag k='addr:state' v='ME' />
    <tag k='addr:street' v='East Lillian Place' />
    <tag k='addr:postcode' v='04092' />
</node>
```
:mage::tophat::rabbit2:

The [Data folder](https://github.com/blackboxlogic/OsmTagsTranslator/tree/master/OsmTagsTranslatorConsole/Data) has the complete example.