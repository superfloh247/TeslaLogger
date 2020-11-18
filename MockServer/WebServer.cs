using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using MySqlConnector;

namespace MockServer
{
    public class WebServer
    {
        public WebServer()
        {
        }

        internal static void ListJSONDumps(HttpListenerRequest request, HttpListenerResponse response)
        {
            if (request is null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            if (response is null)
            {
                throw new ArgumentNullException(nameof(response));
            }

            if (Directory.Exists("JSON"))
            {
                response.AddHeader("Content-Type", "text/html; charset=utf-8");
                string html1 = "<html><head></head><body><table border=\"1\">";
                string html2 = "</table></body></html>";
                StringBuilder sb = new StringBuilder();
                foreach (DirectoryInfo dir in new DirectoryInfo("JSON").EnumerateDirectories())
                {
                    Tools.Log($"dir: {dir.Name} files: {dir.GetFiles().Length}");
                    sb.Append(string.Format("<tr><td>Name: {0}</td><td>Count: {1}</td><td>{2}</td><td>{3}</td></tr>",
                        dir.Name,
                        dir.GetFiles().Length,
                        GetDirectorySummary(dir),
                        $"<form action=\"/mockserver/import/{dir.Name}\" method=\"get\"><button type=\"submit\">IMPORT</button></form>"));
                }
                WriteString(response, html1 + sb.ToString() + html2);
                return;
            }
            WriteString(response, "");
        }

        private static string GetDirectorySummary(DirectoryInfo dir)
        {
            FileInfo[] files = dir.GetFiles();
            if (files.Length > 1)
            {
                files = files.OrderBy(f => f.Name).ToArray();
                FileInfo first = files.First();
                FileInfo last = files.Last();
                return string.Format("Start: {0} End: {1}",
                    Tools.ConvertFromFileTimestamp(Tools.ExtractTimestampFromJSONFileName(first)),
                    Tools.ConvertFromFileTimestamp(Tools.ExtractTimestampFromJSONFileName(last)));
            }
            return string.Empty;
        }

        internal static void WriteString(HttpListenerResponse response, string responseString)
        {
            response.ContentEncoding = System.Text.Encoding.UTF8;
            byte[] buffer = System.Text.Encoding.UTF8.GetBytes(responseString);
            // Get a response stream and write the response to it.
            response.ContentLength64 = buffer.Length;
            Stream output = response.OutputStream;
            output.Write(buffer, 0, buffer.Length);
            // You must close the output stream.
            output.Close();
        }

        internal static void ListSessions(HttpListenerRequest request, HttpListenerResponse response)
        {
            response.AddHeader("Content-Type", "text/html; charset=utf-8");
            string html1 = "<html><head></head><body><table border=\"1\">";
            string html2 = "</table></body></html>";
            StringBuilder sb = new StringBuilder();
            foreach (MSSession session in MSSession.Sessions)
            {
                sb.Append($"<tr><td>{session.Email}</td><td>{session.Token}</td><td>{session.Sessionid}</td></tr>");
            }
            WriteString(response, html1 + sb.ToString() + html2);
            return;
        }

        internal static void ListImports(HttpListenerRequest request, HttpListenerResponse response)
        {
            response.AddHeader("Content-Type", "text/html; charset=utf-8");
            string html1 = "<html><head></head><body><table border=\"1\">";
            string html2 = "</table></body></html>";
            StringBuilder sb = new StringBuilder();
            try
            {
                using (MySqlConnection conn = new MySqlConnection(Database.DBConnectionstring))
                {
                    conn.Open();
                    using (MySqlCommand cmd = new MySqlCommand($"SELECT ms_sessionid, ms_sessionname FROM ms_sessions", conn))
                    {
                        Tools.Log(cmd);
                        using (MySqlDataReader dr = cmd.ExecuteReader())
                        {
                            if (dr.Read())
                            {
                                    sb.Append($"<tr><td>{dr[0]}</td><td>{dr[1]}</td></tr>");
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Tools.Log("Exception", ex);
            }
            WriteString(response, html1 + sb.ToString() + html2);
            return;
        }
    }
}
