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
		private const string LookupId = "ID";
		private const string LookupValue = "Value";
		private const string ElementKeyId = "xID";
		private const string ElementKeyType = "xType";
		private const string ElementTableName = "Elements";
		private const string DatabaseFile = "OsmSql.sqlite";

		private readonly IDbConnection Connection;
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
		public Translator(IEnumerable<OsmGeo> source, bool allowMixedCaseKeys = false)
		{
			Source = source.ToArray();
			Connection = CreateDatabase(DatabaseFile);
			Connection.Open();
			var columns = GetTags(Source, allowMixedCaseKeys);
			ImportElements(Source, columns);
		}

		private Dictionary<string, int> GetTags(IEnumerable<OsmGeo> source, bool allowMixedCaseKeys)
		{
			var tagGroups = source.Where(e => e.Tags != null)
				.SelectMany(e => e.Tags)
				.GroupBy(t => t.Key, StringComparer.OrdinalIgnoreCase);

			if (source.Any(e => e.Id == null)) throw new Exception("All elements must have an id");

			if (!allowMixedCaseKeys)
			{
				var mixedCase = tagGroups.Select(g => g.Select(g => g.Key).Distinct().ToArray()).Where(g => g.Length > 1).ToArray();

				if (mixedCase.Any())
				{
					throw new Exception("Tag keys have inconsistant capitalization: " + string.Join(", ", mixedCase.SelectMany(k => k)));
				}
			}

			return tagGroups.ToDictionary(g => g.Key, g => Math.Max(1, g.Max(t => t.Value.Length)));
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

		private void ImportElements(IEnumerable<OsmGeo> source, Dictionary<string, int> tags)
		{
			var columns = tags
				.Prepend(new KeyValuePair<string, int>(ElementKeyType, 8))
				.Prepend(new KeyValuePair<string, int>(ElementKeyId, 19));

			CreateTable(ElementTableName, columns, 2);

			var count = source.Count();
			var soFar = 0;

			using (var com = Connection.CreateCommand())
			{
				using (var transaction = Connection.BeginTransaction())
				{
					foreach (var element in source)
					{
						InsertRecord(ElementTableName, AsFields(element), com);
					}
					transaction.Commit();
				}
			}
		}

		public OsmGeo[] QueryElements(string sql)
		{
			var tagsCollections = QueryTags(sql);
			var transformed = ApplyTags(Source, tagsCollections);

			return transformed;
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

			using (var com = Connection.CreateCommand())
			{
				foreach (var record in lookup)
				{
					record.Value.Add(LookupId, record.Key);
					InsertRecord(name, record.Value, com);
				}
			}
		}

		public void AddLookup(string name, Dictionary<string, string> lookup)
		{
			var columns = new Dictionary<string, int>()
			{
				{ LookupId, lookup.Keys.Max(k => k.Length) },
				{ LookupValue, lookup.Values.Max(k => k.Length) }
			}.OrderBy(kvp => kvp.Key);

			CreateTable(name, columns);
			using (var com = Connection.CreateCommand())
			{
				foreach (var record in lookup)
				{
					var fields = new Dictionary<string, string>()
				{
					{ LookupId, record.Key },
					{ LookupValue, record.Value }
				};
					InsertRecord(name, fields, com);
				}
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

		private void InsertRecord(string table, Dictionary<string, string> fields, IDbCommand com)
		{
			var keys = new List<string>();
			var values = new List<string>();
			int i = 0;

			com.Parameters.Clear();
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

		// first record is column names
		public IEnumerable<string[]> Query(string sql)
		{
			using (var com = Connection.CreateCommand())
			{
				com.CommandText = sql;
				using (IDataReader reader = com.ExecuteReader())
				{
					yield return Enumerable.Range(0, reader.FieldCount).Select(reader.GetName).ToArray();

					while (reader.Read())
					{
						yield return GetValues(reader, reader.FieldCount);
					}
				}
			}
		}

		private static string[] GetValues(IDataReader reader, int fieldCount)
		{
			var values = new object[fieldCount];
			reader.GetValues(values);
			var strings = new string[fieldCount];

			for (int i = 0; i < fieldCount; i++)
			{
				if (values[i] is string s) strings[i] = s;
				else if (values[i] is long num) strings[i] = num.ToString();
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

		public static Dictionary<K, T[]> ToDictionary<T, K>(IEnumerable<IGrouping<K, T>> groups)
		{
			return groups.ToDictionary(g => g.Key, g => g.ToArray());
		}

		public void Dispose()
		{
			if (DisposeTheSource) ((IDisposable)Source).Dispose();
			Connection.Dispose();
			File.Delete(DatabaseFile);
		}
	}
}
