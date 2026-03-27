using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace RevitFamilyBrowser
{
    public enum LoaderMode
    {
        Load,
        Reset
    }

    public class FamilyDataLoader : IExternalEventHandler
    {
        private FamilyBrowserView _view;
        public LoaderMode Mode { get; set; } = LoaderMode.Load;

        public FamilyDataLoader(FamilyBrowserView view)
        {
            _view = view;
        }

        public void Execute(UIApplication app)
        {
            UIDocument uidoc = app.ActiveUIDocument;
            if (uidoc == null) return;
            Document doc = uidoc.Document;

            if (Mode == LoaderMode.Reset)
            {
                uidoc.Selection.SetElementIds(new List<ElementId>());
                return;
            }



            // Load Favorites (v97 JSON format with parameters)
            var favorites = new Dictionary<string, Dictionary<string, string>>();
            string appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string favoritesFile = System.IO.Path.Combine(appDataPath, "Autodesk", "Revit", "Addins", "2026", "RevitFamilyBrowser", "favorites_v2.json");
            
            if (System.IO.File.Exists(favoritesFile))
            {
                try
                {
                    string json = System.IO.File.ReadAllText(favoritesFile);
                    var docList = System.Text.Json.JsonDocument.Parse(json).RootElement;
                    foreach (var element in docList.EnumerateArray())
                    {
                        if (element.TryGetProperty("Key", out var keyProp))
                        {
                            string key = keyProp.GetString();
                            var paramDict = new Dictionary<string, string>();
                            if (element.TryGetProperty("Parameters", out var paramProp) && paramProp.ValueKind == System.Text.Json.JsonValueKind.Object)
                            {
                                foreach (var prop in paramProp.EnumerateObject())
                                {
                                    paramDict[prop.Name] = prop.Value.GetString();
                                }
                            }
                            favorites[key] = paramDict;
                        }
                    }
                }
                catch (Exception)
                {
                    // Fallback or ignore
                }
            }
            else
            {
                // Fallback to v96 text file
                string oldFavoritesFile = System.IO.Path.Combine(appDataPath, "Autodesk", "Revit", "Addins", "2026", "RevitFamilyBrowser", "favorites.txt");
                if (System.IO.File.Exists(oldFavoritesFile))
                {
                    var lines = System.IO.File.ReadAllLines(oldFavoritesFile);
                    foreach (var line in lines)
                    {
                        if (!string.IsNullOrWhiteSpace(line))
                            favorites[line.Trim()] = new Dictionary<string, string>();
                    }
                }
            }

            // Load User Category Settings
            string appDataDir = System.IO.Path.Combine(appDataPath, "Autodesk", "Revit", "Addins", "2026", "RevitFamilyBrowser");
            if (!System.IO.Directory.Exists(appDataDir)) System.IO.Directory.CreateDirectory(appDataDir);

            List<string> userFamilyCats = LoadCategories(System.IO.Path.Combine(appDataDir, "categories_family.txt"));
            List<string> userAnnotateCats = LoadCategories(System.IO.Path.Combine(appDataDir, "categories_annotate.txt"));

            // 1. Collect Families (Model Categories + System Families)
            var modelCategories = new List<BuiltInCategory>();
            if (userFamilyCats.Count > 0)
            {
                foreach (var catName in userFamilyCats)
                {
                    // Revit categories can have spaces, but BuiltInCategory enums usually don't or use OST_
                    // Try exact match or OST_ prefix
                    var foundCat = doc.Settings.Categories.Cast<Category>().FirstOrDefault(c => c.Name == catName);
                    if (foundCat != null)
                    {
                        // Revit 2024+ uses .Value (long)
                        modelCategories.Add((BuiltInCategory)(int)foundCat.Id.Value);
                    }
                }
            }
            else
            {
                // Default Hardcoded List
                modelCategories = new List<BuiltInCategory>
                {
                    BuiltInCategory.OST_Furniture, BuiltInCategory.OST_GenericModel, BuiltInCategory.OST_SpecialityEquipment,
                    BuiltInCategory.OST_PlumbingFixtures, BuiltInCategory.OST_LightingFixtures, BuiltInCategory.OST_MechanicalEquipment,
                    BuiltInCategory.OST_Windows, BuiltInCategory.OST_Doors
                };
            }

            var familyCollector = new FilteredElementCollector(doc)
                .WhereElementIsElementType()
                .Cast<ElementType>()
                .Where(x => x.Category != null && x.Category.CategoryType == CategoryType.Model);

            if (modelCategories.Count > 0)
            {
                ElementMulticategoryFilter multiCategoryFilter = new ElementMulticategoryFilter(modelCategories);
                familyCollector = familyCollector.Where(x => multiCategoryFilter.PassesFilter(x));
            }

            // Exclude unwanted
            familyCollector = familyCollector.Where(x => 
                x.Category.Id != new ElementId(BuiltInCategory.OST_CurtainWallPanels) &&
                x.Category.Id != new ElementId(BuiltInCategory.OST_CurtainWallMullions) &&
                x.Category.Id != new ElementId(BuiltInCategory.OST_DetailComponents));

            List<SmartAssistItem> familyItems = new List<SmartAssistItem>();
            foreach (var symbol in familyCollector)
            {
                // Get Preview Image
                System.Drawing.Size imgSize = new System.Drawing.Size(256, 256);
                var bitmap = symbol.GetPreviewImage(imgSize);
                System.Windows.Media.ImageSource imageSource = null;
                if (bitmap != null)
                {
                    imageSource = ImageUtils.Convert(bitmap);
                }

                string baseKey = symbol.Category.Name + "::" + symbol.Name;
                
                // Find all favorites that match this base key natively (with or without custom name)
                var matchingFavs = favorites.Where(kvp => kvp.Key == baseKey || kvp.Key.StartsWith(baseKey + "::")).ToList();
                
                // v115: A master item is only "favorited" if an exact match exists in the favorites list
                // (i.e., favorited without parameters or custom name)
                bool isMasterFav = matchingFavs.Any(f => f.Key == baseKey && (f.Value == null || f.Value.Count == 0));

                // 1. Add Master Item (Always clean)
                familyItems.Add(new SmartAssistItem 
                { 
                    Name = symbol.Name,
                    Image = imageSource,
                    SymbolId = symbol.Id,
                    CategoryName = symbol.Category.Name,
                    ItemType = "Family",
                    IsFavorite = isMasterFav,
                    IsPreset = false, // Master is NOT a preset
                    Parameters = new Dictionary<string, string>() // Master has no parameters
                });

                // 2. Add Presets
                foreach (var fav in matchingFavs)
                {
                    // If it's the base key AND it has no parameters, it's just the master being favorited (already added above)
                    if (fav.Key == baseKey && (fav.Value == null || fav.Value.Count == 0))
                        continue;

                    string customName = "";
                    if (fav.Key.Length > baseKey.Length + 2)
                    {
                        customName = fav.Key.Substring(baseKey.Length + 2);
                    }

                    familyItems.Add(new SmartAssistItem 
                    { 
                        Name = symbol.Name,
                        Image = imageSource,
                        SymbolId = symbol.Id,
                        CategoryName = symbol.Category.Name,
                        ItemType = "Family",
                        CustomName = customName,
                        IsFavorite = true,
                        IsPreset = true, // This is a preset
                        Parameters = fav.Value
                    });
                }
            }

            // 2. Collect Annotations
            var annotateCollector = new FilteredElementCollector(doc)
                .OfClass(typeof(FamilySymbol))
                .Cast<FamilySymbol>()
                .Where(x => x.Category != null && (x.Category.CategoryType == CategoryType.Annotation || x.Category.Id.Value == (long)BuiltInCategory.OST_DetailComponents));

            if (userAnnotateCats.Any())
            {
                var filteredAnnotate = annotateCollector.Where(x => userAnnotateCats.Any(u => string.Equals(u, x.Category.Name, StringComparison.OrdinalIgnoreCase))).ToList();
                if (filteredAnnotate.Any())
                {
                    annotateCollector = filteredAnnotate;
                }
                // If empty, fall back to original collector (All annotations)
            }

            List<SmartAssistItem> annotateItems = new List<SmartAssistItem>();
            foreach (var symbol in annotateCollector)
            {
                // Previews disabled for Annotations
                System.Windows.Media.ImageSource imageSource = null;

                // Grouping by actual Category Name
                string categoryName = symbol.Category.Name;
                string itemName = symbol.Family.Name + " : " + symbol.Name;
                string baseKey = categoryName + "::" + itemName;

                // Backward compatibility check for v101 and older
                string legacyCategory = symbol.Category.Id == new ElementId(BuiltInCategory.OST_DetailComponents) ? "상세 항목" : "주석 기호";
                string legacyKey = legacyCategory + "::" + itemName;
                
                var matchingFavs = favorites.Where(kvp => kvp.Key == baseKey || kvp.Key.StartsWith(baseKey + "::") || kvp.Key == legacyKey || kvp.Key.StartsWith(legacyKey + "::")).ToList();
                
                // v115: A master item is only "favorited" if an exact match exists
                bool isMasterFav = matchingFavs.Any(f => (f.Key == baseKey || f.Key == legacyKey) && (f.Value == null || f.Value.Count == 0));

                // 1. Add Master Item
                annotateItems.Add(new SmartAssistItem 
                { 
                    Name = itemName,
                    Image = imageSource,
                    SymbolId = symbol.Id,
                    CategoryName = categoryName,
                    ItemType = "Annotate",
                    IsFavorite = isMasterFav,
                    IsPreset = false,
                    Parameters = new Dictionary<string, string>()
                });

                // 2. Add Presets
                foreach (var fav in matchingFavs)
                {
                    string matchedBaseKey = fav.Key.StartsWith(legacyKey) ? legacyKey : baseKey;
                    
                    // Skip if it's the master favorite (already added)
                    if (fav.Key == matchedBaseKey && (fav.Value == null || fav.Value.Count == 0))
                        continue;

                    string customName = "";
                    if (fav.Key.Length > matchedBaseKey.Length + 2)
                    {
                        customName = fav.Key.Substring(matchedBaseKey.Length + 2);
                    }

                    annotateItems.Add(new SmartAssistItem 
                    { 
                        Name = itemName,
                        Image = imageSource,
                        SymbolId = symbol.Id,
                        CategoryName = categoryName, // Always upgrade category to modern
                        ItemType = "Annotate",
                        CustomName = customName,
                        IsFavorite = true,
                        IsPreset = true,
                        Parameters = fav.Value
                    });
                }
            }


            // Update UI (Run on UI Thread)
            _view.Dispatcher.Invoke(() =>
            {
                _view.UpdateFamilyItems(familyItems);
                _view.UpdateAnnotateItems(annotateItems);
            });
        }

        public void LoadFavorites()
        {
            try
            {
                string appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                string folder = System.IO.Path.Combine(appDataPath, "Autodesk", "Revit", "Addins", "2026", "RevitFamilyBrowser");
                string favPath = System.IO.Path.Combine(folder, "favorites_v2.json");

                if (!System.IO.File.Exists(favPath)) return;

                string json = System.IO.File.ReadAllText(favPath);
                var data = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, string>>>(json);

                if (data != null)
                {
                    _view.Dispatcher.Invoke(() =>
                    {
                        var favItems = new List<SmartAssistItem>();
                        
                        // Wait for actual document fetch to do proper mapping and instances
                        // Since we just load the dictionary and pass it to Execute, we don't need to reconstruct SmartAssistItem objects here.
                        // Actually, Execute uses _view.GetFavorites() which fetches from FavoritesItems.
                        // Since we want `Execute` to know what's favorited, maybe we should just store the dictionary in _view, or populate `favItems` directly.
                        // The old LoadFavorites reconstructed empty SmartAssistItems just to populate `FavoritesItems` with keys.
                    });
                }
            }
            catch (Exception) { }
        }
        public string GetName()
        {
            return "Family Data Loader";
        }
        private List<string> LoadCategories(string path)
        {
            if (System.IO.File.Exists(path))
            {
                return System.IO.File.ReadAllLines(path)
                    .Where(l => !string.IsNullOrWhiteSpace(l))
                    .Select(l => l.Trim())
                    .ToList();
            }
            return new List<string>();
        }
    }
}
