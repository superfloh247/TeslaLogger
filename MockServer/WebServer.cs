using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;

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
                    Program.Log($"dir: {dir.Name} files: {dir.GetFiles().Length}");
                    sb.Append(string.Format("<tr><td>{0}</td><td>{1}</td><td>{2}</td><td>{3}</td></tr>",
                        dir.Name,
                        dir.GetFiles().Length,
                        GetDirectorySummary(dir),
                        "TODO Import Button"));
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

        private static void WriteString(HttpListenerResponse response, string responseString)
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
    }
}
