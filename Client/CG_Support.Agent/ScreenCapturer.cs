using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Forms;

namespace CG_Support.Agent
{
    public static class ScreenCapturer
    {
        public static byte[] CaptureScreen(int scaleWidth = 0, int scaleHeight = 0, int quality = 70)
        {
            try
            {
                // 1. Obtener límites de la pantalla principal
                Rectangle bounds = Screen.PrimaryScreen?.Bounds ?? new Rectangle(0, 0, 1920, 1080);
                
                // 2. Crear bitmap origen
                using (Bitmap rawBitmap = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppArgb))
                {
                    using (Graphics g = Graphics.FromImage(rawBitmap))
                    {
                        g.CopyFromScreen(bounds.X, bounds.Y, 0, 0, bounds.Size, CopyPixelOperation.SourceCopy);
                    }

                    // 3. Redimensionar si se solicita (por ejemplo, para las miniaturas de la consola del profesor)
                    if (scaleWidth > 0 && scaleHeight > 0)
                    {
                        using (Bitmap scaledBitmap = new Bitmap(scaleWidth, scaleHeight, PixelFormat.Format32bppArgb))
                        {
                            using (Graphics sg = Graphics.FromImage(scaledBitmap))
                            {
                                sg.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.Low; // Baja calidad para velocidad de renderizado
                                sg.DrawImage(rawBitmap, 0, 0, scaleWidth, scaleHeight);
                            }
                            return BitmapToJpegBytes(scaledBitmap, quality);
                        }
                    }

                    return BitmapToJpegBytes(rawBitmap, quality);
                }
            }
            catch (Exception)
            {
                // Retornar un buffer vacío en caso de error
                return Array.Empty<byte>();
            }
        }

        private static byte[] BitmapToJpegBytes(Bitmap bitmap, int quality)
        {
            using (MemoryStream ms = new MemoryStream())
            {
                ImageCodecInfo? jpegCodec = GetEncoderInfo(ImageFormat.Jpeg);
                if (jpegCodec == null)
                {
                    bitmap.Save(ms, ImageFormat.Jpeg);
                    return ms.ToArray();
                }

                // Configurar la calidad de compresión
                System.Drawing.Imaging.Encoder encoder = System.Drawing.Imaging.Encoder.Quality;
                EncoderParameters encoderParameters = new EncoderParameters(1);
                EncoderParameter encoderParameter = new EncoderParameter(encoder, (long)quality);
                encoderParameters.Param[0] = encoderParameter;

                bitmap.Save(ms, jpegCodec, encoderParameters);
                return ms.ToArray();
            }
        }

        private static ImageCodecInfo? GetEncoderInfo(ImageFormat format)
        {
            ImageCodecInfo[] codecs = ImageCodecInfo.GetImageEncoders();
            foreach (ImageCodecInfo codec in codecs)
            {
                if (codec.FormatID == format.Guid)
                {
                    return codec;
                }
            }
            return null;
        }
    }
}
