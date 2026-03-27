using Exceptionless;
using MySql.Data.MySqlClient;
using System;
using System.Data;
using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;

#pragma warning disable CS8600 // Converting null literal or possible null value to non-nullable type
#pragma warning disable CS8601 // Possible null reference assignment
#pragma warning disable CS8602 // Dereference of possibly null reference
#pragma warning disable CS8603 // Possible null reference return
#pragma warning disable CS8604 // Possible null reference argument
#pragma warning disable CS8625 // Cannot convert null literal to non-nullable type

#nullable enable

namespace TeslaLogger
{
    [SuppressMessage("Globalization", "CA1303:Literale nicht als lokalisierte Parameter übergeben", Justification = "<Pending>")]
    [SuppressMessage("Design", "CA1031:Keine allgemeinen Ausnahmetypen abfangen", Justification = "<Pending>")]
    public partial class DBHelper
    {
        /// <summary>
        /// Gets all vehicle/car records sorted by token expiration age.
        /// </summary>
        /// <param name="descending">If true, sorts by token age descending; ascending otherwise</param>
        /// <returns>DataTable containing all car records ordered by tesla_token_expire</returns>
        public static DataTable GetCarsByTokenAge(bool descending=false)
        {
            return GetCars($"tesla_token_expire {(descending?"DESC":"ASC")}");
        }

        /// <summary>
        /// Gets all vehicle/car records from the database.
        /// </summary>
        /// <returns>DataTable containing all cars sorted by ID</returns>
        public static DataTable GetCars()
        {
            return GetCars("id");
        }
        
        /// <summary>
        /// Gets all vehicle/car records ordered by the specified column.
        /// Provides SQL injection protection via regex validation on orderByCol.
        /// </summary>
        /// <param name="orderByCol">Column name to order by (validated for word characters only)</param>
        /// <returns>DataTable containing all cars, or empty table if error</returns>
        private static DataTable? GetCars(string? orderByCol)
        {
            DataTable dt = new DataTable();

            // defense against SQLInjection
            if(!Regex.IsMatch(orderByCol, @"^\w+$"))
            {
                orderByCol = "id";
            }

            try
            {
                using (MySqlDataAdapter da = new MySqlDataAdapter($@"
SELECT
    *
FROM
    cars
ORDER BY
    {orderByCol}", DBConnectionstring))
                {
                    _ = SQLTracer.TraceDA(dt, da);
                }
            }
            catch (Exception ex)
            {
                ex.ToExceptionless().FirstCarUserID().Submit();
                Logfile.Log(ex.ToString());
            }

            return dt;
        }

        /// <summary>
        /// Gets a specific vehicle/car record as a DataTable by car ID.
        /// </summary>
        /// <param name="id">The car's database ID</param>
        /// <returns>DataTable with single car record, or empty table if not found</returns>
        public static DataTable GetCarDT(int id)
        {
            DataTable dt = new DataTable();

            try
            {
                using (MySqlDataAdapter da = new MySqlDataAdapter($@"
                    SELECT
                        *
                    FROM
                        cars
                    where id={id}", DBConnectionstring))
                {
                    _ = SQLTracer.TraceDA(dt, da);
                }
            }
            catch (Exception ex)
            {
                ex.ToExceptionless().FirstCarUserID().Submit();
                Logfile.Log(ex.ToString());
            }

            return dt;
        }

        /// <summary>
        /// Gets a specific vehicle/car record as a DataRow by car ID.
        /// Uses parameterized queries for SQL injection protection.
        /// </summary>
        /// <param name="id">The car's database ID</param>
        /// <returns>DataRow with car data, or null if not found</returns>
        public static DataRow GetCar(int id)
        {
            using (DataTable dt = new DataTable())
            {
                try
                {
                    using (MySqlDataAdapter da = new MySqlDataAdapter(@"
SELECT
    *
FROM
    cars
WHERE
    id = @id", DBConnectionstring))
                    {
                        da.SelectCommand.Parameters.AddWithValue("@id", id);
                        _ = SQLTracer.TraceDA(dt, da);

                        if (dt.Rows.Count == 1)
                        {
                            return dt.Rows[0];
                        }
                    }
                }
                catch (Exception ex)
                {
                    ex.ToExceptionless().FirstCarUserID().Submit();
                    Logfile.Log(ex.ToString());
                }
            }

            return null;
        }

        /// <summary>
        /// Gets a specific vehicle/car record as a DataRow by VIN.
        /// Uses parameterized queries for SQL injection protection.
        /// </summary>
        /// <param name="vin">The vehicle identification number</param>
        /// <returns>DataRow with car data, or null if not found</returns>
        public static DataRow? GetCar(string? vin)
        {
            using (DataTable dt = new DataTable())
            {
                try
                {
                    using (MySqlDataAdapter da = new MySqlDataAdapter(@"
SELECT
    *
FROM
    cars
WHERE
    id = @id", DBConnectionstring))
                    {
                        da.SelectCommand.Parameters.AddWithValue("@vin", vin);
                        _ = SQLTracer.TraceDA(dt, da);

                        if (dt.Rows.Count == 1)
                        {
                            return dt.Rows[0];
                        }
                    }
                }
                catch (Exception ex)
                {
                    ex.ToExceptionless().FirstCarUserID().Submit();
                    Logfile.Log(ex.ToString());
                }
            }

            return null;
        }
    }
}
