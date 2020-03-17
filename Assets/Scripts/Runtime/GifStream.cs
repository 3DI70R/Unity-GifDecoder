using System;
using System.IO;
using System.Text;
using ThreeDISevenZeroR.UnityGifDecoder.Decode;
using ThreeDISevenZeroR.UnityGifDecoder.Utils;
using UnityEngine;

namespace ThreeDISevenZeroR.UnityGifDecoder
{
    /// <summary>
    /// Main class for gif decoding
    ///
    /// Reads and decodes gif file sequentially in PullParser manner <br/>
    /// This class can be called from any thread but one at a time, there is no thead safety mechanism<br/>
    /// <br/>
    /// Example usage:<br/>
    /// <code>
    /// var gifStream = new GifStream("yourData");
    /// while (true)
    /// {
    ///     switch (gifStream.NextToken())
    ///     {
    ///         case GifStream.Token.Image:
    ///             var img = gifStream.ReadImage();
    ///             // do something with image
    ///             break;
    /// 
    ///         case GifStream.Token.EndOfFile:
    ///             return;
    ///            
    ///         default:
    ///             gifStream.Skip();
    ///             break;
    ///     }
    /// }
    /// </code>
    /// </summary>
    public class GifStream : IDisposable
    {
        /// <summary>
        /// See: <see cref="GifCanvas.FlipVertically" />
        /// </summary>
        public bool FlipVertically
        {
            get => canvas.FlipVertically;
            set => canvas.FlipVertically = value;
        }

        /// <summary>
        /// Width of currently loaded gif file
        /// </summary>
        public int Width { get; private set; }

        /// <summary>
        /// Height of currently loaded gif file
        /// </summary>
        public int Height { get; private set; }

        /// <summary>
        /// File version of currenly loaded file
        /// </summary>
        public FileVersion Version { get; private set; }

        /// <summary>
        /// Underlying stream which is used for gif data loading
        /// </summary>
        public Stream BaseStream
        {
            get => currentStream;
            set => SetStream(value);
        }

        /// <summary>
        /// Is end of a stream reached and there will be no new gif data
        /// Now you can close this stream or call Reset() and read everything again
        /// </summary>
        public bool IsEOFReached => currentToken == Token.EndOfFile;

        private Stream currentStream;
        private long dataStartPosition;
        private Token currentToken;
        private int currentFrame;

        private GifCanvas canvas;
        private GifLzwDictionary lzwDictionary;
        private GifBitBlockReader blockReader;

        private Color32[] globalColorTable;
        private Color32[] localColorTable;
        private readonly byte[] headerBuffer;
        private readonly byte[] colorTableBuffer;
        private GraphicControl graphicControl;

        /// <summary>
        /// Creates GifStream instance without Stream and preallocates resources for gif decoding
        /// </summary>
        public GifStream()
        {
            lzwDictionary = new GifLzwDictionary();
            canvas = new GifCanvas();
            blockReader = new GifBitBlockReader();
            
            globalColorTable = new Color32[256]; 
            localColorTable = new Color32[256]; 
            headerBuffer = new byte[6]; 
            colorTableBuffer = new byte[768];
        }

        /// <summary>
        /// <p>Convenience constructor</p>
        /// <p>Invokes original constructor and sets stream to read from</p>
        /// </summary>
        /// <param name="stream">Stream to read gif from</param>
        public GifStream(Stream stream) : this()
        {
            SetStream(stream);
        }

        /// <summary>
        /// <p>Convenience constructor</p>
        /// <p>Invokes original constructor and sets MemoryStream with specified bytes</p>
        /// </summary>
        /// <param name="gifBytes">bytes of gif file</param>
        public GifStream(byte[] gifBytes) : this(new MemoryStream(gifBytes)) { }

        /// <summary>
        /// <p>Convenience constructor</p>
        /// <p>Invokes original constructor and open stream from file path</p>
        /// <b>Don't forget to call Dispose() to close file</b>
        /// </summary>
        /// <param name="path">Path to gif file</param>
        public GifStream(string path) : this(File.OpenRead(path)) { }

