using System;
using UnityEngine;

namespace ThreeDISevenZeroR.UnityGifDecoder
{
    public class GifCanvas
    {
        public enum DisposalMethod
        {
            /// <summary>
            /// Keep previous frame and draw new frame on top of it
            /// </summary>
            Keep,
            
            /// <summary>
            /// Clear canvas and draw
            /// </summary>
            ClearToBackgroundColor,
            
            /// <summary>
            /// Revert previous drawing operation, so canvas will contain previous frame
            /// </summary>
            Revert
        }

        /// <summary>
        /// Get color array from this Canvas
        /// You should not modify this 
        /// </summary>
        public Color32[] Colors => canvasColors;
        
        /// <summary>
        /// <p>Since pixel rows for Texture2D start from bottom, original gif image will look upside down<br/></p>
        /// <br/>
        /// <p>So gif decoder can flip image Without performance hit,
        /// and you can provide resulting array to Texture2D without flipping it manually</p>
        /// <br/>
        /// <p>Default value is <b>TRUE</b>, if you want original color order
        /// (which will look flipped on Texture2D) you can set this to false</p>
        /// </summary>
        public bool FlipVertically { get; set; } = true;

        /// <summary>
        /// Color which will be used for background fill<br/>
        /// Note, that background is always transparent, so only r, g, b components are used (alpha is 0)
        /// </summary>
        public Color32 BackgroundColor { get; set; }

        private Color32[] canvasColors;
        private Color32[] revertDisposalBuffer;
        private int canvasWidth;
        private int canvasHeight;
        private bool canvasIsEmpty;

        private Color32[] framePalette;
        private DisposalMethod frameDisposalMethod;

        private int frameCanvasPosition;
        private int frameCanvasRowEndPosition;
        private int frameTransparentColorIndex;
        private int frameRowCurrent;
        private int[] frameRowStart;
        private int[] frameRowEnd;

        public GifCanvas()
        {
            canvasIsEmpty = true;
        }

        public GifCanvas(int width, int height) : this()
        {
            SetSize(width, height);
        }

        public void SetSize(int width, int height)
        {
            if (width != canvasWidth || height != canvasHeight)
            {
                var size = width * height;
                Array.Resize(ref canvasColors, size);
                Array.Resize(ref frameRowStart, height);
                Array.Resize(ref frameRowEnd, height);

                if (revertDisposalBuffer != null)
                    Array.Resize(ref revertDisposalBuffer, size);
                
                canvasWidth = width;
                canvasHeight = height;
            }

            Reset();
        }

        public void Reset()
        {
            frameDisposalMethod = DisposalMethod.Keep;

            if (!canvasIsEmpty)
            {
                FillWithColor(new Color32(0, 0, 0, 0));
                canvasIsEmpty = true;
            }
        }

        public void BeginNewFrame(int x, int y, int width, int height, Color32[] palette, 
            int transparentColorIndex, bool isInterlaced, DisposalMethod disposalMethod)
        {
            switch (frameDisposalMethod)
            {
                case DisposalMethod.ClearToBackgroundColor:
                    FillWithColor(new Color32(BackgroundColor.r, BackgroundColor.g, BackgroundColor.b, 0));
                    break;

                case DisposalMethod.Revert:
                    if(disposalMethod != DisposalMethod.Keep)
                        Array.Copy(revertDisposalBuffer, 0, canvasColors, 0, revertDisposalBuffer.Length);
                    break;
            }

            switch (disposalMethod)
            {
                case DisposalMethod.Revert:
                    if (revertDisposalBuffer == null)
                        revertDisposalBuffer = new Color32[canvasColors.Length];

                    Array.Copy(canvasColors, 0,
                        revertDisposalBuffer, 0, revertDisposalBuffer.Length);
                    break;
            }

            framePalette = palette;
            frameDisposalMethod = disposalMethod;
            canvasIsEmpty = false;

            // Start before canvas, so next pixel output will load correct region
            frameCanvasPosition = 0;
            frameRowCurrent = -1;
            frameCanvasRowEndPosition = -1;
            frameTransparentColorIndex = transparentColorIndex;
            
            RouteFrameDrawing(x, y, width, height, isInterlaced);
        }

        public void OutputPixel(int color)
        {
            if (frameCanvasPosition >= frameCanvasRowEndPosition)
            {
                frameRowCurrent++;
                frameCanvasPosition = frameRowStart[frameRowCurrent];
                frameCanvasRowEndPosition = frameRowEnd[frameRowCurrent];
            }

            if (color != frameTransparentColorIndex)
                canvasColors[frameCanvasPosition++] = framePalette[color];
            else
                frameCanvasPosition++;
        }

        private void RouteFrameDrawing(int x, int y, int width, int height, bool deinterlace)
        {
            var currentRow = 0;

            void ScheduleRowIndex(int row)
            {
                var startPosition = FlipVertically 
                    ? (canvasHeight - 1 - (y + row)) * canvasWidth + x
                    : (y + row) * canvasWidth + x;
                
                frameRowStart[currentRow] = startPosition;
                frameRowEnd[currentRow] = startPosition + width;
                currentRow++;
            }

            if (deinterlace)
            {
                for (var i = 0; i < height; i += 8) ScheduleRowIndex(i); // every 8, start with 0
                for (var i = 4; i < height; i += 8) ScheduleRowIndex(i); // every 0, start with 4
                for (var i = 2; i < height; i += 4) ScheduleRowIndex(i); // every 4, start with 2
                for (var i = 1; i < height; i += 2) ScheduleRowIndex(i); // every 2, start with 1
            }
            else
            {
                for (var i = 0; i < height; i++) ScheduleRowIndex(i); // every row in order
            }
        }

        public void FillWithColor(Color32 color)
        {
            for (var i = 0; i < canvasColors.Length; i++)
                canvasColors[i] = color;
        }
    }
}