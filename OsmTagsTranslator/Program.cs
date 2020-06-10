using System;
using System.IO;
using System.Linq;
using OsmSharp;
using OsmSharp.Streams;

namespace OsmTagsTranslator
{
	public static class Program
	{
		static void Main(string[] args)
		{
			var source = new XmlOsmStreamSource(File.OpenRead(@"Data\Maine_E911_Roads.osm")).OfType<Way>().Take(10);
			var sql = "SELECT [id], [type], [RDNAME] as [name], [RDCLASS] as [highway] FROM Elements LIMIT 5";

			var elements = Translator.Transform(source.ToArray(), sql);

			foreach (var element in elements)
			{
				Console.WriteLine(element);
			}

			Console.ReadKey(true);
		}
	}
}
