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

        internal static async Task<bool> TableExistsAsync(string tablename)
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

        internal static async Task<bool> ColumnExistsAsync(string table, string column)
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
    }
}
