using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Drawing;
using System.Drawing.Imaging;
using System.Windows.Interop;
using System.Runtime.InteropServices;
using System.Diagnostics;

namespace DirectionalBlur
{

    public static class Helper
    {
        static public int GetComponentsNumber(System.Drawing.Imaging.PixelFormat pixelFormat)
        {
            switch (pixelFormat)
            {
                case System.Drawing.Imaging.PixelFormat.Format8bppIndexed:
                    return 1;

                case System.Drawing.Imaging.PixelFormat.Format24bppRgb:
                    return 3;

                case System.Drawing.Imaging.PixelFormat.Format32bppArgb:
                    return 4;

                default:
                    Debug.Assert(false);
                    return 0;
            }
        }
    }


    public static class MyExtensions
    {
        public static double Clamp0_255(this double value)
        {
            return (value < 0) ? 0 : (value > 255) ? 255 : value;
        }
    }

    public class BitmapInfo
    {
        public int Width;
        public int Height;
        public int Stride;
        public int Components;

        public BitmapInfo(BitmapData bitmapData)
        {
            Width = bitmapData.Width;
            Height = bitmapData.Height;
            Stride = Math.Abs(bitmapData.Stride);
            Components = Helper.GetComponentsNumber(bitmapData.PixelFormat);
        }
    }

    public class PreciseColor
    {
        public double A;
        public double R;
        public double G;
        public double B;

        public PreciseColor(double a, double r, double g, double b)
        {
            A = a;
            R = r;
            G = g;
            B = b;
        }

        public static PreciseColor FromArgb(double a, double r, double g, double b)
        {
            return new PreciseColor(a, r, g, b);
        }

        public static PreciseColor Zero
        {
            get{ return new PreciseColor(0, 0, 0, 0);}
        }

        /// <summary>
        /// Convert to 32bpp Color
        /// </summary>
        /// <returns></returns>
        public Color ToColor32()
        {
            return Color.FromArgb(
                Convert.ToByte(A.Clamp0_255()),
                Convert.ToByte(R.Clamp0_255()),
                Convert.ToByte(G.Clamp0_255()),
                Convert.ToByte(B.Clamp0_255()));
        }

        public static PreciseColor operator +(PreciseColor c1, PreciseColor c2)
        {
            return PreciseColor.FromArgb(c1.A + c2.A, c1.R + c2.R, c1.G + c2.G, c1.B + c2.B);
        }

        public static PreciseColor operator -(PreciseColor c1, PreciseColor c2)
        {
            return PreciseColor.FromArgb(c1.A - c2.A, c1.R - c2.R, c1.G - c2.G, c1.B - c2.B);
        }

        public static PreciseColor operator *(double r, PreciseColor c)
        {
            return PreciseColor.FromArgb(r * c.A, r * c.R, r * c.G, r * c.B);
        }

    }

    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        String ImageSourceFileName;

        public MainWindow()
        {
            InitializeComponent();
        }

        private void Window_Drop(object sender, DragEventArgs e)
        {
            String infoMsg = Window_OnDrop_Sub(e);
            if (infoMsg!=null)
            {
                MessageBox.Show(infoMsg);
            }
        }

        private String Window_OnDrop_Sub(DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(DataFormats.FileDrop))
                return "Not a file!";