        /// <summary>
        /// Sets new stream to read gif data from<br/>
        ///
        /// Gif header will be automatically read from this stream, so 
        ///
        /// GifStream is reusable, you can change stream and read new gif from it. That way there will be no 
        /// </summary>
        /// <param name="stream">new stream with gif data</param>
        /// <param name="disposePrevious">Dispose previous stream</param>
        public void SetStream(Stream stream, bool disposePrevious = false)
        {
            if (disposePrevious)
                currentStream?.Dispose();

            currentStream = stream;
            blockReader.SetStream(stream);

            ReadHeader();
        }

        /// <summary>
        /// Disposes underlying Stream
        /// </summary>
        public void Dispose()
        {
            currentStream?.Dispose();
        }

        /// <summary>
        /// Reads next portion of Gif file and return encountered token type<br/>
        /// </summary>
        /// <returns>Encountered token type</returns>
        /// <exception cref="InvalidOperationException">If current token is not <code>Token.Unknown</code></exception>
        public Token NextToken()
        {
            AssertToken(Token.Unknown);

            while (true)
            {
                var blockType = currentStream.ReadByte8();
                switch (blockType)
                {
                    case extensionBlock:

                        var extensionType = currentStream.ReadByte8();
                        switch (extensionType)
                        {
                            case commentLabel:
                                return SetCurrentToken(Token.Comment);

                            case graphicControlLabel:
                                ReadGraphicControlExtension();
                                break;

                            default:
                                BitUtils.SkipGifBlocks(currentStream);
                                break;
                        }

                        break;

                    case imageDescriptorBlock:
                        return SetCurrentToken(Token.Image);

                    case endOfFile:
                        return SetCurrentToken(Token.EndOfFile);

                    default:
                        throw new ArgumentException($"Unknown block type {blockType}");
                }
            }
        }

        /// <summary>
        /// Skip current token reading (Token.Comment or Token.Image)<br/>
        /// You cannot skip unknown or eof token
        /// </summary>
        /// <exception cref="InvalidOperationException">If this is unskippable token</exception>
        public void Skip()
        {
            switch (currentToken)
            {
                case Token.Comment:
                    SkipComment();
                    break;
                case Token.Image:
                    SkipImage();
                    break;
                default: 
                    throw new InvalidOperationException($"Cannot skip token {currentToken}");
            }
        }
        
        /// <summary>
        /// Resets gif stream state, so you can read it from beginning again<br/>
        /// This is useful when you need to playback gif from memory
        /// </summary>
        /// <param name="resetCanvas">Also reset canvas to its initial state, if you set this to "false",
        /// first frame can be drawn ontop of first frame<br/>
        /// <i>(i have no idea if there any gif that exploit drawing first frame on top of last frame,
        /// browsers always reset image state on repeat)</i></param>
        public void Reset(bool resetCanvas = true)
        {
            if (currentStream.Position != dataStartPosition)
                currentStream.Position = dataStartPosition;

            SetCurrentToken(Token.Unknown);
            currentFrame = 0;
            graphicControl = new GraphicControl();

            if (resetCanvas)
                canvas.Reset();
        }

        /// <summary>
        /// <p>Reads and returns comment block from gif</p>
        /// You can call this method only when current token is <code>Token.Comment</code>
        /// </summary>
        /// <returns>Comment from gif file</returns>
        public string ReadComment()
        {
            AssertToken(Token.Comment);
            var text = Encoding.ASCII.GetString(BitUtils.ReadGifBlocks(currentStream));
            SetCurrentToken(Token.Unknown);
            return text;
        }

        /// <summary>
        /// <p>Skips comment block from gif without memory allocations</p>
        /// You can call this method only when current token is <code>Token.Comment</code>
        /// </summary>
        public void SkipComment()
        {
            AssertToken(Token.Comment);
            BitUtils.SkipGifBlocks(currentStream);
            SetCurrentToken(Token.Unknown);
        }

