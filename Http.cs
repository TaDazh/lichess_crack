using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text;

namespace lichess_crack
{
    internal class Http
    {
        public enum Accept
        {
            Html,
            Json,
            None
        }

        public static string ToQuery(Dictionary<string, string> parameters)
        {
            StringBuilder query = new StringBuilder();
            foreach (KeyValuePair<string, string> pair in parameters)
            {
                if (query.Length != 0)
                {
                    query.Append('&');
                }

                query.Append(pair.Key);
                query.Append('=');
                query.Append(pair.Value);
            }

            return query.ToString();
        }

        public static HttpWebResponse Get(string url, string accept, string encoding, CookieCollection cookies = null)
        {
            if (!url.StartsWith("http://"))
            {
                url = url.Insert(0, "http://");
            }

            Uri uri = new Uri(url);

            HttpWebRequest wreq = (HttpWebRequest)WebRequest.Create(uri);
            wreq.Headers["X-Requested-With"] = "XMLHttpRequest";
            wreq.Accept = accept;
            wreq.Headers["Origin"] = uri.Host;
            wreq.Headers["Accept-Encoding"] = encoding;
            wreq.Headers["Accept-Language"] = string.Format("{0},{1};q=0.8", CultureInfo.CurrentCulture.IetfLanguageTag, CultureInfo.CurrentCulture.TwoLetterISOLanguageName);
            wreq.UserAgent = "Mozilla/5.0 (Windows NT 6.1) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/41.0.2228.0 Safari/537.36";
            wreq.Headers["Cache-Control"] = "max-age=0";
            wreq.Method = "GET";
            wreq.Timeout = 5000;
            wreq.AllowAutoRedirect = false;

            wreq.CookieContainer = new CookieContainer();
            if (cookies != null)
            {
                wreq.CookieContainer.Add(cookies);
            }

            try
            {
                return (HttpWebResponse)(wreq.GetResponse());
            }
            catch (Exception ex)
            {
                Debug.WriteLine(string.Format("Error: {0}, Stacktrace = {1}", ex.Message, ex.StackTrace));
                return null;
            }
        }

        public static HttpWebResponse Get(string url, Accept accept, Accept encoding, CookieCollection cookies = null, bool allowRedirect = false)
        {
            if (!url.StartsWith("http://"))
            {
                url = url.Insert(0, "http://");
            }

            Uri uri = new Uri(url);

            HttpWebRequest wreq = (HttpWebRequest)WebRequest.Create(uri);
            wreq.Headers["X-Requested-With"] = "XMLHttpRequest";
            wreq.Accept = accept == Accept.Html ? "text/html,application/xhtml+xml,application/xml;q=0.9,image/webp,*/*;q=0.8" : "application/vnd.lichess.v1+json";
            wreq.Headers["Origin"] = uri.Host;
            if (encoding == Accept.Json)
            {
                wreq.Headers["Accept-Encoding"] = "gzip, deflate";
            }
            else if (encoding == Accept.Html)
            {
                wreq.Headers["Accept-Encoding"] = "html";
            }
            wreq.Headers["Accept-Language"] = string.Format("{0},{1};q=0.8", CultureInfo.CurrentCulture.IetfLanguageTag, CultureInfo.CurrentCulture.TwoLetterISOLanguageName);
            wreq.UserAgent = "Mozilla/5.0 (Windows NT 6.1) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/41.0.2228.0 Safari/537.36";
            wreq.Headers["Cache-Control"] = "max-age=0";
            wreq.Method = "GET";
            wreq.AllowAutoRedirect = allowRedirect;
            wreq.AutomaticDecompression = (encoding == Accept.Json) ? DecompressionMethods.GZip : DecompressionMethods.None;

            wreq.CookieContainer = new CookieContainer();
            if (cookies != null)
            {
                wreq.CookieContainer.Add(cookies);
            }

            try
            {
                return (HttpWebResponse)(wreq.GetResponse());
            }
            catch (Exception ex)
            {
                Debug.WriteLine(string.Format("Error: {0}\nStacktrace:\n{1}", ex.Message, ex.StackTrace));
                return null;
            }
        }

        public static HttpWebResponse Post(string url, byte[] data, Accept accept, CookieCollection cookies = null)
        {
            Uri uri = new Uri(url);

            if (cookies == null)
            {
                //Fetch the basic site cookies
                HttpWebResponse response = Get(uri.Host, Accept.Html, Accept.Html);

                cookies = response.Cookies;
                response.Dispose();
            }

            HttpWebRequest wreq = (HttpWebRequest)WebRequest.Create(uri);
            wreq.Headers["X-Requested-With"] = "XMLHttpRequest";
            switch (accept)
            {
                case Accept.Html:
                    wreq.Accept = "text/html,application/xhtml+xml,application/xml;q=0.9,image/webp,*/*;q=0.8";
                    break;
                case Accept.Json:
                    wreq.Accept = "application/vnd.lichess.v1+json";
                    break;
                case Accept.None:
                    wreq.Accept = "*/*";
                    break;
            }
            wreq.Headers["Origin"] = uri.Host;
            wreq.Headers["Accept-Encoding"] = "gzip, deflate";
            wreq.Headers["Accept-Language"] = string.Format("{0},{1};q=0.8", CultureInfo.CurrentCulture.IetfLanguageTag, CultureInfo.CurrentCulture.TwoLetterISOLanguageName);
            wreq.UserAgent = "Mozilla/5.0 (Windows NT 6.1) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/41.0.2228.0 Safari/537.36";
            wreq.Headers["Cache-Control"] = "max-age=0";
            wreq.ContentType = "application/x-www-form-urlencoded";
            wreq.Method = "POST";
            wreq.ContentLength = data.Length;
            wreq.AllowAutoRedirect = false;

            wreq.CookieContainer = new CookieContainer();
            if (cookies != null)
            {
                wreq.CookieContainer.Add(cookies);
            }

            using (Stream stream = wreq.GetRequestStream())
            {
                stream.Write(data, 0, data.Length);
            }

            try
            {
                return (HttpWebResponse)(wreq.GetResponse());
            }
            catch (Exception ex)
            {
                Debug.WriteLine(string.Format("Error: {0}\nStacktrace:\n{1}", ex.Message, ex.StackTrace));
                return null;
            }
        }

        public static HttpWebResponse Post(string url, string data, Accept accept, CookieCollection cookies = null)
        {
            return Post(url, Encoding.UTF8.GetBytes(data), accept, cookies);
        }
    }
}
