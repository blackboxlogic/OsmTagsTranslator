using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SQLite;
using System.Linq;

namespace OsmTagsTranslator
{
	public class Database : IDisposable
	{
		private readonly IDbConnection Connection;
		public static string DefaultDatabaseFile = "OsmSql.sqlite";

		protected Database(string filePath, bool createNew)
		{
			if (createNew)
			{
				SQLiteConnection.CreateFile(filePath);
			}

			Connection = OpenDatabase(filePath);
			Connection.Open();
		}

		private static SQLiteConnection OpenDatabase(string path)
		{
			var builder = new SQLiteConnectionStringBuilder
			{
				DataSource = path,
				Version = 3,
				SyncMode = SynchronizationModes.Off,
				JournalMode = SQLiteJournalModeEnum.Off
			};
			return new SQLiteConnection(builder.ToString());
		}

		protected void CreateTable(string name, IEnumerable<KeyValuePair<string, int>> columns, int keyCount = 1)
		{
			using (var com = Connection.CreateCommand())
			{
				var columnCode = string.Join(", ", columns.Select(k => $"[{k.Key}] varchar({k.Value}) COLLATE NOCASE"));
				var primaryKey = string.Join(", ", columns.Take(keyCount).Select(kvp => "[" + kvp.Key + "]"));
				com.CommandText = $"CREATE TABLE [{name}] ({columnCode}, PRIMARY KEY({primaryKey}))";
				com.ExecuteNonQuery();
			}
		}

		protected void InsertRecords(string table, IEnumerable<string> columns, IEnumerable<Dictionary<string, string>> source)
		{
			using (var com = Connection.CreateCommand())
			{
				using (var transaction = Connection.BeginTransaction())
				{
					var allParameters = new Dictionary<string, IDbDataParameter>(StringComparer.OrdinalIgnoreCase);
					int i = 0;

					foreach (var column in columns)
					{
						var parameter = com.CreateParameter();
						parameter.ParameterName = "@p" + i;
						com.Parameters.Add(parameter);
						allParameters.Add(column, parameter);
						i++;
					}

					var fieldNames = string.Join(", ", allParameters.Keys.Select(k => $"[{k}]"));
					var parameterNames = string.Join(", ", allParameters.Values.Select(p => p.ParameterName));
					com.CommandText = $"INSERT INTO [{table}] ({fieldNames}) VALUES ({parameterNames});";

					foreach (var fields in source)
					{
						foreach (var field in fields)
						{
							allParameters[field.Key].Value = field.Value;
						}

						com.ExecuteNonQuery();

						foreach (var param in allParameters.Values)
						{
							param.Value = null;
						}
					}

					transaction.Commit();
				}
			}
		}

		//protected void InsertRecord(string table, Dictionary<string, string> fields, IDbCommand com)
		//{
		//	var keys = new List<string>();
		//	var values = new List<string>();
		//	int i = 0;

		//	com.Parameters.Clear();
		//	foreach (var field in fields)
		//	{
		//		var parameter = com.CreateParameter();
		//		parameter.ParameterName = "@p" + i;
		//		parameter.Value = field.Value;
		//		com.Parameters.Add(parameter);
		//		values.Add($"@p" + i);
		//		keys.Add($"[{field.Key}]");
		//		i++;
		//	}

		//	com.CommandText = $"INSERT INTO [{table}] ({string.Join(", ", keys)}) VALUES ({string.Join(", ", values)});";
		//	com.ExecuteNonQuery();
		//}

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

		protected T QueryScalar<T>(string sql)
		{
			using (var com = Connection.CreateCommand())
			{
				com.CommandText = sql;
				var scalar = com.ExecuteScalar();
				return (T)scalar;
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

		public void Dispose()
		{
			Connection.Dispose();
		}
	}
}