            String[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
            if (files.Length > 1)
                return "Too many files!";

            ImageSourceFileName = files[0];

            if (!File.Exists(ImageSourceFileName))
                return "Not a file!";

            FileStream fs = null;
            try
            {
                fs = File.Open(ImageSourceFileName, FileMode.Open, FileAccess.Read, FileShare.None);
            }
            catch (IOException)
            {
                if (fs != null)
                    fs.Close();
                return "File already in use!";
            }


            Bitmap bitmapSource = null;
            try
            {
                bitmapSource = new Bitmap(fs);
            }
            catch (System.Exception /*ex*/)
            {
                bitmapSource.Dispose();
                return "Not an image!";
            }

            ImageSource.Source =
                Imaging.CreateBitmapSourceFromHBitmap(
                    bitmapSource.GetHbitmap(), IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());

            int bitmapWidth = bitmapSource.Width;
            int bitmapHeight = bitmapSource.Height;
            Rectangle rect = Rectangle.FromLTRB(0, 0, bitmapWidth, bitmapHeight);

            BitmapData bitmapDataSource = bitmapSource.LockBits(rect,
                ImageLockMode.WriteOnly, bitmapSource.PixelFormat);

            BitmapInfo bitmapInfo = new BitmapInfo(bitmapDataSource);

            int bitmapStride_abs = Math.Abs(bitmapDataSource.Stride);
            int bitmapComponents = Helper.GetComponentsNumber(bitmapDataSource.PixelFormat);
            int dataBytesSize = bitmapStride_abs * bitmapHeight;

            byte[] rgbaValuesBufferSource = new byte[dataBytesSize];
            Marshal.Copy(bitmapDataSource.Scan0, rgbaValuesBufferSource, 0, dataBytesSize);



            // we do not use this matrix, but here it is
            float[] coefficients = new float[] { 0.0545f, 0.224f, 0.4026f, 0.224f, 0.0545f };
            //int kernelSize = coefficients.Length;

            Bitmap bitmapBlurred = new Bitmap(bitmapWidth, bitmapHeight);
            BitmapData bitmapDataBlurred = bitmapBlurred.LockBits(rect,
                ImageLockMode.WriteOnly, bitmapSource.PixelFormat);
            byte[] rgbaValuesBufferBlurred = new byte[dataBytesSize];
            for (int y = 0; y < bitmapHeight; y++)
            {
                for (int x = 0; x < bitmapWidth; x++)
                {
                    PreciseColor color = PreciseColor.Zero;

                    for (int i = -2; i < /*kernelSize*/ 3 ; ++i )
                    {
                        PreciseColor sourceColor = GetPixelColorFromArray(rgbaValuesBufferSource, x + i, y, bitmapInfo);
                        color = color + coefficients[i+2] * sourceColor;
                    }

                    SetPixelColorInArray(rgbaValuesBufferBlurred, x, y, color.ToColor32(), bitmapInfo);
                }
            }
            Marshal.Copy(rgbaValuesBufferBlurred, 0, bitmapDataBlurred.Scan0, dataBytesSize);
            bitmapBlurred.UnlockBits(bitmapDataBlurred);

            ImageBlurred.Source =
                Imaging.CreateBitmapSourceFromHBitmap(
                    bitmapBlurred.GetHbitmap(), IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());


            return null;
        }





 

        static private PreciseColor GetPixelColorFromArray(
            byte[] pixelsArray, int x, int y, BitmapInfo bitmapInfo)
        {
            if (x < 0 || y < 0 || x >= bitmapInfo.Width || y >= bitmapInfo.Height)
                return PreciseColor.FromArgb(0,0,0,0);

            int indexDithered = (bitmapInfo.Stride * y) + (bitmapInfo.Components * x);
            byte A = (bitmapInfo.Components == 4) ? pixelsArray[indexDithered + 3] : (byte)255;
            byte R = pixelsArray[indexDithered + 2];
            byte G = pixelsArray[indexDithered + 1];
            byte B = pixelsArray[indexDithered + 0];

            return PreciseColor.FromArgb(A, R, G, B);
        }

        static private void SetPixelColorInArray(
            byte[] pixelsArray, int x, int y, System.Drawing.Color color, BitmapInfo bitmapInfo)
        {
            if (x < 0 || y < 0 || x >= bitmapInfo.Width || y >= bitmapInfo.Height)
                return;

            int indexDithered = (bitmapInfo.Stride * y) + (bitmapInfo.Components * x);
            pixelsArray[indexDithered + 0] = color.B;  // B
            pixelsArray[indexDithered + 1] = color.G;  // G
            pixelsArray[indexDithered + 2] = color.R;  // R
            if (bitmapInfo.Components == 4)
                pixelsArray[indexDithered + 3] = color.A;  // A
        }




    }
}
