using System;
using System.Windows;
using System.Windows.Data;
using System.Windows.Controls;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace Heerim_SmartAssist
{
    public class DropdownOption
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public System.Windows.Media.ImageSource ThumbnailImage { get; set; }
        public override string ToString() => Name;
    }

    public class ParameterInfo
    {
        public bool IsBoolean { get; set; }
        public string GroupName { get; set; }
        public bool IsEnumerable { get; set; }
        public List<DropdownOption> AllowedValues { get; set; } = new List<DropdownOption>();
        public bool HasBrowseButton { get; set; }
        public bool IsTypeParameter { get; set; } // v131: Differentiation
        public bool IsCompoundStructure { get; set; } // v168: Compound Structure
        public string DefaultValue { get; set; } // v173: Safe defaults
        public bool IsColor { get; set; } // v184: Color mapping
        public bool IsFillPattern { get; set; }
    }

    public class ParameterEntry : System.ComponentModel.INotifyPropertyChanged
    {
        public string Name { get; set; }
        public bool IsBoolean { get; set; }
        public string GroupName { get; set; }
        public bool IsEnumerable { get; set; }
        public List<DropdownOption> AllowedValues { get; set; } = new List<DropdownOption>();
        public bool HasBrowseButton { get; set; }
        public bool IsTypeParameter { get; set; } // v131: Differentiation
        public bool IsCompoundStructure { get; set; } // v168: Compound Structure
        public bool IsColor { get; set; }
        public bool IsFillPattern { get; set; }
        
        public System.Windows.Media.SolidColorBrush ColorHexBrush
        {
            get
            {
                if (!IsColor || string.IsNullOrEmpty(Value)) return System.Windows.Media.Brushes.Black;
                if (int.TryParse(Value, out int colorInt))
                {
                    byte r = (byte)(colorInt % 256);
                    byte g = (byte)((colorInt / 256) % 256);
                    byte b = (byte)((colorInt / 65536) % 256);
                    return new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(r, g, b));
                }
                return System.Windows.Media.Brushes.Black;
            }
        }

        public string ColorName
        {
            get
            {
                if (!IsColor || string.IsNullOrEmpty(Value)) return "검은색";
                if (int.TryParse(Value, out int colorInt))
                {
                    byte r = (byte)(colorInt % 256);
                    byte g = (byte)((colorInt / 256) % 256);
                    byte b = (byte)((colorInt / 65536) % 256);
                    if (r == 0 && g == 0 && b == 0) return "검은색";
                    return $"RGB {r}-{g}-{b}";
                }
                return "검은색";
            }
        }

        private string _value;
        public string Value 
        { 
            get => _value; 
            set 
            {
                if (_value != value)
                {
                    _value = value;
                    OnPropertyChanged(nameof(Value));
                    if (IsBoolean) OnPropertyChanged(nameof(BoolValue));
                    if (IsColor)
                    {
                        OnPropertyChanged(nameof(ColorHexBrush));
                        OnPropertyChanged(nameof(ColorName));
                    }
                }
            }
        }

        public bool BoolValue 
        { 
            get => Value == "1" || (Value != null && Value.Equals("Yes", StringComparison.OrdinalIgnoreCase)); 
            set 
            {
                Value = value ? "1" : "0"; 
            }
        }

        public event System.ComponentModel.PropertyChangedEventHandler PropertyChanged;
        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(propertyName));
        }
    }

    public class MaterialEntry
    {
        public long Id { get; set; }
        public string Name { get; set; }
        public byte R { get; set; }
        public byte G { get; set; }
        public byte B { get; set; }
        public string ColorHex => $"#{R:X2}{G:X2}{B:X2}";
    }

    public class ParameterFetchHandler : IExternalEventHandler
    {
        public SmartAssistItem TargetItem { get; set; }
        public FamilyBrowserView View { get; set; }
        
        public static List<MaterialEntry> AvailableMaterials { get; set; } = new List<MaterialEntry>();
        public static List<DropdownOption> AvailableLevels { get; set; } = new List<DropdownOption>();
        public static List<DropdownOption> AvailablePhases { get; set; } = new List<DropdownOption>();
        public static List<Element> AvailableFillPatterns { get; set; } = new List<Element>();
        public static List<DropdownOption> AvailableFillPatternOptions { get; set; } = new List<DropdownOption>();

        private static System.Windows.Media.ImageSource GenerateFillPatternThumbnail(FillPatternElement fpElement)
        {
            var drawingGroup = new System.Windows.Media.DrawingGroup();
            using (var dc = drawingGroup.Open())
            {
                var rect = new System.Windows.Rect(0, 0, 48, 16);
                dc.DrawRectangle(System.Windows.Media.Brushes.White, null, rect);
                
                try
                {
                    var pattern = fpElement.GetFillPattern();
                    if (pattern != null)
                    {
                        if (pattern.IsSolidFill)
                        {
                            dc.DrawRectangle(System.Windows.Media.Brushes.Black, null, rect);
                        }
                        else
                        {
                            var grids = pattern.GetFillGrids();
                            var pen = new System.Windows.Media.Pen(System.Windows.Media.Brushes.Black, 0.5);
                            dc.PushClip(new System.Windows.Media.RectangleGeometry(rect));
                            
                            foreach (var grid in grids)
                            {
                                double angle = grid.Angle;
                                double offset = grid.Offset;
                                if (offset <= 0) offset = 1;
                                
                                double scale = 100;
                                double screenOffset = offset * scale;
                                if (screenOffset < 2) screenOffset = 2;
                                
                                if (Math.Abs(Math.Sin(angle)) > 0.01)
                                {
                                    for (double y = -16; y < 32; y += screenOffset)
                                        dc.DrawLine(pen, new System.Windows.Point(-10, y), new System.Windows.Point(58, y + 68/Math.Tan(angle)));
                                }
                                else
                                {
                                    for (double x = -10; x < 58; x += screenOffset)
                                        dc.DrawLine(pen, new System.Windows.Point(x, -16), new System.Windows.Point(x, 32));
                                }
                            }
                            dc.Pop();
                        }
                    }
                }
                catch {}
                
                dc.DrawRectangle(null, new System.Windows.Media.Pen(System.Windows.Media.Brushes.Gray, 1), rect);
            }
            return new System.Windows.Media.DrawingImage(drawingGroup);
        }

        private bool IsYesNoParameter(Parameter p)
        {
            if (p.StorageType != StorageType.Integer) return false;
            try
            {
                return p.Definition.GetDataType() == SpecTypeId.Boolean.YesNo;
            }
            catch
            {
                return false;
            }
        }

        public void Execute(UIApplication app)
        {
            try 
            {
                Document doc = app.ActiveUIDocument?.Document;
                var extendedParamInfo = new Dictionary<string, ParameterInfo>();

                // v155: Selection Optimization
                if (doc != null && TargetItem?.SymbolId != null)
                {
                    var selection = app.ActiveUIDocument.Selection.GetElementIds();
                    foreach (var selId in selection)
                    {
                        Element selElem = doc.GetElement(selId);
                        if (selElem != null && selElem.GetTypeId() == TargetItem.SymbolId)
                        {
                            CollectInstanceParams(selElem, extendedParamInfo);
                            break;
                        }
                    }
                }

                // Collect Type parameters
                if (doc != null && TargetItem?.SymbolId != null)
                {
                    // v168: Cache materials for Compound Structure Edit
                    if (AvailableMaterials.Count == 0)
                    {
                        AvailableMaterials = new FilteredElementCollector(doc)
                            .OfClass(typeof(Material))
                            .Cast<Material>()
                            .OrderBy(m => m.Name)
                            .Select(m => {
                                byte r = 200, g = 200, b = 200;
                                try {
                                    if (m.Color != null && m.Color.IsValid)
                                    {
                                        r = m.Color.Red;
                                        g = m.Color.Green;
                                        b = m.Color.Blue;
                                    }
                                } catch {}
                                return new MaterialEntry { 
                                    Id = m.Id.Value, 
                                    Name = m.Name,
                                    R = r,
                                    G = g,
                                    B = b
                                };
                            })
                            .ToList();
                    }

                    if (AvailableLevels.Count == 0)
                    {
                        AvailableLevels = new FilteredElementCollector(doc)
                            .OfClass(typeof(Level))
                            .Cast<Level>()
                            .OrderBy(l => l.Elevation)
                            .Select(l => new DropdownOption { Id = l.Id.Value.ToString(), Name = l.Name })
                            .ToList();
                    }
                    if (AvailablePhases.Count == 0)
                    {
                        AvailablePhases = new FilteredElementCollector(doc)
                            .OfClass(typeof(Phase))
                            .Cast<Phase>()
                            .Select(p => new DropdownOption { Id = p.Id.Value.ToString(), Name = p.Name })
                            .ToList();
                    }
                    if (AvailableFillPatterns.Count == 0)
                    {
                        AvailableFillPatterns = new FilteredElementCollector(doc)
                            .OfClass(typeof(FillPatternElement))
                            .ToElements()
                            .ToList();

                        AvailableFillPatternOptions = new List<DropdownOption>();
                        AvailableFillPatternOptions.Add(new DropdownOption { Id = "-1", Name = "<없음>" });
                        foreach (FillPatternElement fpe in AvailableFillPatterns)
                        {
                            AvailableFillPatternOptions.Add(new DropdownOption
                            {
                                Id = fpe.Id.ToString(),
                                Name = fpe.Name,
                                ThumbnailImage = GenerateFillPatternThumbnail(fpe)
                            });
                        }
                    }

                    Element symbol = doc.GetElement(TargetItem.SymbolId);
                    if (symbol != null)
                    {
                        if (!extendedParamInfo.Keys.Any(k => k.EndsWith("|Type")))
                        {
                            foreach (Parameter p in symbol.Parameters)
                            {
                                try
                                {
                                    if (!p.IsReadOnly)
                                    {
                                        string group = "기타";
                                        string enumStr = "";
                                        try { enumStr = p.Definition.GetGroupTypeId().TypeId.ToUpper(); } catch { }
                                        try { group = LabelUtils.GetLabelForGroup(p.Definition.GetGroupTypeId()); } catch { }
                                        
                                        // v179: Strict whitelist without GEOMETRY (Added MEP)
                                        if (!(enumStr.Contains("CONSTRUCTION") || enumStr.Contains("GRAPHICS") || enumStr.Contains("DIMENSIONS") || enumStr.Contains("MECHANICAL") || enumStr.Contains("PLUMBING") || enumStr.Contains("ROUTING") ||
                                              group.Contains("시공") || group.Contains("그래픽") || group.Contains("치수") || group.Contains("기계") || group.Contains("배관") || group.Contains("라우팅")))
                                            continue;

                                        bool isBool = IsYesNoParameter(p);
                                        var info = new ParameterInfo { IsBoolean = isBool, GroupName = group, IsTypeParameter = true };
                                        
                                        // Capture exact default model value
                                        if (p.StorageType == StorageType.Double) info.DefaultValue = p.AsValueString() ?? p.AsDouble().ToString();
                                        else if (p.StorageType == StorageType.String) info.DefaultValue = p.AsString() ?? "";
                                        else if (p.StorageType == StorageType.Integer)
                                        {
                                            info.DefaultValue = isBool ? p.AsInteger().ToString() : p.AsInteger().ToString();
                                            
                                            if (!isBool && (p.Definition.Name.Contains("색상") || p.Definition.Name.Contains("Color") || p.Definition.Name.Contains("색")))
                                            {
                                                info.IsColor = true;
                                            }

                                            // Handle BuiltIn Wall Enums
                                            if (p.Definition.Name == "기능" || p.Definition.Name == "Function")
                                            {
                                                info.IsEnumerable = true;
                                                info.AllowedValues = new List<DropdownOption> {
                                                    new DropdownOption { Id="0", Name="내부" }, new DropdownOption { Id="1", Name="외부" },
                                                    new DropdownOption { Id="2", Name="기초" }, new DropdownOption { Id="3", Name="옹벽" },
                                                    new DropdownOption { Id="4", Name="밑면" }, new DropdownOption { Id="5", Name="코어 샤프트" }
                                                };
                                            }
                                            else if (p.Definition.Name == "끝 마무리" || p.Definition.Name == "Wrapping at Ends")
                                            {
                                                info.IsEnumerable = true;
                                                info.AllowedValues = new List<DropdownOption> {
                                                    new DropdownOption { Id="0", Name="없음" }, new DropdownOption { Id="1", Name="외부" }, new DropdownOption { Id="2", Name="내부" }
                                                };
                                            }
                                            else if (p.Definition.Name == "인서트 부위 마무리" || p.Definition.Name == "Wrapping at Inserts")
                                            {
                                                info.IsEnumerable = true;
                                                info.AllowedValues = new List<DropdownOption> {
                                                    new DropdownOption { Id="0", Name="마무리하지 않음" }, new DropdownOption { Id="1", Name="외부" },
                                                    new DropdownOption { Id="2", Name="내부" }, new DropdownOption { Id="3", Name="둘 다" }
                                                };
                                            }
                                        }

                                        if (p.StorageType == StorageType.ElementId)
                                        {
                                            if (enumStr.Contains("MATERIALS") || group.Contains("재질") || group.Contains("재료") || group.Contains("마감") || enumStr.Contains("CONSTRUCTION") || group.Contains("시공"))
                                            {
                                                info.IsEnumerable = true;
                                                var mats = AvailableMaterials.Select(m => new DropdownOption { Id = m.Id.ToString(), Name = m.Name }).ToList();
                                                mats.Insert(0, new DropdownOption { Id = "-1", Name = "<카테고리별>" });
                                                info.AllowedValues = mats;
                                            }
                                            else if (enumStr.Contains("GRAPHICS") || group.Contains("그래픽"))
                                            {
                                                info.IsEnumerable = true;
                                                info.IsFillPattern = true;
                                                info.AllowedValues = AvailableFillPatternOptions;
                                            }
                                            else
                                            {
                                                continue; // Hide all raw, unmappable ElementIds
                                            }
                                            if (p.AsElementId() != ElementId.InvalidElementId) info.DefaultValue = p.AsElementId().Value.ToString();
                                        }

                                        extendedParamInfo[p.Definition.Name + "|Type"] = info;
                                    }
                                }
                                catch { }
                            }
                        }

                        // v168: Serialize Compound Structure for Walls/Floors AFTER regular param fetch
                        if (symbol is HostObjAttributes hostAttr)
                        {
                            var cs = hostAttr.GetCompoundStructure();
                            if (cs != null)
                            {
                                string defVal = "";
                                if (TargetItem.Parameters == null || !TargetItem.Parameters.ContainsKey("_SmartAssist_CompoundStructure|Type"))
                                {
                                    var layers = cs.GetLayers();
                                    List<string> layerStrings = new List<string>();
                                    foreach (var layer in layers)
                                    {
                                        string lStr = $"{(int)layer.Function},{layer.MaterialId.Value},{layer.Width},{cs.ParticipatesInWrapping(layer.LayerId)},{cs.StructuralMaterialIndex == layer.LayerId}";
                                        layerStrings.Add(lStr);
                                    }
                                    defVal = string.Join(";", layerStrings);
                                }
                                
                                extendedParamInfo["_SmartAssist_CompoundStructure|Type"] = new ParameterInfo { 
                                    IsCompoundStructure = true, 
                                    GroupName = "시공", 
                                    IsTypeParameter = true,
                                    DefaultValue = defVal
                                };
                            }
                        }

                        // Fallback for instance parameters
                        if (!extendedParamInfo.Keys.Any(k => k.EndsWith("|Instance")))
                        {
                            using (Transaction t = new Transaction(doc, "Temp Instance"))
                            {
                                t.Start();
                                try
                                {
                                    Level anyLevel = new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>().FirstOrDefault();
                                    Element tempInst = null;

                                    if (symbol is FamilySymbol fs2)
                                    {
                                        if (!fs2.IsActive) fs2.Activate();
                                        try {
                                            if (anyLevel != null) tempInst = doc.Create.NewFamilyInstance(XYZ.Zero, fs2, anyLevel, Autodesk.Revit.DB.Structure.StructuralType.NonStructural);
                                        } catch { }

                                        if (tempInst == null) {
                                            try { tempInst = doc.Create.NewFamilyInstance(XYZ.Zero, fs2, Autodesk.Revit.DB.Structure.StructuralType.NonStructural); } catch { }
                                        }
                                    }
                                    else if (anyLevel != null)
                                    {
                                        // System Family Creation Trick
                                        try {
                                            if (symbol is WallType) {
                                                Curve wallLine = Line.CreateBound(XYZ.Zero, new XYZ(10, 0, 0));
                                                tempInst = Wall.Create(doc, wallLine, symbol.Id, anyLevel.Id, 10, 0, false, false);
                                            }
                                            else if (symbol is FloorType) {
                                                var curveLoop = new CurveLoop();
                                                curveLoop.Append(Line.CreateBound(XYZ.Zero, new XYZ(10, 0, 0)));
                                                curveLoop.Append(Line.CreateBound(new XYZ(10, 0, 0), new XYZ(10, 10, 0)));
                                                curveLoop.Append(Line.CreateBound(new XYZ(10, 10, 0), new XYZ(0, 10, 0)));
                                                curveLoop.Append(Line.CreateBound(new XYZ(0, 10, 0), XYZ.Zero));
                                                tempInst = Floor.Create(doc, new List<CurveLoop>{curveLoop}, symbol.Id, anyLevel.Id);
                                            }
                                            // Using explicit namespaces for MEP to prevent build failure if missing usings
                                            else if (symbol.Category != null && symbol.Category.Id.Value == (long)BuiltInCategory.OST_PipeCurves) {
                                                var sysType = new FilteredElementCollector(doc).OfClass(typeof(Autodesk.Revit.DB.Plumbing.PipingSystemType)).FirstElementId();
                                                tempInst = Autodesk.Revit.DB.Plumbing.Pipe.Create(doc, sysType, symbol.Id, anyLevel.Id, XYZ.Zero, new XYZ(10, 0, 0));
                                            }
                                            else if (symbol.Category != null && symbol.Category.Id.Value == (long)BuiltInCategory.OST_DuctCurves) {
                                                var sysType = new FilteredElementCollector(doc).OfClass(typeof(Autodesk.Revit.DB.Mechanical.MechanicalSystemType)).FirstElementId();
                                                tempInst = Autodesk.Revit.DB.Mechanical.Duct.Create(doc, sysType, symbol.Id, anyLevel.Id, XYZ.Zero, new XYZ(10, 0, 0));
                                            }
                                            else if (symbol.Category != null && symbol.Category.Id.Value == (long)BuiltInCategory.OST_CableTray) {
                                                tempInst = Autodesk.Revit.DB.Electrical.CableTray.Create(doc, symbol.Id, XYZ.Zero, new XYZ(10, 0, 0), anyLevel.Id);
                                            }
                                            else if (symbol.Category != null && symbol.Category.Id.Value == (long)BuiltInCategory.OST_Conduit) {
                                                tempInst = Autodesk.Revit.DB.Electrical.Conduit.Create(doc, symbol.Id, XYZ.Zero, new XYZ(10, 0, 0), anyLevel.Id);
                                            }
                                        } catch { }
                                    }

                                    if (tempInst != null) CollectInstanceParams(tempInst, extendedParamInfo);
                                }
                                catch { }
                                t.RollBack();
                            }

                            // v163: System Family Fallback
                            if (!extendedParamInfo.Keys.Any(k => k.EndsWith("|Instance")))
                            {
                                try
                                {
                                    var anyInst = new FilteredElementCollector(doc)
                                        .WhereElementIsNotElementType()
                                        .FirstOrDefault(e => e.GetTypeId() == symbol.Id);
                                        
                                    if (anyInst == null)
                                    {
                                        if (symbol is WallType)
                                            anyInst = new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_Walls).WhereElementIsNotElementType().FirstOrDefault();
                                        else if (symbol is FloorType)
                                            anyInst = new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_Floors).WhereElementIsNotElementType().FirstOrDefault();
                                        else if (symbol is RoofType)
                                            anyInst = new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_Roofs).WhereElementIsNotElementType().FirstOrDefault();
                                        else if (symbol is CeilingType)
                                            anyInst = new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_Ceilings).WhereElementIsNotElementType().FirstOrDefault();
                                        else if (symbol.Category != null)
                                        {
                                            anyInst = new FilteredElementCollector(doc)
                                                .OfCategoryId(symbol.Category.Id)
                                                .WhereElementIsNotElementType()
                                                .FirstOrDefault();
                                        }
                                    }
                                    
                                    if (anyInst != null)
                                    {
                                        CollectInstanceParams(anyInst, extendedParamInfo);
                                    }
                                }
                                catch { }
                            }
                        }
                    }
                }

                // Call UI
                View?.Dispatcher.BeginInvoke(new Action(() => View.ShowParameterEditWindow(TargetItem, extendedParamInfo)));
            }
            catch (Exception ex)
            {
                MessageBox.Show("오류 발생 (Parameter Fetch):\n" + ex.ToString(), "Smart Assist Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void CollectInstanceParams(Element inst, Dictionary<string, ParameterInfo> dict)
        {
            foreach (Parameter p in inst.Parameters)
            {
                try
                {
                    if (!p.IsReadOnly)
                    {
                        string group = "기타";
                        string enumStr = "";
                        try { enumStr = p.Definition.GetGroupTypeId().TypeId.ToUpper(); } catch {}
                        try { group = LabelUtils.GetLabelForGroup(p.Definition.GetGroupTypeId()); } catch {}
                        
                        // v178: Bulletproof whitelist using Enum strings + Korean fallbacks (Added MEP)
                        if (!(enumStr.Contains("CONSTRAINTS") || enumStr.Contains("IDENTITY_DATA") || enumStr.Contains("MATERIALS") || enumStr.Contains("STRUCTURAL") || enumStr.Contains("DATA") || enumStr.Contains("MECHANICAL") || enumStr.Contains("PLUMBING") ||
                              group.Contains("구속조건") || group.Contains("데이터") || group.Contains("식별") || group.Contains("재료") || group.Contains("마감재") || group.Contains("구조") || group.Contains("기계") || group.Contains("배관")))
                            continue;

                        bool isBool = IsYesNoParameter(p);
                        
                        var info = new ParameterInfo { IsBoolean = isBool, GroupName = group, IsTypeParameter = false };

                        // Capture exact default model value
                        if (p.StorageType == StorageType.Double) info.DefaultValue = p.AsValueString() ?? p.AsDouble().ToString();
                        else if (p.StorageType == StorageType.String) info.DefaultValue = p.AsString() ?? "";
                        else if (p.StorageType == StorageType.Integer) info.DefaultValue = isBool ? p.AsInteger().ToString() : (p.AsValueString() ?? p.AsInteger().ToString());

                        if (p.StorageType == StorageType.ElementId)
                        {
                            if (enumStr.Contains("CONSTRAINTS") || group.Contains("구속조건") || p.Definition.Name.Contains("레벨") || p.Definition.Name.Contains("Level") || p.Definition.Name.Contains("연결"))
                            {
                                info.IsEnumerable = true;
                                info.AllowedValues = AvailableLevels;
                            }
                            else if (p.Definition.Name.Contains("단계") || p.Definition.Name.Contains("Phase"))
                            {
                                info.IsEnumerable = true;
                                info.AllowedValues = AvailablePhases;
                            }
                            else if (enumStr.Contains("MATERIALS") || enumStr.Contains("STRUCTURAL") || group.Contains("재질") || group.Contains("재료") || group.Contains("마감") || group.Contains("구조"))
                            {
                                info.IsEnumerable = true;
                                var mats = AvailableMaterials.Select(m => new DropdownOption { Id = m.Id.ToString(), Name = m.Name }).ToList();
                                mats.Insert(0, new DropdownOption { Id = "-1", Name = "<카테고리별>" });
                                info.AllowedValues = mats;
                            }
                            else
                            {
                                continue; // Hide unmappable ElementIds
                            }
                            
                            if (p.AsElementId() != ElementId.InvalidElementId) info.DefaultValue = p.AsElementId().Value.ToString();
                        }

                        dict[p.Definition.Name + "|Instance"] = info;
                    }
                }
                catch
                {
                    // Ignore parameters that throw exceptions when accessed
                }
            }
        }

        public string GetName() => "ParameterFetchHandler";
    }

    public partial class ParameterEditWindow : Window
    {
        private SmartAssistItem _item;
        private Dictionary<string, ParameterInfo> _fullAvailableParams;
        public ObservableCollection<ParameterEntry> Entries { get; set; }
        private List<ParameterEntry> _typeEntries;
        public string CustomName { get; private set; }
        public Dictionary<string, string> SavedParameters { get; private set; } // v158: Isolated storage

        public ParameterEditWindow(SmartAssistItem item, Dictionary<string, ParameterInfo> availableParams, int presetIndex = 0)
        {
            InitializeComponent();
            _item = item;
            _fullAvailableParams = availableParams;
            
            ItemNameText.Text = item.Name;
            ItemCategoryText.Text = item.CategoryName;
            ItemPreviewImage.Source = item.Image;

            if (!item.IsPreset || string.IsNullOrEmpty(item.CustomName))
            {
                PresetNameTextBox.Text = $"{item.Name}_{presetIndex + 1:D3}";
            }
            else
            {
                PresetNameTextBox.Text = item.CustomName;
            }
            
            Entries = new ObservableCollection<ParameterEntry>();
            LoadParameterEntries(); // Load both Instance and Type
            
            this.DataContext = this;
        }

        private void LoadParameterEntries()
        {
            Entries.Clear();
            _typeEntries = new List<ParameterEntry>();
            
            foreach (var kvp in _fullAvailableParams)
            {
                bool isTypeParam = kvp.Key.EndsWith("|Type");
                string suffix = isTypeParam ? "|Type" : "|Instance";
                string displayName = kvp.Key.Substring(0, kvp.Key.Length - suffix.Length);
                
                if (displayName == "_SmartAssist_CompoundStructure") displayName = "구조";
                else if (displayName == "구조" || displayName == "Structure") continue; // Obsolete

                string val = "";
                
                // Try to get value from item.Parameters if it exists
                if (_item.Parameters != null && _item.Parameters.TryGetValue(kvp.Key, out string savedVal))
                {
                    val = savedVal;
                }
                else if (!string.IsNullOrEmpty(kvp.Value.DefaultValue))
                {
                    val = kvp.Value.DefaultValue;
                }
                else
                {
                    val = kvp.Value.IsBoolean ? "0" : "";
                }

                var entry = new ParameterEntry
                {
                    Name = displayName == "_SmartAssist_CompoundStructure" ? "복합 구조" : displayName,
                    Value = val,
                    IsBoolean = kvp.Value.IsBoolean,
                    GroupName = kvp.Value.GroupName,
                    IsEnumerable = kvp.Value.IsEnumerable,
                    AllowedValues = kvp.Value.AllowedValues,
                    IsTypeParameter = isTypeParam,
                    IsCompoundStructure = kvp.Value.IsCompoundStructure,
                    IsColor = kvp.Value.IsColor,
                    IsFillPattern = kvp.Value.IsFillPattern
                };

                if (isTypeParam)
                {
                    _typeEntries.Add(entry);
                }
                else
                {
                    Entries.Add(entry);
                }
            }

            // Sync CVS
            var cvs = this.Resources["ParametersCVS"] as CollectionViewSource;
            if (cvs != null) cvs.View?.Refresh();
        }

        private void OnTypeEditButtonClick(object sender, RoutedEventArgs e)
        {
            CustomName = PresetNameTextBox.Text?.Trim() ?? "";
            var typeName = string.IsNullOrEmpty(CustomName) ? _item.Name : CustomName;
            var dialog = new TypeParameterEditWindow(_item.CategoryName, typeName, _typeEntries);
            dialog.Owner = this;
            if (dialog.ShowDialog() == true)
            {
                _typeEntries = new List<ParameterEntry>(dialog.Entries);
            }
        }

        private void OnSaveClick(object sender, RoutedEventArgs e)
        {
            try
            {
                // v131: Update the item's parameters dictionary.
                // We must be careful not to lose parameters from the OTHER mode (Instance vs Type)
                // because LoadParameterEntries only shows one at a time.
                
                CustomName = PresetNameTextBox.Text?.Trim() ?? "";
                
                // v158: Store to SavedParameters instead of modifying item directly if it's a master
                SavedParameters = new Dictionary<string, string>();
                // We should also include existing params that aren't being edited right now
                if (_item.Parameters != null)
                {
                    foreach (var kvp in _item.Parameters)
                    {
                        SavedParameters[kvp.Key] = kvp.Value;
                    }
                }

                // We strictly map only parameters that actively differ from the master's default setting
                // Save diff from Instance entries
                foreach (var entry in Entries)
                {
                    string internalKey = entry.Name + "|Instance";
                    if (!string.IsNullOrWhiteSpace(entry.Value))
                    {
                        string savedVal = entry.Value.Trim();
                        if (_fullAvailableParams.TryGetValue(internalKey, out var info))
                        {
                            if (savedVal != info.DefaultValue)
                            {
                                SavedParameters[internalKey] = savedVal;
                            }
                        }
                    }
                }
                
                // Save diff from Type entries
                foreach (var entry in _typeEntries)
                {
                    string internalKey = entry.Name + "|Type";
                    if (entry.Name == "복합 구조") internalKey = "_SmartAssist_CompoundStructure|Type";
                    
                    if (!string.IsNullOrWhiteSpace(entry.Value))
                    {
                        string savedVal = entry.Value.Trim();
                        if (_fullAvailableParams.TryGetValue(internalKey, out var info))
                        {
                            if (savedVal != info.DefaultValue)
                            {
                                SavedParameters[internalKey] = savedVal;
                            }
                        }
                    }
                }

                if (_item.IsPreset)
                {
                    // If already a preset, we CAN update it directly
                    _item.CustomName = CustomName;
                    _item.Parameters = SavedParameters;
                    _item.NotifyParametersChanged();
                }
                // If it's a master, we leave _item untouched and let FamilyBrowserView handle cloning
                
                this.DialogResult = true;
                this.Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"저장 실패: {ex.Message}");
            }
        }

        private void OnCancelClick(object sender, RoutedEventArgs e)
        {
            this.DialogResult = false;
            this.Close();
        }

        private void Window_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == System.Windows.Input.Key.Escape) OnCancelClick(null, null);
        }

        // v142: Select all text when focus gained for easier renaming
        private void OnPresetNameTextBoxGotFocus(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.TextBox tb)
            {
                // v144: Use Dispatcher to ensure selection isn't cleared by the click that gave focus
                Dispatcher.BeginInvoke(new Action(() => tb.SelectAll()), System.Windows.Threading.DispatcherPriority.Input);
            }
        }

        // v157: Ensure SelectAll on every click, even if already focused
        private void OnPresetNameTextBoxPreviewMouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (sender is System.Windows.Controls.TextBox tb)
            {
                if (!tb.IsFocused)
                {
                    tb.Focus();
                    e.Handled = true; // Prevent default mouse down logic that clears selection
                }
                else
                {
                    // Already focused? Force select all
                    tb.SelectAll();
                    e.Handled = true;
                }
            }
        }

        private void OnColorButtonClick(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.Button btn && btn.Tag is ParameterEntry entry)
            {
                int currentColor = 0;
                int.TryParse(entry.Value, out currentColor);

                byte r = (byte)(currentColor % 256);
                byte g = (byte)((currentColor / 256) % 256);
                byte b = (byte)((currentColor / 65536) % 256);

                using (var colorDialog = new System.Windows.Forms.ColorDialog())
                {
                    colorDialog.Color = System.Drawing.Color.FromArgb(r, g, b);
                    if (colorDialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                    {
                        var c = colorDialog.Color;
                        int newColor = c.R + (c.G << 8) + (c.B << 16);
                        entry.Value = newColor.ToString();
                    }
                }
            }
        }
    }
}