        /// <summary>
        /// <p>Reads image block from Gif file</p>
        /// <p>You can call this method only when current token is <code>Token.Image</code></p>
        /// <br/>
        /// <p><b>NOTE:</b> color array in ImageFrame is shared between frames (for performance reasons)
        /// and if you need to store frames in memory, you should copy each frame to your own array</p>
        /// </summary>
        /// <returns>Decoded image frame</returns>
        /// <exception cref="InvalidOperationException">If current token is not <code>Token.Unknown</code></exception>
        public ImageFrame ReadImage()
        {
            var left = currentStream.ReadInt16LittleEndian();
            var top = currentStream.ReadInt16LittleEndian();
            var width = currentStream.ReadInt16LittleEndian();
            var height = currentStream.ReadInt16LittleEndian();
            var flags = currentStream.ReadByte8();

            var localColorTableSize = BitUtils.GetColorTableSize(flags.GetBitsFromByte(0, 3));
            var isInterlaced = flags.GetBitFromByte(6);
            var hasLocalColorTable = flags.GetBitFromByte(7);

            if (hasLocalColorTable)
                ReadColorTable(localColorTableSize, localColorTable, currentFrame);

            var usedColorTable = hasLocalColorTable
                ? localColorTable
                : globalColorTable;

            var lzwMinCodeSize = currentStream.ReadByte8();

            DecodeLzwImageToCanvas(lzwMinCodeSize, left, top, width, height, usedColorTable,
                graphicControl.transparentColorIndex, isInterlaced, graphicControl.disposalMethod);
            SetCurrentToken(Token.Unknown);

            var frameIndex = currentFrame++;
            return new ImageFrame
            {
                index = frameIndex,
                delay = graphicControl.delayTime,
                colors = canvas.Colors,
            };
        }

        /// <summary>
        /// Skips image reading (not)<br/>
        /// <p>You can call this method only when current token is <code>Token.Image</code></p>
        /// </summary>
        public void SkipImage()
        {
            // Skipping image would be destructive, since next frame can depend on it
            // So read it anyway and pretend we're skipped it just for code readability sake
            // Anyway, why would you want to decode gif and ignore images?
            ReadImage();
        }

        private void ReadHeader()
        {
            // Header
            currentStream.Read(headerBuffer, 0, headerBuffer.Length);

            if (headerBuffer[0] != 'G' ||
                headerBuffer[1] != 'I' ||
                headerBuffer[2] != 'F' ||
                headerBuffer[3] != '8' ||
                headerBuffer[4] != '7' && headerBuffer[4] != '9' ||
                headerBuffer[5] != 'a')
            {
                throw new ArgumentException("Invalid or corrupted Gif file");
            }

            switch ((char) headerBuffer[4])
            {
                case '7':
                    Version = FileVersion.Gif87a;
                    break;
                case '9':
                    Version = FileVersion.Gif89a;
                    break;
            }

            // Screen descriptor
            Width = currentStream.ReadInt16LittleEndian();
            Height = currentStream.ReadInt16LittleEndian();

            var flags = currentStream.ReadByte8();
            var globalTableSize = BitUtils.GetColorTableSize(flags.GetBitsFromByte(0, 3));
            var hasGlobalColorTable = flags.GetBitFromByte(7);

            var transparentColorIndex = currentStream.ReadByte8();
            var pixelAspectRatio = currentStream.ReadByte8();

            canvas.SetSize(Width, Height);
            graphicControl = new GraphicControl();
            currentFrame = 0;

            if (hasGlobalColorTable)
            {
                ReadColorTable(globalTableSize, globalColorTable, 0);
                canvas.BackgroundColor = globalColorTable[transparentColorIndex];
            }

            dataStartPosition = currentStream.Position;
            Reset();
        }

        private void ReadGraphicControlExtension()
        {
            currentStream.AssertByte(0x04);

            var graphicsFlags = currentStream.ReadByte8();
            var disposalMethodValue = graphicsFlags.GetBitsFromByte(2, 3);

            graphicControl.hasTransparency = graphicsFlags.GetBitFromByte(0);
            graphicControl.delayTime = currentStream.ReadInt16LittleEndian();
            graphicControl.transparentColorIndex = currentStream.ReadByte8();

            // Boolean should be read anyway, so there is no point to not read original transparentColorIndex value
            if (!graphicControl.hasTransparency)
                graphicControl.transparentColorIndex = -1;

            switch (disposalMethodValue)
            {
                case 0:
                case 1:
                    graphicControl.disposalMethod = GifCanvas.DisposalMethod.Keep;
                    break;
                case 2:
                    graphicControl.disposalMethod = GifCanvas.DisposalMethod.ClearToBackgroundColor;
                    break;
                case 3:
                    graphicControl.disposalMethod = GifCanvas.DisposalMethod.Revert;
                    break;
                default: throw new ArgumentException($"Invalid disposal method type: {disposalMethodValue}");
            }

            currentStream.AssertByte(0x00);
        }

