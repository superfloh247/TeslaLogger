using System;
using System.Threading.Tasks;
using MySqlConnector;

namespace MockServer
{
    public class DBTools
    {
        public DBTools()
        {
        }

        internal static async Task<bool> TableExists(string tablename)
        {
            try
            {
                using (MySqlConnection conn = new MySqlConnection(Database.DBConnectionstring))
                {
                    await conn.OpenAsync();
                    using (MySqlCommand cmd = new MySqlCommand(@"
SELECT
  *
FROM
  information_schema.tables
WHERE
  table_name = @tablename", conn))
                    {
                        cmd.Parameters.AddWithValue("@tablename", tablename);
                        Tools.Log(cmd);
                        using (MySqlDataReader reader = await cmd.ExecuteReaderAsync())
                        {
                            if (await reader.ReadAsync())
                            {
                                return true;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Tools.Log("Exception", ex);
            }
            return false;
        }

        internal static async Task<bool> CreateTableWithID(string tablename)
        {
            try
            {
                using (MySqlConnection conn = new MySqlConnection(Database.DBConnectionstring))
                {
                    await conn.OpenAsync();
                    using (MySqlCommand cmd = new MySqlCommand($"CREATE TABLE {tablename} ( id int(11) NOT NULL AUTO_INCREMENT, PRIMARY KEY (id))", conn))
                    {
                        Tools.Log(cmd);
                        using (MySqlDataReader reader = await cmd.ExecuteReaderAsync())
                        {
                            if (await reader.ReadAsync())
                            {
                                return true;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Tools.Log("Exception", ex);
            }
            return false;
        }

        internal static async Task<bool> CreateTableWithIDAndFields(string tablename)
        {
            if (CreateTableWithID(tablename).Result)
            {
                return await CreateColumn(tablename, "fields", "TEXT", false);
            }
            return false;
        }

        internal static async Task<bool> CreateColumn(string tablename, string columnname, string columntype, bool nullable)
        {
            try
            {
                using (MySqlConnection conn = new MySqlConnection(Database.DBConnectionstring))
                {
                    await conn.OpenAsync();
                    using (MySqlCommand cmd = new MySqlCommand($"ALTER TABLE {tablename} ADD {columnname} {columntype} {(nullable ? "" : "NOT")} NULL", conn))
                    {
                        Tools.Log(cmd);
                        using (MySqlDataReader reader = await cmd.ExecuteReaderAsync())
                        {
                            if (await reader.ReadAsync())
                            {
                                return true;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Tools.Log("Exception", ex);
            }
            return false;
        }

        internal static async Task<bool> ColumnExists(string table, string column)
        {
            try {
                using (MySqlConnection conn = new MySqlConnection(Database.DBConnectionstring))
                {
                    await conn.OpenAsync();
                    using (MySqlCommand cmd = new MySqlCommand($"SHOW COLUMNS FROM {table} LIKE '{column}'", conn))
                    {
                        Tools.Log(cmd);
                        using (MySqlDataReader dr = await cmd.ExecuteReaderAsync())
                        {
                            if (dr.Read())
                            {
                                return true;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Tools.Log("Exception", ex);
            }
            return false;
        }

        internal static string TypeToDBType(object obj)
        {
            if (obj == null)
            {
                return "_NULL_";
            }
            switch (Type.GetTypeCode(obj.GetType()))
            {
                case TypeCode.Int32:
                case TypeCode.Int64:
                    return "BIGINT";
                case TypeCode.Decimal:
                    return "DOUBLE";
                case TypeCode.Boolean:
                    return "BOOLEAN";
                case TypeCode.String:
                    return "TEXT";
                case TypeCode.Object:
                    return "_OBJECT_";
                default:
                    Tools.Log($"TypeToDBType unhandled {Type.GetTypeCode(obj.GetType())}");
                    break;
            }
            return string.Empty;
        }

        internal static async Task<int?> GetMaxValue(string tablename, string columnname)
        {
            try
            {
                using (MySqlConnection conn = new MySqlConnection(Database.DBConnectionstring))
                {
                    await conn.OpenAsync();
                    using (MySqlCommand cmd = new MySqlCommand($"SELECT MAX({columnname}) FROM {tablename}", conn))
                    {
                        Tools.Log(cmd);
                        using (MySqlDataReader dr = await cmd.ExecuteReaderAsync())
                        {
                            if (dr.Read())
                            {
                                if (int.TryParse(dr[0].ToString(), out int value))
                                {
                                    return value;
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Tools.Log("Exception", ex);
            }
            return null;
        }


    }
}
