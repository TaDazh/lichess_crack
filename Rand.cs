using System;
using System.Text;

namespace lichess_crack
{
    public static class Rand
    {
        private static Random r;
        public const string Base36 = "0123456789abcdefghijklmnopqrstuvwxyz";

        static Rand()
        {
            r = new Random();
        }
        
        public static string NextSri()
        {
            StringBuilder sri = new StringBuilder();
            for (int l = 0; l < 16; l++)
            {
                sri.Append(Base36[r.Next(Base36.Length)]);
            }

            return sri.ToString();
        }
    }
}
