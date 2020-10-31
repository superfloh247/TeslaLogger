using System;
using MySqlConnector;

namespace MockServer
{
    public class DBTools
    {
        public DBTools()
        {
        }

        internal static bool TableExists(string tablename)
        {
            using (MySqlConnection con = new MySqlConnection(Database.DBConnectionstring))
            {
                con.Open();
                using (MySqlCommand cmd = new MySqlCommand("SELECT * FROM information_schema.tables where table_name = @tablename", con))
                {
                    cmd.Parameters.Add("@tablename", MySqlDbType.VarChar).Value = tablename;
                    using (MySqlDataReader dr = cmd.ExecuteReader())
                    {
                        if (dr.Read())
                        {
                            return true;
                        }
                    }
                }
            }
            return false;
        }
    }
}
