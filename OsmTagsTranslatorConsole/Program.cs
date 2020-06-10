using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
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
			var directory = Path.GetDirectoryName(args[0]);
			Console.WriteLine("Reading osm elements from: " + args[0]);
			var source = new XmlOsmStreamSource(File.OpenRead(args[0]));
			var translator = new Translator(source);

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
				var destination = file + "+" + Path.GetFileName(args[0]);
				Console.WriteLine("Writing results to " + destination);
				File.WriteAllText(destination, osm.SerializeToXml());
			}

			Console.WriteLine("Finished");
			Console.ReadKey(true);
		}
	}
}
