using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;

namespace lichess_crack
{
    public class Cracker
    {
        public const string Alpha = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";

        private long multiplier;
        private long increment;
        private long inv_mult;
        private long mask;
        private long crackSeed;

        public Cracker(long multiplier, long increment)
        {
            this.multiplier = multiplier;
            this.increment = increment;
            inv_mult = ModInv(multiplier, 1L << 48);
            mask = (1L << 48) - 1;
        }

        private long ModInv(long a, long n)
        {
            long i = n, v = 0, d = 1;
            while (a > 0)
            {
                long t = i / a, x = a;
                a = i % x;
                i = x;
                x = d;
                d = v - t * x;
                v = x;
            }
            v %= n;
            if (v < 0) v = (v + n) % n;
            return v;
        }

        public long CrackGpu(int[] sequence)
        {
            if (sequence.Length != 16)
            {
                return -1;
            }

            List<long> lows = new List<long>();
            long bit17 = sequence[0] & 1;
            for (long low = 0; low < (1L << 17); low++)
            {
                long seed = (bit17 << 17) | low;
                bool isSeed = true;
                for (int n = 0; n < sequence.Length; n++)
                {
                    seed = (seed * multiplier + increment) & mask;
                    if (((seed >> 17) & 1) != (sequence[n] & 1))
                    {
                        isSeed = false;
                        break;
                    }
                }

                if (isSeed)
                {
                    lows.Add(low);
                }
            }

            for (int i = 0; i < lows.Count; i++)
            {
                long seedArray = Gpu.CrackHigh(sequence, lows[i]);
                if (seedArray != 0)
                {
                    return seedArray;
                }
            }

            return -1;
        }

        public long CrackCpu(int[] sequence, int threads)
        {
            if (sequence.Length != 16)
            {
                return -1;
            }

            List<long> lows = new List<long>();
            long bit17 = sequence[0] & 1;
            for (long low = 0; low < (1L << 17); low++)
            {
                long seed = (bit17 << 17) | low;
                bool isSeed = true;
                for (int n = 0; n < sequence.Length; n++)
                {
                    seed = (seed * multiplier + increment) & mask;
                    if (((seed >> 17) & 1) != (sequence[n] & 1))
                    {
                        isSeed = false;
                        break;
                    }
                }

                if (isSeed)
                {
                    lows.Add(low);
                }
            }

            for (int i = 0; i < lows.Count; i++)
            {
                long seedArray = CrackHighCpu(sequence, lows[i], threads);
                if (seedArray != 0)
                {
                    return seedArray;
                }
            }

            return -1;
        }

        private long CrackHighCpu(int[] sequence, long low, int threadCount)
        {
            crackSeed = 0;
            
            Thread[] threads = new Thread[threadCount];
            for (int i = 0; i < threads.Length; i++)
            {
                threads[i] = new Thread(() => CrackThreadImpl(i, threadCount, sequence, low));
                threads[i].Name = "CrackHigh";
                threads[i].Priority = ThreadPriority.Highest;
                threads[i].Start();
            }

            for (int j = 0; j < threads.Length; j++)
            {
                threads[j].Join();
            }

            return crackSeed;
        }

        private void CrackThreadImpl(int idx, int threadCount, int[] sequence, long low)
        {
            if (crackSeed != 0)
            {
                return;
            }

            long idxCount = (1L << 31) / threadCount;
            if (idx >= 0 && idx < threadCount)
            {
                long highStart = idx * idxCount;
                long highEnd = highStart + idxCount;

                for (long high = highStart; high < highEnd; high++)
                {
                    if (crackSeed != 0)
                    {
                        return;
                    }

                    long seed = (high << 17) | low;

                    bool isSeed = true;
                    for (int n = 0; n < sequence.Length; n++)
                    {
                        seed = (seed * multiplier + increment) & mask;
                        int next = (int)((seed >> 17) % 62);

                        if (next != sequence[n])
                        {
                            isSeed = false;
                            break;
                        }
                    }

                    if (isSeed)
                    {
                        //Add current seed (start seed is (high << 17) | low)
                        crackSeed = seed;
                        return;
                    }
                }
            }
        }

        public long RollbackSeed(long seed, int iterations)
        {
            long cur = seed;
            for (int i = 0; i < iterations; i++)
            {
                cur = ((cur - increment) * inv_mult) & mask;
            }

            return cur;
        }

        public void GetPlayerIdsForward(long seed, string gameId, out string white, out string black, int timeout = 10)
        {
            long current = seed;

            int mi = 0;
            char[] match = gameId.ToCharArray();

            bool found = false;
            Stopwatch sw = Stopwatch.StartNew();
            while (sw.Elapsed.TotalSeconds < timeout)
            {
                current = (current * multiplier + increment) & mask;
                int bits = (int)(current >> 17);
                int index = bits % 62;

                char next = Alpha[index];
                if (next == match[mi])
                {
                    mi++;
                    if (mi == match.Length)
                    {
                        found = true;
                        break;
                    }
                }
                else
                {
                    mi = 0;
                }
            }

            if (!found)
            {
                white = null;
                black = null;
                return;
            }

            current = RollbackSeed(current, 16);

            char[] whiteChar = new char[4];
            char[] blackChar = new char[4];

            for (int i = 0; i < 4; i++)
            {
                current = (current * multiplier + increment) & mask;
                int bits = (int)(current >> 17);
                int index = bits % 62;
                whiteChar[i] = Alpha[index];
            }

            for (int i = 0; i < 4; i++)
            {
                current = (current * multiplier + increment) & mask;
                int bits = (int)(current >> 17);
                int index = bits % 62;
                blackChar[i] = Alpha[index];
            }

            white = new string(whiteChar);
            black = new string(blackChar);
        }

        public void GetPlayerIdsBackward(long seed, string gameId, out string white, out string black, int timeout = 10)
        {
            long current = seed;

            int mi = 0;
            char[] match = gameId.ToCharArray();

            bool found = false;
            Stopwatch sw = Stopwatch.StartNew();
            while (sw.Elapsed.TotalSeconds < timeout)
            {
                current = ((current - increment) * inv_mult) & mask;
                int bits = (int)(current >> 17);
                int index = bits % 62;

                char next = Alpha[index];
                if (next == match[match.Length - mi - 1])
                {
                    mi++;
                    if (mi == match.Length)
                    {
                        found = true;
                        break;
                    }
                }
                else
                {
                    mi = 0;
                }
            }

            if (!found)
            {
                white = null;
                black = null;
                return;
            }

            char[] whiteChar = new char[4];
            char[] blackChar = new char[4];

            for (int i = 0; i < 4; i++)
            {
                current = ((current - increment) * inv_mult) & mask;
                int bits = (int)(current >> 17);
                int index = bits % 62;
                blackChar[3 - i] = Alpha[index];
            }

            for (int i = 0; i < 4; i++)
            {
                current = ((current - increment) * inv_mult) & mask;
                int bits = (int)(current >> 17);
                int index = bits % 62;
                whiteChar[3 - i] = Alpha[index];
            }

            white = new string(whiteChar);
            black = new string(blackChar);
        }

        public static int[] ConvertAlphanumeric(string str)
        {
            int[] seq = new int[str.Length];
            for (int i = 0; i < str.Length; i++)
            {
                seq[i] = Alpha.IndexOf(str[i]);
            }

            return seq;
        }
    }
}
