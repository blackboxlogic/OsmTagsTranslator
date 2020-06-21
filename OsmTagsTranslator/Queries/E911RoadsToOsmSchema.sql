-- This is an example transofrmation, designed for Maine's E911 road dataset.

-- Data Corrections, these could be separated out
Update elements set route_num = '' where route_num = '0';
Update elements set route_num = '202/4' where route_num = '2002/4';
Update elements set speed = '' where speed in ('0', '2', '3', '253');

-- Schema Translation
SELECT
		Elements.xid, -- Required first column
		Elements.xtype, -- Required second column

		COALESCE(pre.value || ' ', '')
			|| STREETNAME
			|| COALESCE(' ' || suf.value, '')
			|| COALESCE(' ' || post.value, '') as [name],

		SPEED || ' mph' as [maxspeed],

		CASE ONEWAY
			WHEN 'FT' THEN 'yes'
			WHEN 'TF' THEN '-1' -- way could be reversed, oneway changed to yes
		END as [oneway],

		-- split on /, add ME_ (or US_ for 1, 1A, 2, 2A, 201, 201A, 202, 302), join with ;
		CASE ROUTE_NUM
			WHEN '' THEN null
			ELSE
				RTRIM(
					Replace(
					Replace(
					Replace(
					Replace(
					Replace(
					Replace(
					Replace(
					Replace(
						'ME ' || replace(ROUTE_NUM, '/', ';ME ') || ';',
					'ME 1;', 'US 1;'),
					'ME 1A;', 'US 1A;'),
					'ME 2;', 'US 2;'),
					'ME 2A;', 'US 2A;'),
					'ME 201;', 'US 201;'),
					'ME 201A;', 'US 201A;'),
					'ME 202;', 'US 202;'),
					'ME 302;', 'US 302;')
				, ';')
		END as [ref],

		otherTags.*, -- highway:residential could be {primary, seconday, tertiary, residential}

		OBJECTID as [e911objectid] -- Remove in POST
	FROM Elements
	LEFT JOIN Directions as pre
		ON pre.id = PREDIR
	LEFT JOIN Directions as post
		ON post.id = POSTDIR
	LEFT JOIN StreetSuffixes as suf
		ON suf.id = SUFFIX
	LEFT JOIN RoadClasses as otherTags
		ON otherTags.id = RDCLASS
	WHERE highway != 'proposed'