using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace lichess_crack
{
    public class StrongSocket : IDisposable
    {
        public delegate void Handler(StrongSocket sock, JObject data);

        public class Event : IDisposable
        {
            private ManualResetEvent handle;
            public string Message { get; private set; }
            public JObject Data { get; set; }

            public void Dispose()
            {
                if (handle != null)
                {
                    handle.Dispose();
                    handle = null;
                }
            }

            public bool Wait(int timeout)
            {
                if (handle != null)
                {
                    return handle.WaitOne(timeout);
                }

                return false;
            }

            public void Release(JObject data)
            {
                if (handle != null)
                {
                    handle.Set();
                }

                Data = data;
            }

            public Event(string message)
            {
                Message = message;
                handle = new ManualResetEvent(false);
            }
        }

        private bool disconnectHandled;
        private object sendLock;

        private DateTime? lastPingTime;
        private ClientWebSocket ws;
        private CancellationTokenSource recvTokenSource;

        private List<byte[]> ackableMessages;
        private Dictionary<string, string> queryParams;
        private List<Event> events;

        public int Version { get; set; }

        public string Sri { get; set; }

        public string Url { get; set; }

        public double Lag { get; private set; }

        public CookieCollection Cookies
        {
            get
            {
                if (ws == null || ws.Options.Cookies == null)
                {
                    return null;
                }

                Uri lichessUrl = GetUrl();
                return ws.Options.Cookies.GetCookies(lichessUrl);
            }
            set
            {
                if (ws == null)
                {
                    return;
                }

                if (ws.Options.Cookies == null)
                {
                    ws.Options.Cookies = new CookieContainer();
                }

                ws.Options.Cookies.Add(value);
            }
        }

        public bool IsConnected()
        {
            if (ws == null)
            {
                return false;
            }

            return ws.State == WebSocketState.Connecting || ws.State == WebSocketState.Open;
        }

        public string GetBaseUrl()
        {
            return string.Format("socket.{0}.lichess.org", CultureInfo.CurrentCulture.TwoLetterISOLanguageName);
        }

        public Uri GetUrl()
        {
            return new Uri(string.Format("https://{0}.lichess.org", CultureInfo.CurrentCulture.TwoLetterISOLanguageName));
        }

        public Uri GetConnectionUrl()
        {
            return new Uri(string.Format("ws://{0}{1}?{2}", GetBaseUrl(), Url, Http.ToQuery(queryParams)));
        }

        private async void ReceiveLoop()
        {
            disconnectHandled = false;
            recvTokenSource = new CancellationTokenSource();

            byte[] buffer = new byte[1024];
            ArraySegment<byte> bufferSegment = new ArraySegment<byte>(buffer);
            BufferedArray bufferedArray = new BufferedArray();

            try
            {
                while (IsConnected())
                {
                    WebSocketReceiveResult recvResult = await ws.ReceiveAsync(bufferSegment, recvTokenSource.Token);
                    if (recvResult != null && recvResult.MessageType == WebSocketMessageType.Text)
                    {
                        bufferedArray.Add(buffer, recvResult.Count);

                        if (recvResult.EndOfMessage)
                        {
                            await HandleMessage(Encoding.UTF8.GetString(bufferedArray.Buffered, 0, bufferedArray.Length));
                            bufferedArray.Delete();
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
                disconnectHandled = true;
            }
            catch (WebSocketException wse)
            {
                if (!disconnectHandled)
                {
                    WebSocketError code = wse.WebSocketErrorCode;
                    if (code != WebSocketError.Success)
                    {
                        string disconnectReason = Enum.GetName(typeof(WebSocketError), code);
                        Debug.WriteLine(string.Format("Disconnected: {0}", disconnectReason));
                    }

                    disconnectHandled = true;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine(string.Format("Error: {0}\nStacktrace:\n{1}", ex.Message, ex.StackTrace));
                disconnectHandled = true;
            }

            if (IsConnected())
            {
                Disconnect();
            }

            if (recvTokenSource != null)
            {
                recvTokenSource.Dispose();
                recvTokenSource = null;
            }
        }

        private async Task HandleMessage(string data)
        {
            bool pingServer = false;
            string mtype = null;

            int nextVersion = Version;
            JObject jsonObject = (JObject)JsonConvert.DeserializeObject(data);
            foreach (JProperty prop in jsonObject.Properties())
            {
                if (prop.Value is JArray)
                {
                    JArray array = prop.Value as JArray;
                    for (int i = 0; i < array.Count; i++)
                    {
                        if (array[i] is JObject)
                        {
                            JObject item = (JObject)array[i];
                            foreach (JProperty itemProp in item.Properties())
                            {
                                switch (itemProp.Name)
                                {
                                    case "v":
                                        nextVersion = Math.Max(Version, (int)itemProp.Value);
                                        break;
                                }
                            }
                        }
                    }
                }

                switch (prop.Name)
                {
                    case "v":
                        nextVersion = Math.Max(Version, (int)prop.Value);
                        break;
                    case "t":
                        {
                            mtype = (string)prop.Value;
                            if (string.IsNullOrEmpty(mtype))
                            {
                                break;
                            }

                            switch (mtype)
                            {
                                case "n":
                                    {
                                        if (lastPingTime.HasValue)
                                        {
                                            TimeSpan span = DateTime.Now.Subtract(lastPingTime.Value);
                                            Lag = span.TotalMilliseconds;
                                        }

                                        pingServer = true;
                                    }
                                    break;
                            }

                            for (int i = events.Count - 1; i >= 0; i--)
                            {
                                if (string.Compare(events[i].Message, mtype, false) == 0)
                                {
                                    events[i].Release(jsonObject);
                                    events.RemoveAt(i);
                                }
                            }
                        }
                        break;
                }
            }

            Version = Math.Max(Version, nextVersion);
            if (pingServer)
            {
                await Task.Delay(1000);
                await PingServer();
            }
        }

        private async Task<bool> Send(byte[] data)
        {
            Task sendTask = null;
            await Task.Run(delegate ()
            {
                try
                {
                    ArraySegment<byte> bufferSegment = new ArraySegment<byte>(data);
                    lock (sendLock)
                    {
                        Task.WaitAll(sendTask = ws.SendAsync(bufferSegment, WebSocketMessageType.Text, true, CancellationToken.None));
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine("Send Failed: " + ex.Message);
                }
            });

            return sendTask != null && sendTask.Status == TaskStatus.RanToCompletion;
        }

        private async Task<bool> Send(string data)
        {
            return await Send(Encoding.UTF8.GetBytes(data));
        }

        public async Task<bool> Send(string type, JToken data = null)
        {
            if (!IsConnected())
            {
                return false;
            }

            JObject obj = new JObject();
            obj["t"] = type;

            if (data != null)
            {
                obj["d"] = data;
            }

            string serializedObj = JsonConvert.SerializeObject(obj);
            return await Send(serializedObj);
        }

        public async Task<bool> PingServer()
        {
            if (!IsConnected())
            {
                return false;
            }

            JObject obj = new JObject();
            obj["t"] = "p";
            obj["v"] = Version;

            string serializedObj = JsonConvert.SerializeObject(obj);

            bool success = await Send(serializedObj);
            lastPingTime = DateTime.Now;
            return success;
        }

        public async Task<bool> Connect(CookieCollection cookies = null)
        {
            if (IsConnected())
            {
                Disconnect();
            }

            ws = new ClientWebSocket();
            if (cookies != null)
            {
                ws.Options.Cookies = new CookieContainer();

                try
                {
                    ws.Options.Cookies.Add(cookies);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine(string.Format("Error: {0}\nStacktrace:\n{1}", ex.Message, ex.StackTrace));
                }
            }

            Uri conUrl = GetConnectionUrl();

            try
            {
                await ws.ConnectAsync(conUrl, CancellationToken.None);
            }
            catch (Exception ex)
            {
                Debug.WriteLine(string.Format("Error: {0}\nStacktrace:\n{1}", ex.Message, ex.StackTrace));
                return false;
            }

            if (ws.State != WebSocketState.Connecting &&
                ws.State != WebSocketState.Open)
            {
                return false;
            }

            await PingServer();

            Thread receiveThread = new Thread(ReceiveLoop);
            receiveThread.Name = "StrongSocket";
            receiveThread.IsBackground = false;
            receiveThread.Priority = ThreadPriority.AboveNormal;
            receiveThread.Start();

            return true;
        }

        public Event Listen(string mtype)
        {
            Event evt = new Event(mtype);
            events.Add(evt);
            return evt;
        }

        public JObject On(Event evt)
        {
            evt.Wait(2500);
            evt.Dispose();
            return evt.Data;
        }

        public void Disconnect()
        {
            if (IsConnected())
            {
                if (recvTokenSource != null)
                {
                    recvTokenSource.Cancel(false);
                }

                ws.Abort();
            }
        }

        public void Dispose()
        {
            if (IsConnected())
            {
                Disconnect();
            }

            if (ws != null)
            {
                ws.Dispose();
                ws = null;
            }
        }

        public StrongSocket(string url, int version, Dictionary<string, string> queryParams)
        {
            Url = url;
            Version = version;
            Sri = Rand.NextSri();

            ackableMessages = new List<byte[]>();
            if (queryParams == null)
            {
                queryParams = new Dictionary<string, string>();
            }

            if (!queryParams.ContainsKey("sri"))
            {
                queryParams.Add("sri", Sri);
            }

            this.queryParams = queryParams;
            events = new List<Event>();
            sendLock = new object();
        }
    }
}
