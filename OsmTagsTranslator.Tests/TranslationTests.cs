using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OsmSharp;
using OsmSharp.Streams;
using OsmSharp.Tags;

namespace OsmTagsTranslator.Tests
{
	[TestClass]
	public class TranslationTests
	{
		[TestMethod]
		public void CaseInsensitiveLookups()
		{
			var element = new Node() { Id = -1, Tags = new TagsCollection()
				{
					new Tag("matched", "matched"),
					new Tag("unmatched", "UnMaTcHeD"),
				}
			};

			using (Translator translator = new Translator(new[] { element }))
			{
				var lookup = new Dictionary<string, string>() { { "matched", "matched" }, { "unmatched", "unmatched" }};
				translator.AddLookup("case", lookup);
				var query =
@"select elements.xid, elements.xtype, matched.value as matched, unmatched.value as unmatched
	from elements
	left join [case] as matched on matched.id = elements.matched
	left join [case] as unmatched on unmatched.id = elements.unmatched";
				var result = translator.QueryElements(query);
				Assert.AreEqual(result.Single().Tags.Count, 2);
			}
		}

		[TestMethod]
		public void NonElementQueries()
		{
			using (Translator translator = new Translator(new[] { new Node() { Id = -1 }, new Node() { Id = -2 } }))
			{
				var query = @"select count(1) as count from elements where xid = -1";
				var result = translator.Query(query);
				CollectionAssert.AreEqual(new[] { "count", "1" }, result.SelectMany(s => s).ToArray());
			}
		}

		[TestMethod]
		public void FullTranslateAddresses()
		{
			var sourceFile = "SampleE911Addresses.osm";
			var source = new XmlOsmStreamSource(File.OpenRead(sourceFile));

			using (Translator translator = new Translator(source))
			{
				translator.AddLookup("Lookups\\Directions.json");
				translator.AddLookup("Lookups\\PlaceTypes.json");
				translator.AddLookup("Lookups\\StreetSuffixes.json");
				var sqlFile = "Queries\\E911AddressesToOsmSchema.sql";
				var expectedFile = "E911AddressesToOsmSchema.sql+SampleE911Addresses.osm";

				var results = translator.QueryElements(File.ReadAllText(sqlFile));
				var expecteds = new XmlOsmStreamSource(File.OpenRead(expectedFile)).ToArray();

				Assert.AreEqual(results.Length, expecteds.Length);
				var index = results.ToDictionary(e => e.Id);

				foreach (var expected in expecteds)
				{
					Assert.IsTrue(index.ContainsKey(expected.Id));
					var result = index[expected.Id];
					Assert.AreEqual(result.Tags.Count, expected.Tags.Count);

					foreach (var expectedTag in expected.Tags)
					{
						Assert.IsTrue(result.Tags.Contains(expectedTag));
					}
				}
			}
		}
	}
}
