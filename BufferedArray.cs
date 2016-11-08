using System;

namespace lichess_crack
{
    internal class BufferedArray
    {
        private byte[] bufferedArray;

        public byte[] Buffered { get { return bufferedArray; } }

        public int Length { get { return bufferedArray.Length; } }

        public void Add(byte[] array, int length)
        {
            byte[] buffered = new byte[length];
            Buffer.BlockCopy(array, 0, buffered, 0, length);

            Array.Resize(ref bufferedArray, bufferedArray.Length + length);
            Buffer.BlockCopy(buffered, 0, bufferedArray, bufferedArray.Length - length, length);
        }

        public void Delete()
        {
            bufferedArray = new byte[0];
        }

        public BufferedArray()
        {
            bufferedArray = new byte[0];
        }
    }
}
