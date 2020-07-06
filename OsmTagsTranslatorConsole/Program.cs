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

			var doNodes = args.Contains("-N");
			var doWays = args.Contains("-W");
			var doRelations = args.Contains("-R");

			using (var osm = OpenFile(osmFile))
			using (var translator = new Translator(osm, true, doNodes, doWays, doRelations))
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
					RunInteractive(translator);
				}
			}
		}

		private static void PrintUsage()
		{
			Console.WriteLine("Usage: <source>.{osm|pbf} [<lookup>.json ...] [<transformation>.{sql|sqlite}] [-KeepAllElements|-KeepChildrenElements]");
			Console.WriteLine("\t-KeepAllElements Include elements from the source which were not returned from the query.");
			Console.WriteLine("\t-KeepChildrenElements Include elements from the source which were not returned from the query but are children of elements in the query.");
			Console.WriteLine("\t-N only transform nodes.");
			Console.WriteLine("\t-W only transform ways.");
			Console.WriteLine("\t-R only transform relations.");
			Console.WriteLine("Example: StateAddresses.osm StreetSuffixes.json Directions.josn StateAddressesToOsmSchema.sql");
			Environment.Exit(1);
		}

		private static void RunInteractive(Translator translator)
		{
			Console.WriteLine("Ready for instructions:");
			Console.WriteLine("\t- A SQLite query");
			Console.WriteLine("\t- A path to a SQLite query file");
			Console.WriteLine("\t- A path to a json lookup table");
			Console.WriteLine("\t- \"exit\"");
			Console.WriteLine();

			string command = "SELECT Count(*) AS ElementCount FROM Elements";
			Console.WriteLine(command);

			while (true)
			{
				if (command == "exit") return;

				try
				{
					if (command.EndsWith(".json"))
					{
						translator.AddLookup(File.ReadAllText(command));
						Console.WriteLine("Loaded " + command);
					}
					else
					{
						if (command.EndsWith(".sql")) command = File.ReadAllText(command);

						var records = translator.Query(command);
						var display = string.Join(Environment.NewLine, records.Select(record => string.Join('\t', record)));
						Console.WriteLine(display);
					}
				}
				catch (Exception e)
				{
					Console.WriteLine(e);
				}

				Console.WriteLine();
				Console.Write("> ");
				command = Console.ReadLine();
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