        private void DecodeLzwImageToCanvas(int lzwMinCodeSize, int x, int y, int width, int height,
            Color32[] colorTable,
            int transparentColorIndex, bool isInterlaced, GifCanvas.DisposalMethod disposalMethod)
        {
            lzwDictionary.InitWithWordSize(lzwMinCodeSize);
            canvas.BeginNewFrame(x, y, width, height, colorTable, transparentColorIndex, isInterlaced, disposalMethod);

            var lastCodeId = -1;
            blockReader.StartNewReading();

            while (true)
            {
                var codeId = blockReader.ReadBits(lzwDictionary.CodeSize);

                if (lzwDictionary.IsClearCode(codeId))
                {
                    lzwDictionary.Clear();
                    lastCodeId = -1;
                }
                else if (lzwDictionary.IsStopCode(codeId))
                {
                    break;
                }
                else
                {
                    if (lzwDictionary.Contains(codeId))
                    {
                        lzwDictionary.OutputCode(codeId, canvas);

                        if (lastCodeId >= 0)
                            lzwDictionary.CreateNewCode(lastCodeId, codeId);

                        lastCodeId = codeId;
                    }
                    else
                    {
                        lastCodeId = lzwDictionary.CreateNewCode(lastCodeId, lastCodeId);
                        lzwDictionary.OutputCode(lastCodeId, canvas);
                    }
                }
            }
        }

        private void ReadColorTable(int size, Color32[] target, int frame)
        {
            currentStream.Read(colorTableBuffer, 0, size * 3);

            var position = 0;
            for (var i = 0; i < size; i++)
            {
                target[i] = new Color32(
                    colorTableBuffer[position++],
                    colorTableBuffer[position++],
                    colorTableBuffer[position++],
                    255);
            }
        }

        private Token SetCurrentToken(Token token)
        {
            currentToken = token;
            return token;
        }

        private void AssertToken(Token token)
        {
            if (currentToken != token)
                throw new InvalidOperationException(
                    $"Cannot invoke this method while last token is \"{currentToken}\", " +
                    $"method should be called when token is {token}");
        }

        public enum FileVersion
        {
            /// <summary>
            /// Gif specification from year 1989
            /// </summary>
            Gif89a,
            
            /// <summary>
            /// Gif specification from year 1987
            /// </summary>
            Gif87a
        }

        public enum Token
        {
            /// <summary>
            /// Token is unknown, You should call "NextToken()" to read it
            /// </summary>
            Unknown,
            
            /// <summary>
            /// Next token is image, You should call ReadImage() to read it
            /// </summary>
            Image,
            
            /// <summary>
            /// Next token is comment, You should call ReadComment() to read it
            /// </summary>
            Comment,
            
            /// <summary>
            /// Next token is End of file, all gif data was successfully read
            /// </summary>
            EndOfFile
        }

        public struct ImageFrame
        {
            /// <summary>
            /// Index of this frame
            /// </summary>
            public int index;
            
            /// <summary>
            /// Delay to next image (Display duration)
            /// </summary>
            public int delay;
            
            /// <summary>
            /// Color array of image frame, with Width * Height size
            /// <p>NOTE: This color array is reused between frames, if you want to collect frames, you should create copy of it</p>
            /// </summary>
            public Color32[] colors;
        }

        private struct GraphicControl
        {
            public bool hasTransparency;
            public int delayTime;
            public int transparentColorIndex;
            public GifCanvas.DisposalMethod disposalMethod;
        }

        private const int extensionBlock = 0x21;
        private const int imageDescriptorBlock = 0x2c;
        private const int endOfFile = 0x3b;

        private const int plainTextLabel = 0x01;
        private const int graphicControlLabel = 0xf9;
        private const int commentLabel = 0xfe;
        private const int applicationExtensionLabel = 0xff;
    }
}