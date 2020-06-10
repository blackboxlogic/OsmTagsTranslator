using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SQLite;
using System.Linq;
using System.Text;
using OsmSharp;
using OsmSharp.Tags;

namespace OsmTagsTranslator
{
	public static class Translator
	{
		public static OsmGeo[] Transform(IEnumerable<OsmGeo> elements, string sqlTransformation)
		{
			using (var conn = CreateDatabase(@"Data\test.sqlite"))
			{
				conn.Open();
				var tagKeys = elements.Where(e => e.Tags != null)
					.SelectMany(e => e.Tags)
					.GroupBy(t => t.Key)
					.ToDictionary(g => g.Key,
						g => Math.Max(1, g.Max(t => t.Value.Length)));
				CreateElementTable(conn, tagKeys);
				WriteElements(conn, elements);
				//CreateTempTables(conn, dictionaries)
				var tagsCollections = Query(conn, sqlTransformation);
				var transformed = ApplyTags(elements, tagsCollections);

				return transformed;
			}
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

		private static void CreateElementTable(IDbConnection conn, Dictionary<string, int> keys)
		{
			using (var com = conn.CreateCommand())
			{
				var keycode = string.Concat(keys.Select(k => $", [{k.Key}] varchar({k.Value})"));
				com.CommandText = $"CREATE TABLE elements ([id] bigint, [type] varchar(8){keycode}, PRIMARY KEY (id, type))";
				com.ExecuteNonQuery();
			}
		}

		private static void WriteElements(IDbConnection conn, IEnumerable<OsmGeo> elements)
		{
			using (var com = conn.CreateCommand())
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

		private static Dictionary<OsmGeoKey, TagsCollection> Query(IDbConnection conn, string query)
		{
			var results = new Dictionary<OsmGeoKey, TagsCollection>();
			using (var com = conn.CreateCommand())
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
							.Select(i => new Tag(keys[i], (string)values[i]))
							.Where(t => t.Value != null);
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
	}
}
