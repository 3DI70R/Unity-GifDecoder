﻿using System;
 
namespace ThreeDISevenZeroR.UnityGifDecoder.Decode
{
    /// <summary>
    /// LZW Dictionary used to decode bit stream
    /// </summary>
    public class GifLzwDictionary
    {
        /// <summary>
        /// Current entry code size in bits
        /// </summary>
        public int CodeSize { get; private set; }
        
        private readonly Entry[] dictionaryEntries;
        private byte[] dictionaryHeap;
        private int dictionarySize;
        private int dictionaryHeapPosition;
        
        private int initialDictionarySize;
        private int initialLzwCodeSize;
        private int initialDictionaryHeapPosition;
        private int nextLzwCodeGrowth;
        private int currentMinLzwCodeSize;

        private int clearCodeId;
        private int stopCodeId;
        private bool isFull;
        
        /// <summary>
        /// Creates new instance and allocates dictionary resources
        /// </summary>
        public GifLzwDictionary()
        {
            dictionaryEntries = new Entry[4096];
            dictionaryHeap = new byte[16384];
        }

        /// <summary>
        /// Initializes dictionary with minimum code size
        /// </summary>
        /// <param name="minLzwCodeSize">new minimum lzw code size</param>
        public void InitWithWordSize(int minLzwCodeSize)
        {
            if (currentMinLzwCodeSize != minLzwCodeSize)
            {
                currentMinLzwCodeSize = minLzwCodeSize;
                dictionaryHeapPosition = 0;
                dictionarySize = 0;
            
                var colorCount = 1 << minLzwCodeSize;

                for (var i = 0; i < colorCount; i++)
                {
                    dictionaryEntries[i] = new Entry { heapPosition = dictionaryHeapPosition, size = 1 };
                    dictionaryHeap[dictionaryHeapPosition++] = (byte) i;
                }
            
                initialDictionarySize = colorCount + 2;
                initialLzwCodeSize = minLzwCodeSize + 1;
                initialDictionaryHeapPosition = dictionaryHeapPosition;

                clearCodeId = colorCount;
                stopCodeId = colorCount + 1;
            }

            Clear();
        }

        /// <summary>
        /// Clear dictionary contents
        /// </summary>
        public void Clear()
        {
            CodeSize = initialLzwCodeSize;
            dictionarySize = initialDictionarySize;
            dictionaryHeapPosition = initialDictionaryHeapPosition;
            nextLzwCodeGrowth = 1 << CodeSize;
            isFull = false;
        }

        /// <summary>
        /// Is specified entry exists in dictionary
        /// </summary>
        public bool Contains(int code) => code < dictionarySize;
        
        /// <summary>
        /// Is this code is dictionary clear code?
        /// </summary>
        public bool IsClearCode(int code) => code == clearCodeId;
        
        /// <summary>
        /// Is this code is stop code?
        /// </summary>
        public bool IsStopCode(int code) => code == stopCodeId;

        /// <summary>
        /// Output dictionary entry to canvas
        /// </summary>
        public void OutputCode(int entry, GifCanvas c)
        {
            if (entry < initialDictionarySize)
            {
                c.OutputPixel(entry);
            }
            else
            {
                var e = dictionaryEntries[entry];
                var heapEnd = e.heapPosition + e.size;
                for (var i = e.heapPosition; i < heapEnd; i++)
                    c.OutputPixel(dictionaryHeap[i]);
            }
        }

        /// <summary>
        /// Create new dictionary entry from base entry
        /// </summary>
        public int CreateNewCode(int baseEntry, int deriveEntry)
        {
            if (isFull)
                return -1;

            var entry = dictionaryEntries[baseEntry];
            var newEntry = new Entry {heapPosition = dictionaryHeapPosition, size = entry.size + 1};

            EnsureHeapCapacity(dictionaryHeapPosition + newEntry.size);

            if (entry.size < 4)
            {
                // It is faster to just copy array manually for small values
                var endValue = entry.heapPosition + entry.size;
                for (var i = entry.heapPosition; i < endValue; i++)
                    dictionaryHeap[dictionaryHeapPosition++] = dictionaryHeap[i];
                
            }
            else
            {
                Buffer.BlockCopy(dictionaryHeap, entry.heapPosition,
                    dictionaryHeap, dictionaryHeapPosition, entry.size);
                dictionaryHeapPosition += entry.size;
            }
            
            dictionaryHeap[dictionaryHeapPosition++] = GetFirstCode(deriveEntry);

            var insertPosition = dictionarySize++;
            dictionaryEntries[insertPosition] = newEntry;

            if (dictionarySize >= nextLzwCodeGrowth)
            {
                CodeSize++;
                nextLzwCodeGrowth = CodeSize == 12 ? int.MaxValue : 1 << CodeSize;
            }

            // Dictionary is capped at 4096 elements
            if (dictionarySize >= 4096)
                isFull = true;

            return insertPosition;
        }

        private byte GetFirstCode(int entry)
        {
            if (entry < initialDictionarySize)
                return (byte) entry;
            
            return dictionaryHeap[dictionaryEntries[entry].heapPosition];
        }

        private void EnsureHeapCapacity(int size)
        {
            if (dictionaryHeap.Length < size)
                Array.Resize(ref dictionaryHeap, Math.Max(dictionaryHeap.Length * 2, size));
        }

        private struct Entry
        {
            public int heapPosition;
            public int size;
        }
    }
}