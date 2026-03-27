using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Media.Imaging;

namespace RevitFamilyBrowser
{
    public static class ImageUtils
    {
        public static Bitmap CreateSymbolicIcon(string categoryName, System.Drawing.Size size)
        {
            Bitmap bmp = new Bitmap(size.Width, size.Height);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                g.Clear(System.Drawing.Color.White);

                float w = size.Width;
                float h = size.Height;
                float padding = w * 0.15f;

                // Determine category and theme
                if (categoryName.Contains("태그") || categoryName.ToLower().Contains("tag"))
                {
                    // Draw Tag (Diamond)
                    using (var brush = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(232, 242, 255)))
                    using (var pen = new System.Drawing.Pen(System.Drawing.Color.FromArgb(0, 122, 255), 2))
                    {
                        System.Drawing.PointF[] points = {
                            new System.Drawing.PointF(w/2, padding),
                            new System.Drawing.PointF(w - padding, h/2),
                            new System.Drawing.PointF(w/2, h - padding),
                            new System.Drawing.PointF(padding, h/2)
                        };
                        g.FillPolygon(brush, points);
                        g.DrawPolygon(pen, points);
                        
                        // Centered dot
                        g.FillEllipse(new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(0, 122, 255)), 
                                      w/2-3, h/2-3, 6, 6);
                    }
                }
                else if (categoryName.Contains("상세") || categoryName.ToLower().Contains("detail"))
                {
                    // Draw Detail (Brick Pattern)
                    using (var brush = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(232, 249, 237)))
                    using (var pen = new System.Drawing.Pen(System.Drawing.Color.FromArgb(52, 199, 89), 2))
                    {
                        float bw = (w - 2 * padding) / 2.5f;
                        float bh = (h - 2 * padding) / 4f;

                        // Background Rect
                        g.FillRectangle(brush, padding, padding, w - 2*padding, h - 2*padding);
                        
                        // Drawing "Bricks"
                        for (int row = 0; row < 4; row++)
                        {
                            float offset = (row % 2 == 0) ? 0 : bw / 2;
                            for (int col = 0; col < 3; col++)
                            {
                                float x = padding + offset + col * bw;
                                float y = padding + row * bh;
                                if (x + bw <= w - padding + 5)
                                    g.DrawRectangle(pen, x, y, bw, bh);
                            }
                        }
                    }
                }
                else
                {
                    // Draw General (Circular Badge / Annotation)
                    using (var brush = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(255, 242, 232)))
                    using (var pen = new System.Drawing.Pen(System.Drawing.Color.FromArgb(255, 149, 0), 2))
                    {
                        float r = (w - 2 * padding) / 2;
                        g.FillEllipse(brush, w/2 - r, h/2 - r, r*2, r*2);
                        g.DrawEllipse(pen, w/2 - r, h/2 - r, r*2, r*2);

                        // Inner Symbol (Circle with dot)
                        g.DrawEllipse(pen, w/2 - r/2, h/2 - r/2, r, r);
                    }
                }
            }
            return bmp;
        }

        public static BitmapImage Convert(Bitmap src)
        {
            if (src == null) return null;

            MemoryStream ms = new MemoryStream();
            src.Save(ms, ImageFormat.Png);
            
            BitmapImage image = new BitmapImage();
            image.BeginInit();
            ms.Seek(0, SeekOrigin.Begin);
            image.StreamSource = ms;
            image.CacheOption = BitmapCacheOption.OnLoad; // Important for memory management
            image.EndInit();
            image.Freeze(); // Make it cross-thread accessible

            return image;
        }
    }
}
