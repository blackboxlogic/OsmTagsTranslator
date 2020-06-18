using System;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OsmSharp.Streams;

namespace OsmTagsTranslator.Tests
{
	[TestClass]
	public class UnitTest1
	{
		[TestMethod]
		public void TranslateAddresses()
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

				var results = translator.Transform(File.ReadAllText(sqlFile));
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
