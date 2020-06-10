using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SQLite;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using OsmSharp;
using OsmSharp.Tags;

namespace OsmTagsTranslator
{
	public class Translator : IDisposable
	{
		private OsmGeo[] Source;
		private IDbConnection Connection;

		public Translator(IEnumerable<OsmGeo> source)
		{
			Source = source.ToArray();
			Connection = CreateDatabase(@"Data\test.sqlite");
			Connection.Open();

			var tagKeys = Source.Where(e => e.Tags != null)
				.SelectMany(e => e.Tags)
				.GroupBy(t => t.Key)
				.ToDictionary(g => g.Key,
					g => Math.Max(1, g.Max(t => t.Value.Length)));
			CreateElementTable(tagKeys);
			WriteElements(Source);
		}

		public OsmGeo[] Transform(string sql)
		{
			var tagsCollections = Query(sql);
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

		private void CreateElementTable(Dictionary<string, int> columns)
		{
			using (var com = Connection.CreateCommand())
			{
				var keycode = string.Concat(columns.Select(k => $", [{k.Key}] varchar({k.Value})"));
				com.CommandText = $"CREATE TABLE elements ([id] bigint, [type] varchar(8){keycode}, PRIMARY KEY (id, type))";
				com.ExecuteNonQuery();
			}
		}

		public void AddLookup(string path)
		{
			JsonSerializer serializer = new JsonSerializer();

			using (StreamReader file = File.OpenText(path))
			{
				var lookup = serializer.Deserialize<Dictionary<string, string>>(new JsonTextReader(file));
				AddLookup(Path.GetFileNameWithoutExtension(path), lookup);
			}
		}

		public void AddLookup(string name, Dictionary<string, string> lookup)
		{
			using (var com = Connection.CreateCommand())
			{
				var keyLength = lookup.Keys.Max(k => k.Length);
				var valueLength = lookup.Values.Max(k => k.Length);
				com.CommandText = $"CREATE TABLE [{name}] (lookupKey varchar({keyLength}) PRIMARY KEY COLLATE NOCASE, lookupValue varchar({valueLength}))";
				com.ExecuteNonQuery();

				foreach (var record in lookup)
				{
					var parameter = com.CreateParameter();
					parameter.ParameterName = "@p0";
					parameter.Value = record.Key;
					com.Parameters.Add(parameter);

					parameter = com.CreateParameter();
					parameter.ParameterName = "@p1";
					parameter.Value = record.Value;
					com.Parameters.Add(parameter);

					com.CommandText = $"INSERT INTO [{name}] ([lookupKey], [lookupValue]) values (@p0, @p1);";
					com.ExecuteNonQuery();
				}
			}
		}

		private void WriteElements(IEnumerable<OsmGeo> elements)
		{
			using (var com = Connection.CreateCommand())
			{
				foreach (var element in elements)
				{
					var keys = new StringBuilder();
					var values = new StringBuilder();
					if (element.Tags != null)
					{
						int i = 0;
						foreach (var tag in element.Tags)
						{
							var parameter = com.CreateParameter();
							parameter.ParameterName = "@p" + i;
							parameter.Value = tag.Value;
							com.Parameters.Add(parameter);
							values.Append($", @p" + i);
							keys.Append($", [{tag.Key}]");
							i++;
						}
					}

					//com.CreateParameter();
					com.CommandText = $"INSERT INTO Elements ([id], [type]{keys}) VALUES ({element.Id}, '{element.Type}'{values});";
					com.ExecuteNonQuery();
				}
			}
		}

		private Dictionary<OsmGeoKey, TagsCollection> Query(string query)
		{
			var results = new Dictionary<OsmGeoKey, TagsCollection>();
			using (var com = Connection.CreateCommand())
			{
				com.CommandText = query;

				using (IDataReader reader = com.ExecuteReader())
				{
					var keys = Enumerable.Range(0, reader.FieldCount).Select(reader.GetName).ToArray();
					if (keys[0] != "id" || keys[1] != "type") throw new Exception("The first two field in the query must be [id] and [type].");

					while (reader.Read())
					{
						var values = new object[reader.FieldCount];
						reader.GetValues(values);
						var tags = Enumerable.Range(2, reader.FieldCount - 2)
							.Select(i => new Tag(keys[i], values[i] == DBNull.Value ? null : (string)values[i]))
							.Where(t => !string.IsNullOrEmpty(t.Value));
						var tagsCollection = new TagsCollection(tags);
						var osmGeoKey = new OsmGeoKey((OsmGeoType)Enum.Parse(typeof(OsmGeoType), (string)values[1]), (long)values[0]);
						results.Add(osmGeoKey, tagsCollection);
					}
				}
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

		public void Dispose()
		{
			Connection.Dispose();
		}
	}
}
