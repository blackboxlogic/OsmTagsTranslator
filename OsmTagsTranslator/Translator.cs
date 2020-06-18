using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SQLite;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using OsmSharp;
using OsmSharp.Streams;
using OsmSharp.Tags;

namespace OsmTagsTranslator
{
	public class Translator : IDisposable
	{
		private OsmGeo[] Source;
		private IDbConnection Connection;

		public Translator(string pathToOsmFile)
			: this(new XmlOsmStreamSource(File.OpenRead(pathToOsmFile)))
		{ }

		public Translator(IEnumerable<OsmGeo> source)
		{
			Source = source.ToArray();
			Connection = CreateDatabase(@"test.sqlite");
			Connection.Open();

			if (source.Any(e => e.Id == null)) throw new Exception("All elements must have an id");

			var columns = Source.Where(e => e.Tags != null)
				.SelectMany(e => e.Tags)
				.GroupBy(t => t.Key)
				.ToDictionary(g => g.Key,
					g => Math.Max(1, g.Max(t => t.Value.Length)))
				.Prepend(new KeyValuePair<string, int>("Type", 8))
				.Prepend(new KeyValuePair<string, int>("ID", 19));

			CreateTable("Elements", columns, 2);

			foreach (var element in Source)
			{
				InsertRecord("Elements", AsFields(element));
			}
		}

		public OsmGeo[] Transform(string sql)
		{
			var tagsCollections = QueryElements(sql);
			var transformed = ApplyTags(Source, tagsCollections);

			return transformed;
		}

		private static SQLiteConnection CreateDatabase(string path)
		{
			SQLiteConnection.CreateFile(path);
			var builder = new SQLiteConnectionStringBuilder
			{
				DataSource = path,
				Version = 3,
				SyncMode = SynchronizationModes.Off,
				JournalMode = SQLiteJournalModeEnum.Off
			};
			return new SQLiteConnection(builder.ToString());
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
				.Prepend(new KeyValuePair<string, int>("ID", keyLength));
			CreateTable(name, columns);

			foreach (var record in lookup)
			{
				record.Value.Add("ID", record.Key);
				InsertRecord(name, record.Value);
			}
		}

		public void AddLookup(string name, Dictionary<string, string> lookup)
		{
			var columns = new Dictionary<string, int>()
			{
				{ "ID", lookup.Keys.Max(k => k.Length) },
				{ "Value", lookup.Values.Max(k => k.Length) }
			}.OrderBy(kvp => kvp.Key);

			CreateTable(name, columns);

			foreach (var record in lookup)
			{
				var fields = new Dictionary<string, string>()
				{
					{ "ID", record.Key },
					{ "Value", record.Value }
				};
				InsertRecord(name, fields);
			}
		}

		private void CreateTable(string name, IEnumerable<KeyValuePair<string, int>> columns, int keyCount = 1)
		{
			using (var com = Connection.CreateCommand())
			{
				var columnCode = string.Join(", ", columns.Select(k => $"[{k.Key}] varchar({k.Value}) COLLATE NOCASE"));
				var primaryKey = string.Join(", ", columns.Take(keyCount).Select(kvp => "[" + kvp.Key + "]"));
				com.CommandText = $"CREATE TABLE [{name}] ({columnCode}, PRIMARY KEY({primaryKey}))";
				com.ExecuteNonQuery();
			}
		}

		private void InsertRecord(string table, Dictionary<string, string> fields)
		{
			using (var com = Connection.CreateCommand())
			{
				var keys = new List<string>();
				var values = new List<string>();
				int i = 0;

				foreach (var field in fields)
				{
					var parameter = com.CreateParameter();
					parameter.ParameterName = "@p" + i;
					parameter.Value = field.Value;
					com.Parameters.Add(parameter);
					values.Add($"@p" + i);
					keys.Add($"[{field.Key}]");
					i++;
				}

				com.CommandText = $"INSERT INTO [{table}] ({string.Join(", ", keys)}) VALUES ({string.Join(", ", values)});";
				com.ExecuteNonQuery();
			}
		}

		private Dictionary<string, string> AsFields(OsmGeo element)
		{
			var fields = element.Tags.ToDictionary(t => t.Key, t => t.Value);
			fields.Add("ID", element.Id.ToString());
			fields.Add("Type", element.Type.ToString());
			return fields;
		}

		private Dictionary<OsmGeoKey, TagsCollection> QueryElements(string query)
		{
			var results = new Dictionary<OsmGeoKey, TagsCollection>();

			using (var com = Connection.CreateCommand())
			{
				com.CommandText = query;

				using (IDataReader reader = com.ExecuteReader())
				{
					var keys = Enumerable.Range(0, reader.FieldCount).Select(reader.GetName).ToArray();
					if (!string.Equals(keys[0], "ID", StringComparison.OrdinalIgnoreCase)
						|| !string.Equals(keys[1], "Type", StringComparison.OrdinalIgnoreCase))
					{
						throw new Exception("The first two field in the query must be [id] and [type].");
					}

					while (reader.Read())
					{
						var values = GetValues(reader, reader.FieldCount);
						var tags = Enumerable.Range(2, reader.FieldCount - 2)
							.Where(i => !string.IsNullOrEmpty(values[i]) && keys[i] != "ID")
							.Select(i => new Tag(keys[i], values[i]));
						var tagsCollection = new TagsCollection(tags);
						var osmGeoKey = new OsmGeoKey((OsmGeoType)Enum.Parse(typeof(OsmGeoType), values[1]), long.Parse(values[0]));
						results.Add(osmGeoKey, tagsCollection);
					}
				}
			}

			return results;
		}

		private static string[] GetValues(IDataReader reader, int fieldCount)
		{
			var values = new object[fieldCount];
			reader.GetValues(values);
			var strings = new string[fieldCount];

			for (int i = 0; i < fieldCount; i++)
			{
				if (values[i] is string s) strings[i] = s;
			}

			return strings;
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

		public void Dispose()
		{
			Connection.Dispose();
		}
	}
}
