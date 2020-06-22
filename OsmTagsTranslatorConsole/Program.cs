using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OsmSharp;
using OsmSharp.API;
using OsmSharp.IO.Xml;
using OsmSharp.Streams;
using OsmTagsTranslator;

namespace Example
{
	class Program
	{
		static void Main(string[] args)
		{
			Console.WriteLine("Starting...");
			var osmFile = args.SingleOrDefault(a => a.EndsWith(".osm") || a.EndsWith(".pbf"));

			if (osmFile == null) PrintUsage();

			using (var osm = OpenFile(osmFile))
			using (var translator = new Translator(osm, true))
			{
				foreach (var json in args.Where(a => a.EndsWith(".json")))
				{
					translator.AddLookup(json);
				}

				var sql = args.SingleOrDefault(a => a.EndsWith(".sql") || a.EndsWith(".sqlite"));
				if (sql != null)
				{
					var elements = args.Contains("-KeepAllElements", StringComparer.OrdinalIgnoreCase)
						? translator.QueryElementsKeepAll(File.ReadAllText(sql))
						: args.Contains("-KeepChildrenElements", StringComparer.OrdinalIgnoreCase)
							? translator.QueryElementsWithChildren(File.ReadAllText(sql))
							: translator.QueryElements(File.ReadAllText(sql));
					var asOsm = AsOsm(elements);
					var destination = Path.GetFileName(sql) + "+" + Path.GetFileName(osmFile);
					File.WriteAllText(destination, asOsm.SerializeToXml());
				}
				else
				{
					DoInteractive(translator);
				}
			}
		}

		private static void PrintUsage()
		{
			Console.WriteLine("Usage: <source>.{osm|pbf} [<lookup>.json ...] [<transformation>.{sql|sqlite}] [-KeepAllElements|-KeepChildrenElements]");
			Console.WriteLine("\t-KeepAllElements Include elements from the source which were not returned from the query.");
			Console.WriteLine("\t-KeepChildrenElements Include elements from the source which were not returned from the query but are children of elements in the query.");
			Console.WriteLine("Example: StateAddresses.osm StreetSuffixes.json Directions.josn StateAddressesToOsmSchema.sql");
			Environment.Exit(1);
		}

		private static void DoInteractive(Translator translator)
		{
			Console.Write("> ");
			var sql = Console.ReadLine();

			while (sql != "")
			{
				try
				{
					foreach (var record in translator.Query(sql))
					{
						Console.WriteLine(string.Join('\t', record));
					}
				}
				catch (Exception e) { Console.WriteLine(e); }

				Console.Write("> ");
				sql = Console.ReadLine();
			}
		}

		private static OsmStreamSource OpenFile(string path)
		{
			var extension = Path.GetExtension(path);

			if (extension.Equals(".pbf", StringComparison.OrdinalIgnoreCase))
			{
				// Warning: IDisposable not disposed.
				return new OsmSharp.Streams.PBFOsmStreamSource(new FileInfo(path).OpenRead());
			}
			else if (extension.Equals(".osm", StringComparison.OrdinalIgnoreCase))
			{
				return new XmlOsmStreamSource(File.OpenRead(path));
			}

			throw new Exception("Must be .pbf or .osm");
		}

		private static Osm AsOsm(IEnumerable<OsmGeo> elements, string generator = null, double? version = .6)
		{
			return new Osm()
			{
				Nodes = elements.OfType<Node>().ToArray(),
				Ways = elements.OfType<Way>().ToArray(),
				Relations = elements.OfType<Relation>().ToArray(),
				Version = version,
				Generator = generator
			};
		}
	}
}
