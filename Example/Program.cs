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
			var source = new XmlOsmStreamSource(File.OpenRead(args[0]));
			var translator = new Translator(source);

			foreach (var file in Directory.GetFiles(".", "*.json"))
			{
				translator.AddLookup(file);
			}

			foreach (var file in Directory.GetFiles(".", "*.sql"))
			{
				var sql = File.ReadAllText(file);
				var elements = translator.Transform(sql);
				var osm = new Osm() { Nodes = elements.OfType<Node>().ToArray() };
				File.WriteAllText(file+ args[0], osm.SerializeToXml());
			}

			Console.ReadKey(true);
		}
	}
}
