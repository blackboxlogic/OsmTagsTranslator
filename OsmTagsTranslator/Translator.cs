using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using OsmSharp;
using OsmSharp.Db;
using OsmSharp.Db.Impl;
using OsmSharp.Streams;
using OsmSharp.Tags;

namespace OsmTagsTranslator
{
	public class Translator : Database, IDisposable
	{
		private const string LookupId = "ID";
		private const string LookupValue = "Value";
		private const string ElementKeyId = "xID";
		private const string ElementKeyType = "xType";
		private const string ElementTableName = "Elements";
		private const string DatabaseFile = "OsmSql.sqlite";

		private readonly IEnumerable<OsmGeo> Source;
		private readonly bool DisposeTheSource;

		/// <param name="allowMixedCaseKeys">
		/// Set this TRUE if any of your tag keys have inconsistant capitalization and
		/// you're ok with them being treated as equivalent
		/// ("Name" and "name" will be treated as the same tag). Leave this FALSE (default)
		/// if you know your elements don't have inconsistant tag key capitalization.
		/// </param>
		public Translator(string pathToOsmFile, bool allowMixedCaseKeys = false)
			: this(new XmlOsmStreamSource(File.OpenRead(pathToOsmFile)), allowMixedCaseKeys)
		{
			DisposeTheSource = true;
		}

		/// <param name="allowMixedCaseKeys">
		/// Set this TRUE if any of your tag keys have inconsistant capitalization and
		/// you're ok with them being treated as equivalent
		/// ("Name" and "name" will be treated as the same tag). Leave this FALSE (default)
		/// if you know your elements don't have inconsistant tag key capitalization.
		/// </param>
		public Translator(IEnumerable<OsmGeo> source, bool allowMixedCaseKeys = false,
			bool doNodes = false, bool doWays = false, bool doRelations = false)
			: base(DefaultDatabaseFile, true)
		{
			Source = source.Where(ee => ee.Tags != null).ToArray();

			var doAll = !doRelations && !doWays && !doNodes;
			var toTranslate = Source.Where(e => doAll
				|| (e.Type == OsmGeoType.Node && doNodes)
				|| (e.Type == OsmGeoType.Way && doWays)
				|| (e.Type == OsmGeoType.Relation && doRelations));
			var columns = GetTags(toTranslate, allowMixedCaseKeys);

			ImportElements(toTranslate, columns);
		}

		private Dictionary<string, int> GetTags(IEnumerable<OsmGeo> source, bool allowMixedCaseKeys)
		{
			var tagGroups = source.Where(e => e.Tags != null)
				.SelectMany(e => e.Tags)
				.GroupBy(t => t.Key, StringComparer.OrdinalIgnoreCase);

			if (source.Any(e => e.Id == null)) throw new Exception("All elements must have an id");

			if (!allowMixedCaseKeys)
			{
				var mixedCase = tagGroups.Select(gs => gs.Select(g => g.Key).Distinct().ToArray()).Where(g => g.Length > 1).ToArray();

				if (mixedCase.Any())
				{
					throw new Exception("Tag keys have inconsistant capitalization: " + string.Join(", ", mixedCase.SelectMany(k => k)));
				}
			}

			return tagGroups.ToDictionary(g => g.Key, g => Math.Max(1, g.Max(t => t.Value.Length)));
		}

		private void ImportElements(IEnumerable<OsmGeo> source, Dictionary<string, int> allTags)
		{
			var columns = allTags
				.Prepend(new KeyValuePair<string, int>(ElementKeyType, 8))
				.Prepend(new KeyValuePair<string, int>(ElementKeyId, 19));

			CreateTable(ElementTableName, columns, 2);

			InsertRecords(ElementTableName, columns.Select(f => f.Key), source.Select(AsFields));
		}

		public OsmGeo[] QueryElements(string sql)
		{
			var tagsCollections = QueryTags(sql);
			var transformed = ApplyTags(Source, tagsCollections);

			return transformed;
		}

		public OsmGeo[] QueryElementsWithChildren(string sql)
		{
			var tagsCollections = QueryTags(sql);
			var transformed = ApplyTags(Source, tagsCollections);
			var index = OsmSharp.Db.Impl.Extensions.CreateSnapshotDb(new MemorySnapshotDb(Source));

			return WithChildren(transformed, index).ToArray();
		}

		public OsmGeo[] QueryElementsKeepAll(string sql)
		{
			var tagsCollections = QueryTags(sql);
			var transformed = ApplyTags(Source, tagsCollections);

			return transformed.Union(Source).GroupBy(e => new OsmGeoKey(e)).Select(g => g.First()).ToArray();
		}

