using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OsmSharp;
using OsmSharp.API;
using OsmSharp.Db;
using OsmSharp.Db.Impl;
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
			var osmFile = args.Single(a => a.EndsWith(".osm") || a.EndsWith(".pbf"));

			using (var osm = OpenFile(osmFile))
			using (var translator = new Translator(osm, true))
			{
				foreach (var json in args.Where(a => a.EndsWith(".json")))
				{
					translator.AddLookup(json);
				}

				var sql = args.SingleOrDefault(a => a.EndsWith(".sql"));
				if (sql != null)
				{
					var elements = translator.QueryElements(File.ReadAllText(sql));
					var index = OsmSharp.Db.Impl.Extensions.CreateSnapshotDb(new MemorySnapshotDb(osm));
					var result = AsOsm(WithChildren(elements, index));
					var destination = Path.GetFileName(sql) + "+" + Path.GetFileName(osmFile);
					File.WriteAllText(destination, result.SerializeToXml());
				}
				else
				{
					DoInteractive(translator);
				}
			}
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

		private static IEnumerable<OsmGeo> WithChildren(IEnumerable<OsmGeo> parents, IOsmGeoSource possibleChilden)
		{
			return parents.SelectMany(p => WithChildren(p, possibleChilden));
		}

		private static IEnumerable<OsmGeo> WithChildren(OsmGeo parent, IOsmGeoSource possibleChilden)
		{
			if (parent is Node) return new[] { parent };
			else if (parent is Way way)
			{
				return way.Nodes.Select(n => possibleChilden.Get(OsmGeoType.Node, n))
					.Append(parent);
			}
			else if (parent is Relation relation)
			{
				return relation.Members
					.SelectMany(m =>
					{
						var child = possibleChilden.Get(m.Type, m.Id);
						return child != null
							? WithChildren(child, possibleChilden)
							: Enumerable.Empty<OsmGeo>();
					})
					.Where(m => m != null)
					.Append(parent);
			}
			throw new Exception("OsmGeo wasn't a Node, Way or Relation.");
		}
	}
}
