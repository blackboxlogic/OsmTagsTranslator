SELECT
		-- These first two columns are required to identify the element
		xid,
		xtype,
		-- Every other column becomes a tag
		-- Some tags just need their key changed
		ADDRESS_NUMBER as [addr:housenumber],
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
		ON moreDetails.id = PLACE_TYPE
	-- Filter too, if you'd like. Those elements won't be in the result
	WHERE ADDRESS_NUMBER != '0'