		public void AddLookup(string path)
		{
			JsonSerializer serializer = new JsonSerializer();

			try
			{
				using (StreamReader file = File.OpenText(path))
				{
					var lookup = serializer.Deserialize<Dictionary<string, string>>(new JsonTextReader(file));
					AddLookup(Path.GetFileNameWithoutExtension(path), lookup);
				}
			}
			catch
			{
				using (StreamReader file = File.OpenText(path))
				{
					var lookup = serializer.Deserialize<Dictionary<string, Dictionary<string, string>>>(new JsonTextReader(file));
					AddLookup(Path.GetFileNameWithoutExtension(path), lookup);
				}
			}
		}

		public void AddLookup(string name, Dictionary<string, Dictionary<string, string>> lookup)
		{
			var keyLength = lookup.Keys.Max(k => k.Length);
			var columns = lookup.SelectMany(d => d.Value).GroupBy(kvp => kvp.Key).ToDictionary(g => g.Key, g => g.Max(kvp => kvp.Value.Length))
				.Prepend(new KeyValuePair<string, int>(LookupId, keyLength));
			CreateTable(name, columns);

			foreach (var record in lookup)
			{
				record.Value.Add(LookupId, record.Key);
			}

			InsertRecords(name, columns.Select(kvp => kvp.Key), lookup.Values);
		}

		public void AddLookup(string name, Dictionary<string, string> lookup)
		{
			var columns = new Dictionary<string, int>()
			{
				{ LookupId, lookup.Keys.Max(k => k.Length) },
				{ LookupValue, lookup.Values.Max(k => k.Length) }
			}.OrderBy(kvp => kvp.Key);

			CreateTable(name, columns);

			var records = lookup.Select(record => new Dictionary<string, string>()
				{
					{ LookupId, record.Key },
					{ LookupValue, record.Value }
				});

			InsertRecords(name, columns.Select(kvp => kvp.Key), records);
		}

		private Dictionary<string, string> AsFields(OsmGeo element)
		{
			var fields = element.Tags?.ToDictionary(t => t.Key, t => t.Value) ?? new Dictionary<string, string>();
			fields.Add(ElementKeyId, element.Id.ToString());
			fields.Add(ElementKeyType, element.Type.ToString());
			return fields;
		}

		private Dictionary<OsmGeoKey, TagsCollection> QueryTags(string sql)
		{
			var results = new Dictionary<OsmGeoKey, TagsCollection>();
			var records = Query(sql).ToArray();
			var keys = records[0];

			if (string.Join(",", keys.Take(2)).ToLower() != $"{ElementKeyId},{ElementKeyType}".ToLower())
				throw new Exception($"The first two field in the query must be [{ElementTableName}.{ElementKeyId}] and [{ElementTableName}.{ElementKeyType}].");

			foreach(var record in records.Skip(1))
			{
				var osmGeoKey = new OsmGeoKey((OsmGeoType)Enum.Parse(typeof(OsmGeoType), record[1]), long.Parse(record[0]));
				var tags = Enumerable.Range(2, record.Length - 2)
					.Where(i => !string.IsNullOrEmpty(record[i]) && keys[i] != LookupId)
					.Select(i => new Tag(keys[i], record[i]));
				var tagsCollection = new TagsCollection(tags);
				results.Add(osmGeoKey, tagsCollection);
			}

			return results;
		}

		private static OsmGeo[] ApplyTags(IEnumerable<OsmGeo> elements,
			Dictionary<OsmGeoKey, TagsCollection> tagsCollections)
		{
			var results = new List<OsmGeo>(tagsCollections.Count);

			foreach (var element in elements)
			{
				var key = new OsmGeoKey(element);
				if (tagsCollections.TryGetValue(key, out var tags))
				{
					element.Tags = tags;
					results.Add(element);
				}
			}

			return results.ToArray();
		}

		public static Dictionary<K, T[]> ToDictionary<T, K>(IEnumerable<IGrouping<K, T>> groups)
		{
			return groups.ToDictionary(g => g.Key, g => g.ToArray());
		}

		public new void Dispose()
		{
			if (DisposeTheSource) ((IDisposable)Source).Dispose();
			base.Dispose();
			File.Delete(DatabaseFile);
		}

		private static IEnumerable<OsmGeo> WithChildren(IEnumerable<OsmGeo> parents, IOsmGeoSource possibleChilden)
		{
			return parents.SelectMany(p => WithChildren(p, possibleChilden)).GroupBy(e => new OsmGeoKey(e)).Select(g => g.First());
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
							? WithChildren(child, possibleChilden) // warning, infinite recursion if references have circular members
							: Enumerable.Empty<OsmGeo>();
					})
					.Where(m => m != null)
					.Append(parent);
			}
			throw new Exception("OsmGeo wasn't a Node, Way or Relation.");
		}
	}
}
