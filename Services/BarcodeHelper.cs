using SkiaSharp;
using System;
using System.IO;
using ZXing;
using ZXing.QrCode;
using ZXing.Common;
using Microsoft.Maui.Controls;

namespace TaroziAPP.Services
{
    public static class BarcodeHelper
    {
        public static byte[]? GenerateQrCodeBytes(string text, int width = 300, int height = 300)
        {
            try 
            {
                var options = new QrCodeEncodingOptions
                {
                    Width = width,
                    Height = height,
                    Margin = 1,
                    CharacterSet = "UTF-8"
                };

                // Use generic writer to key BitMatrix
                var writer = new BarcodeWriterGeneric
                {
                    Format = BarcodeFormat.QR_CODE,
                    Options = options
                };

                // Get raw boolean matrix
                BitMatrix matrix = writer.Encode(text);

                // Create SKBitmap with requested size (not matrix size)
                // Use requested width/height (300x300)
                var info = new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
                
                using (var bitmap = new SKBitmap(info))
                {
                    // Fill with White background
                    bitmap.Erase(SKColors.White);

                    using (var canvas = new SKCanvas(bitmap))
                    {
                        // Calculate scale
                        float scaleX = (float)width / matrix.Width;
                        float scaleY = (float)height / matrix.Height;

                        using (var paint = new SKPaint { Color = SKColors.Black, Style = SKPaintStyle.Fill, IsAntialias = false })
                        {
                            for (int y = 0; y < matrix.Height; y++)
                            {
                                for (int x = 0; x < matrix.Width; x++)
                                {
                                    if (matrix[x, y])
                                    {
                                        var rect = SKRect.Create(x * scaleX, y * scaleY, scaleX, scaleY);
                                        canvas.DrawRect(rect, paint);
                                    }
                                }
                            }
                        }
                    }

                    // Encode to PNG stream
                    using (var memoryStream = new MemoryStream())
                    {
                        using (var image = SKImage.FromBitmap(bitmap))
                        {
                             var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
                             encoded.SaveTo(memoryStream);
                        }
                        
                        var imageBytes = memoryStream.ToArray();
                        System.Diagnostics.Debug.WriteLine($"[BarcodeHelper] Generated {imageBytes.Length} bytes of QR image ({width}x{height})");
                        return imageBytes;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[BarcodeHelper] QR Generation failed: {ex}");
                return null;
            }
        }
        
        // Keep old method for backward compatibility if needed, calling new one
        public static ImageSource GenerateQrCode(string text, int width = 300, int height = 300)
        {
             var bytes = GenerateQrCodeBytes(text, width, height);
             if (bytes == null) return null;
             return ImageSource.FromStream(() => new MemoryStream(bytes));
        }
    }
}
