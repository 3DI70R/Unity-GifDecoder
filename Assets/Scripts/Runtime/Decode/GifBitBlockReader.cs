using System.IO;
using ThreeDISevenZeroR.UnityGifDecoder.Utils;

namespace ThreeDISevenZeroR.UnityGifDecoder
{
    public class GifBitBlockReader
    {
        private Stream stream;
        private byte[] buffer;
        private int currentByte;
        private int currentBitPosition;
        private int currentBufferPosition;
        private int currentBufferSize;
        private bool endReached;

        public GifBitBlockReader()
        {
            buffer = new byte[256];
        }

        public GifBitBlockReader(Stream stream) : this()
        {
            SetStream(stream);
        }

        public void SetStream(Stream stream)
        {
            this.stream = stream;
        }

        public void StartNewReading()
        {
            currentByte = 0;
            currentBitPosition = 8;
            ReadNextBlock();
        }

        private void ReadNextBlock()
        {
            currentBufferSize = stream.ReadByte8();
            currentBufferPosition = 0;
            endReached = currentBufferSize == 0;
            
            if(!endReached)
                stream.Read(buffer, 0, currentBufferSize);
        }

        public int ReadBits(int count)
        {
            var result = 0;
            
            for(var i = 0; i < count; i++)
            {
                if (currentBitPosition == 8)
                {
                    currentBitPosition = 0;
                    
                    if (endReached)
                    {
                        // Some gifs can read slightly past end of a stream
                        // (since there is a zero byte afterwards anyway, it is safe to return 0)
                        currentByte = 0;
                    }
                    else
                    {
                        currentByte = buffer[currentBufferPosition++];
                        if (currentBufferPosition == currentBufferSize)
                            ReadNextBlock();
                    }
                }

                result += ((currentByte & (1 << currentBitPosition)) != 0 ? 1 : 0) << i;
                currentBitPosition++;
            }

            return result;
        }
    }
}