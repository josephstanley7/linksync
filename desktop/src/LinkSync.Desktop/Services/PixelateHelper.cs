using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace LinkSync.Desktop.Services
{
    public static class PixelateHelper
    {
        /// <summary>
        /// Applies mosaic pixelation to a specific rectangular region on a bitmap.
        /// </summary>
        public static WriteableBitmap ApplyPixelate(BitmapSource source, Int32Rect region, int blockSize = 14)
        {
            var formatConverted = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
            var writeable = new WriteableBitmap(formatConverted);

            int width = writeable.PixelWidth;
            int height = writeable.PixelHeight;

            int rx = Math.Clamp(region.X, 0, width - 1);
            int ry = Math.Clamp(region.Y, 0, height - 1);
            int rw = Math.Clamp(region.Width, 1, width - rx);
            int rh = Math.Clamp(region.Height, 1, height - ry);

            int stride = width * 4;
            byte[] pixels = new byte[height * stride];
            writeable.CopyPixels(pixels, stride, 0);

            for (int by = ry; by < ry + rh; by += blockSize)
            {
                for (int bx = rx; bx < rx + rw; bx += blockSize)
                {
                    int blockW = Math.Min(blockSize, rx + rw - bx);
                    int blockH = Math.Min(blockSize, ry + rh - by);

                    long totalB = 0, totalG = 0, totalR = 0, totalA = 0;
                    int count = 0;

                    for (int y = by; y < by + blockH; y++)
                    {
                        for (int x = bx; x < bx + blockW; x++)
                        {
                            int idx = (y * stride) + (x * 4);
                            totalB += pixels[idx];
                            totalG += pixels[idx + 1];
                            totalR += pixels[idx + 2];
                            totalA += pixels[idx + 3];
                            count++;
                        }
                    }

                    if (count == 0) continue;

                    byte avgB = (byte)(totalB / count);
                    byte avgG = (byte)(totalG / count);
                    byte avgR = (byte)(totalR / count);
                    byte avgA = (byte)(totalA / count);

                    for (int y = by; y < by + blockH; y++)
                    {
                        for (int x = bx; x < bx + blockW; x++)
                        {
                            int idx = (y * stride) + (x * 4);
                            pixels[idx] = avgB;
                            pixels[idx + 1] = avgG;
                            pixels[idx + 2] = avgR;
                            pixels[idx + 3] = avgA;
                        }
                    }
                }
            }

            writeable.WritePixels(new Int32Rect(0, 0, width, height), pixels, stride, 0);
            return writeable;
        }

        /// <summary>
        /// Applies a fast box blur to a specific rectangular region on a bitmap.
        /// </summary>
        public static WriteableBitmap ApplyBlur(BitmapSource source, Int32Rect region, int radius = 8)
        {
            var formatConverted = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
            var writeable = new WriteableBitmap(formatConverted);

            int width = writeable.PixelWidth;
            int height = writeable.PixelHeight;

            int rx = Math.Clamp(region.X, 0, width - 1);
            int ry = Math.Clamp(region.Y, 0, height - 1);
            int rw = Math.Clamp(region.Width, 1, width - rx);
            int rh = Math.Clamp(region.Height, 1, height - ry);

            int stride = width * 4;
            byte[] src = new byte[height * stride];
            byte[] dst = new byte[height * stride];
            writeable.CopyPixels(src, stride, 0);
            Array.Copy(src, dst, src.Length);

            // Horizontal blur pass on target region
            for (int y = ry; y < ry + rh; y++)
            {
                for (int x = rx; x < rx + rw; x++)
                {
                    int totalB = 0, totalG = 0, totalR = 0, totalA = 0;
                    int hits = 0;

                    for (int kx = -radius; kx <= radius; kx++)
                    {
                        int sx = x + kx;
                        if (sx >= rx && sx < rx + rw)
                        {
                            int idx = (y * stride) + (sx * 4);
                            totalB += src[idx];
                            totalG += src[idx + 1];
                            totalR += src[idx + 2];
                            totalA += src[idx + 3];
                            hits++;
                        }
                    }

                    int outIdx = (y * stride) + (x * 4);
                    dst[outIdx] = (byte)(totalB / hits);
                    dst[outIdx + 1] = (byte)(totalG / hits);
                    dst[outIdx + 2] = (byte)(totalR / hits);
                    dst[outIdx + 3] = (byte)(totalA / hits);
                }
            }

            // Vertical blur pass
            for (int y = ry; y < ry + rh; y++)
            {
                for (int x = rx; x < rx + rw; x++)
                {
                    int totalB = 0, totalG = 0, totalR = 0, totalA = 0;
                    int hits = 0;

                    for (int ky = -radius; ky <= radius; ky++)
                    {
                        int sy = y + ky;
                        if (sy >= ry && sy < ry + rh)
                        {
                            int idx = (sy * stride) + (x * 4);
                            totalB += dst[idx];
                            totalG += dst[idx + 1];
                            totalR += dst[idx + 2];
                            totalA += dst[idx + 3];
                            hits++;
                        }
                    }

                    int outIdx = (y * stride) + (x * 4);
                    src[outIdx] = (byte)(totalB / hits);
                    src[outIdx + 1] = (byte)(totalG / hits);
                    src[outIdx + 2] = (byte)(totalR / hits);
                    src[outIdx + 3] = (byte)(totalA / hits);
                }
            }

            writeable.WritePixels(new Int32Rect(0, 0, width, height), src, stride, 0);
            return writeable;
        }
    }
}
