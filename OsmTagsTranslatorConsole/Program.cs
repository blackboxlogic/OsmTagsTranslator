using System;
using System.IO;
using System.Linq;
using OsmSharp;
using OsmSharp.API;
using OsmSharp.IO.Xml;
using OsmTagsTranslator;

namespace Example
{
	class Program
	{
		static void Main(string[] args)
		{
			var targetOsmFile = args[0];
			var directory = Path.GetDirectoryName(targetOsmFile);
			Console.WriteLine("Reading osm elements from: " + targetOsmFile);

			using (var translator = new Translator(targetOsmFile))
			{

				foreach (var file in Directory.GetFiles(directory, "*.json"))
				{
					Console.WriteLine("Adding lookup table: " + file);
					translator.AddLookup(file);
				}

				foreach (var file in Directory.GetFiles(directory, "*.sql"))
				{
					Console.WriteLine("Running: " + file);
					var sql = File.ReadAllText(file);
					var elements = translator.Transform(sql);
					var osm = new Osm() { Nodes = elements.OfType<Node>().ToArray(), Version = .6 };
					var destination = file + "+" + Path.GetFileName(targetOsmFile);
					Console.WriteLine("Writing results to " + destination);
					File.WriteAllText(destination, osm.SerializeToXml());
				}
			}

			Console.WriteLine("Finished");
			Console.ReadKey(true);
		}
	}
}
