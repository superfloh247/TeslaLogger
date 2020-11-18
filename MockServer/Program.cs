using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;

namespace MockServer
{
    internal class Program
    {
        public static void Main(string[] args)
        {
            Tools.Log("Starting");
            Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("en-US");

            InitDBSchema();

            try
            {
                Thread APIServerThread = new Thread(() =>
                {
                    APIServer apiServer = new APIServer();
                })
                {
                    Name = "APIServerThread"
                };
                APIServerThread.Start();
            }
            catch (Exception ex)
            {
                Tools.Log("Exception", ex);
            }

            // DEBUG - start import w/o webrequest
            //Importer.importFromDirectory("drive-park");
            //Database.ImportJSONFile(new FileInfo("/Users/lindner/VSCode/TeslaLogger/MockServer/bin/Debug/JSON/drive-park/20201103081858917_vehicles_1.json"), 1604391538917, 1);
        }

        private static void InitDBSchema()
        {
            string tablename = "ms_sessions";
            if (!DBTools.TableExists(tablename).Result)
            {
                _ = DBTools.CreateTableWithID(tablename).Result;
            }
            if (!DBTools.ColumnExists(tablename, "ms_sessionid").Result)
            {
                _ = DBTools.CreateColumn(tablename, "ms_sessionid", "INT", false);
            }
            if (!DBTools.ColumnExists(tablename, "ms_sessionname").Result)
            {
                _ = DBTools.CreateColumn(tablename, "ms_sessionname", "TEXT", false);
            }
            tablename = "ms_cars";
            if (!DBTools.TableExists(tablename).Result)
            {
                _ = DBTools.CreateTableWithID(tablename).Result;
            }
            if (!DBTools.ColumnExists(tablename, "ms_email").Result)
            {
                _ = DBTools.CreateColumn(tablename, "ms_email", "TEXT", false);
            }
            if (!DBTools.ColumnExists(tablename, "ms_sessionid").Result)
            {
                _ = DBTools.CreateColumn(tablename, "ms_sessionid", "INT", false);
            }
        }
    }
}
