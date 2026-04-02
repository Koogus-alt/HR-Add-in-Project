using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Heerim_FamilyBrowser
{
    public class FamilyItem
    {
        public string Name { get; set; } = string.Empty;
        public string FullPath { get; set; } = string.Empty;
        public string MetadataPath { get; set; } = string.Empty;
        public string ThumbnailPath { get; set; } = string.Empty;
        public ImageSource? Thumbnail { get; set; }
        public string Category { get; set; } = string.Empty;
    }

    public static class FamilyLoader
    {
        public static List<FamilyItem> LoadFromDirectory(string directoryPath, string categoryName)
        {
            var items = new List<FamilyItem>();

            if (!Directory.Exists(directoryPath)) return items;

            // Get all .rfa files
            var rfaFiles = Directory.GetFiles(directoryPath, "*.rfa");

            foreach (var rfa in rfaFiles)
            {
                var baseName = Path.GetFileNameWithoutExtension(rfa);
                var folder = Path.GetDirectoryName(rfa) ?? string.Empty;

                var item = new FamilyItem
                {
                    Name = baseName,
                    FullPath = rfa,
                    MetadataPath = Path.Combine(folder, baseName + ".xml"),
                    ThumbnailPath = Path.Combine(folder, baseName + ".png"),
                    Category = categoryName
                };

                // Load Thumbnail if exists
                if (File.Exists(item.ThumbnailPath))
                {
                    try
                    {
                        var bitmap = new BitmapImage();
                        bitmap.BeginInit();
                        bitmap.UriSource = new Uri(item.ThumbnailPath);
                        bitmap.CacheOption = BitmapCacheOption.OnLoad;
                        bitmap.EndInit();
                        item.Thumbnail = bitmap;
                    }
                    catch { /* Ignore thumbnail error */ }
                }

                items.Add(item);
            }

            return items;
        }
    }
}
