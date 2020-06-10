-- Executes SQLite
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
	-- JSON dictionary files become joinable tables (with the same name) with columns: "LookupKey" and "LookupValue"
	LEFT JOIN Directions as pre
		ON pre.LookupKey = PREDIR
	LEFT JOIN Directions as post
		ON post.LookupKey = POSTDIR
	LEFT JOIN StreetSuffix as suf
		ON suf.LookupKey = SUFFIX
	WHERE ADDRESS_NUMBER != '0' -- Filter too, because why not?