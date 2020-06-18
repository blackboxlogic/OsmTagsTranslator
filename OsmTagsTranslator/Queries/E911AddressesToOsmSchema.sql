-- Executes SQLite
SELECT
		Elements.id, -- Required first column
		Elements.type, -- Required second column
		Elements.ADDRESS_NUMBER as [addr:housenumber], -- Every other column becomes a tag with that name
		POSTAL_COMMUNITY as [addr:city],
		ZIPCODE as [addr:postcode],
		STATE as [addr:state],
		COALESCE(pre.value || ' ', '')
			|| STREETNAME
			|| COALESCE(' ' || suf.value, '')
			|| COALESCE(' ' || post.value, '') as [addr:street],
		UNIT as [addr:unit],
		LANDMARK as [name], -- Null or empty tags get thrown out
		placeTags.*
	FROM Elements
	-- JSON dictionary files become joinable tables (with the same name) with columns: "id" and "value"
	LEFT JOIN Directions as pre
		ON pre.id = PREDIR
	LEFT JOIN Directions as post
		ON post.id = POSTDIR
	LEFT JOIN StreetSuffixes as suf
		ON suf.id = SUFFIX
	LEFT JOIN PlaceTypes as placeTags
		ON placeTags.id = PLACE_TYPE
	WHERE ADDRESS_NUMBER != '0' -- Filter too, because why not?