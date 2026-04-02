using System;
using System.Linq;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace RevitFamilyBrowser
{
    public class FamilyPlacementHandler : IExternalEventHandler
    {
        public ElementId SymbolId { get; set; }

        public void Execute(UIApplication app)
        {
            try
            {
                UIDocument uidoc = app.ActiveUIDocument;
                if (uidoc == null) return;
                Document doc = uidoc.Document;

                // v90: Force clear selection if SymbolId is null (used for Total Cancel via API)
                if (SymbolId == null)
                {
                    try {
                        uidoc.Selection.SetElementIds(new List<ElementId>());
                    } catch {}
                    return;
                }

                // v165: Type Duplication and Updater setup
                ElementId targetSymbolId = SymbolId;
                var paramUpdater = MyApplication.ParameterUpdater;
                Dictionary<string, string> activeParams = null;
                
                if (paramUpdater != null && paramUpdater.ActiveSymbolId == SymbolId)
                {
                    activeParams = paramUpdater.ActiveParameters;
                    if (activeParams != null && activeParams.Count > 0)
                    {
                        var typeParams = activeParams.Where(kvp => kvp.Key.EndsWith("|Type")).ToList();
                        if (typeParams.Count > 0)
                        {
                            using (Transaction t = new Transaction(doc, "Smart Assist: Create Preset Type"))
                            {
                                t.Start();
                                try
                                {
                                    ElementType baseType = doc.GetElement(SymbolId) as ElementType;
                                    if (baseType != null)
                                    {
                                        string source = string.Join(";", typeParams.Select(kvp => kvp.Key + "=" + kvp.Value));
                                        string hashStr = "";
                                        using (var md5 = System.Security.Cryptography.MD5.Create())
                                        {
                                            byte[] hash = md5.ComputeHash(System.Text.Encoding.UTF8.GetBytes(source));
                                            hashStr = BitConverter.ToString(hash).Replace("-", "").Substring(0, 8);
                                        }
                                        
                                        // v167: Use CustomName if available
                                        string customName = paramUpdater.ActivePresetName;
                                        string newTypeName = string.IsNullOrWhiteSpace(customName) ? $"{baseType.Name}_SA_{hashStr}" : customName;
                                        
                                        var existingType = new FilteredElementCollector(doc)
                                            .OfClass(baseType.GetType())
                                            .WhereElementIsElementType()
                                            .Cast<ElementType>()
                                            .FirstOrDefault(et => et.FamilyName == baseType.FamilyName && et.Name == newTypeName);
                                            
                                        if (existingType != null)
                                        {
                                            targetSymbolId = existingType.Id;
                                            
                                            // v168: Compound Structure Injection
                                            if (typeParams.Any(k => k.Key == "_SmartAssist_CompoundStructure|Type"))
                                            {
                                                string csData = typeParams.First(k => k.Key == "_SmartAssist_CompoundStructure|Type").Value;
                                                ApplyCompoundStructure(doc, existingType, csData);
                                            }
                                            
                                            // v167: If type exists (e.g. user updated preset Type params but kept the same name), apply parameters
                                            foreach (var kvp in typeParams)
                                            {
                                                if (kvp.Key == "_SmartAssist_CompoundStructure|Type") continue;
                                                
                                                string pName = kvp.Key.Replace("|Type", "");
                                                Parameter p = existingType.LookupParameter(pName);
                                                ParameterInjectUpdater.ApplyParameterValue(p, kvp.Value, doc);
                                            }
                                        }
                                        else
                                        {
                                            ElementType newType = baseType.Duplicate(newTypeName);
                                            
                                            // v168: Compound Structure Injection
                                            if (typeParams.Any(k => k.Key == "_SmartAssist_CompoundStructure|Type"))
                                            {
                                                string csData = typeParams.First(k => k.Key == "_SmartAssist_CompoundStructure|Type").Value;
                                                ApplyCompoundStructure(doc, newType, csData);
                                            }
                                            
                                            foreach (var kvp in typeParams)
                                            {
                                                if (kvp.Key == "_SmartAssist_CompoundStructure|Type") continue;
                                                
                                                string pName = kvp.Key.Replace("|Type", "");
                                                Parameter p = newType.LookupParameter(pName);
                                                ParameterInjectUpdater.ApplyParameterValue(p, kvp.Value, doc);
                                            }
                                            targetSymbolId = newType.Id;
                                        }
                                    }
                                }
                                catch { }
                                t.Commit();
                            }
                            paramUpdater.SetActivePreset(targetSymbolId, activeParams, paramUpdater.ActivePresetName);
                        }
                    }
                }

                // Task 2: Type Swapping logic
                var selectedIds = uidoc.Selection.GetElementIds();
                if (selectedIds != null && selectedIds.Count > 0)
                {
                    bool swapSuccess = false;
                    using (Transaction t = new Transaction(doc, "Smart Assist: Type Swap"))
                    {
                        t.Start();
                        foreach (ElementId id in selectedIds)
                        {
                            Element el = doc.GetElement(id);
                            if (el != null)
                            {
                                try {
                                    el.ChangeTypeId(targetSymbolId);
                                    
                                    // v165: Apply Instance parameters to the swapped element
                                    if (activeParams != null)
                                    {
                                        foreach (var kvp in activeParams)
                                        {
                                            if (kvp.Key.EndsWith("|Instance"))
                                            {
                                                string pName = kvp.Key.Replace("|Instance", "");
                                                Parameter p = el.LookupParameter(pName);
                                                ParameterInjectUpdater.ApplyParameterValue(p, kvp.Value, doc);
                                            }
                                        }
                                    }
                                    swapSuccess = true;
                                } catch {}
                            }
                        }
                        t.Commit();
                    }

                    if (swapSuccess) return;

                    try {
                        uidoc.Selection.SetElementIds(new List<ElementId>());
                    } catch {}
                }

                ElementType type = doc.GetElement(targetSymbolId) as ElementType;
                if (type == null) return;

                if (type is FamilySymbol symbol)
                {
                    if (!symbol.IsActive)
                    {
                        using (Transaction t = new Transaction(doc, "Activate Family Symbol"))
                        {
                            t.Start();
                            symbol.Activate();
                            doc.Regenerate();
                            t.Commit();
                        }
                    }
                }

                uidoc.PostRequestForElementTypePlacement(type);
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { }
            catch (Exception ex)
            {
                if (!ex.Message.Contains("aborted"))
                {
                    TaskDialog.Show("Error", "Failed to place family: " + ex.Message);
                }
            }
        }

        public string GetName() => "FamilyPlacementHandler";

        private void ApplyCompoundStructure(Document doc, ElementType type, string data)
        {
            if (string.IsNullOrWhiteSpace(data) || !(type is HostObjAttributes hostType)) return;

            string[] parts = data.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
            List<CompoundStructureLayer> newLayers = new List<CompoundStructureLayer>();

            foreach (string p in parts)
            {
                string[] vals = p.Split(',');
                if (vals.Length >= 5)
                {
                    int.TryParse(vals[0], out int func);
                    long.TryParse(vals[1], out long matId);
                    double.TryParse(vals[2], out double width);

                    try 
                    {
                        var layer = new CompoundStructureLayer(width, (MaterialFunctionAssignment)func, new ElementId(matId));
                        newLayers.Add(layer);
                    }
                    catch { }
                }
            }

            if (newLayers.Count > 0)
            {
                try
                {
                    var cs = CompoundStructure.CreateSimpleCompoundStructure(newLayers);
                    hostType.SetCompoundStructure(cs);
                }
                catch (Exception ex)
                {
                    TaskDialog.Show("Smart Assist", "복합 구조 주입 실패: " + ex.Message);
                }
            }
        }
    }
}
