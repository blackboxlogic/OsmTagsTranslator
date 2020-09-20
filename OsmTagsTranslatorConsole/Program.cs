using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OsmSharp;
using OsmSharp.API;
using OsmSharp.Streams;
using OsmTagsTranslator;

namespace Example
{
	public class Program
	{
		public static void Main(string[] args)
		{
			Console.WriteLine("Starting...");
			var osmFile = args.SingleOrDefault(a => a.EndsWith(".osm") || a.EndsWith(".pbf"));

			if (osmFile == null) PrintUsage();

			var doNodes = args.Contains("-N", StringComparer.OrdinalIgnoreCase);
			var doWays = args.Contains("-W", StringComparer.OrdinalIgnoreCase);
			var doRelations = args.Contains("-R", StringComparer.OrdinalIgnoreCase);

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
					SaveFile(asOsm, destination);
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
					else if (command != "")
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
				return new PBFOsmStreamSource(new FileInfo(path).OpenRead());
			}
			else if (extension.Equals(".osm", StringComparison.OrdinalIgnoreCase))
			{
				return new XmlOsmStreamSource(File.OpenRead(path));
			}

			throw new Exception("Source file must be .pbf or .osm");
		}

		private static void SaveFile(Osm osm, string path)
		{
			var serializer = new System.Xml.Serialization.XmlSerializer(typeof(Osm));
			var settings = new System.Xml.XmlWriterSettings();
			settings.OmitXmlDeclaration = true;
			settings.Indent = false;
			settings.NewLineChars = string.Empty;
			var emptyNamespace = new System.Xml.Serialization.XmlSerializerNamespaces();
			emptyNamespace.Add(string.Empty, string.Empty);

			using (var resultStream = new FileStream(path, FileMode.Create))
			using (var stringWriter = System.Xml.XmlWriter.Create(resultStream, settings))
			{
				serializer.Serialize(stringWriter, osm, emptyNamespace);
			}
		}

		private static Osm AsOsm(IEnumerable<OsmGeo> elements, string generator = "OsmTagsTranslator", double? version = .6)
		{
			var e = elements.GroupBy(e => e.Type).ToDictionary(g => g.Key, g => g.ToArray());

			return new Osm()
			{
				Nodes = e.TryGetValue(OsmGeoType.Node, out var nodes) ? (Node[])nodes : new Node[0],
				Ways = e.TryGetValue(OsmGeoType.Way, out var ways) ? (Way[])ways : new Way[0],
				Relations = e.TryGetValue(OsmGeoType.Relation, out var relations) ? (Relation[])relations : new Relation[0],
				Version = version,
				Generator = generator
			};
		}
	}
}
