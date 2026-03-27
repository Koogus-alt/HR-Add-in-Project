using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;

namespace RevitFamilyBrowser
{
    public class ParameterInjectUpdater : IUpdater
    {
        static AddInId _appId;
        static UpdaterId _updaterId;
        
        // Dictionary mapping SymbolId to a Dictionary of ParamName -> ParamValue
        // This acts as our active preset payload.
        private Dictionary<ElementId, Dictionary<string, string>> _activePresets = new Dictionary<ElementId, Dictionary<string, string>>();

        public ParameterInjectUpdater(AddInId id)
        {
            _appId = id;
            _updaterId = new UpdaterId(_appId, new Guid("8A1B2C3D-4E5F-6A7B-8C9D-0E1F2A3B4C5D")); // Unique GUID
        }

        public void Execute(UpdaterData data)
        {
            if (_activePresets.Count == 0) return;

            Document doc = data.GetDocument();
            foreach (ElementId id in data.GetAddedElementIds())
            {
                Element elem = doc.GetElement(id);
                if (elem == null) continue;

                // Check if this newly added element matches any of our active presets
                ElementId typeId = elem.GetTypeId();
                if (typeId != null && _activePresets.TryGetValue(typeId, out var presetParams))
                {
                    Element typeElem = doc.GetElement(typeId);
                    foreach (var kvp in presetParams)
                    {
                        string paramName = kvp.Key;
                        bool isTypeParam = false;
                        
                        // v131: Handle suffixes
                        if (paramName.EndsWith("|Type"))
                        {
                            paramName = paramName.Substring(0, paramName.Length - 5);
                            isTypeParam = true;
                        }
                        else if (paramName.EndsWith("|Instance"))
                        {
                            paramName = paramName.Substring(0, paramName.Length - 9);
                        }

                            // v164: Skip Type parameters in Updater since the PlacementHandler now handles Type Duplication
                            if (isTypeParam) continue;

                            // Target correct element
                            Element target = isTypeParam ? typeElem : elem;
                            if (target == null) continue;

                            Parameter p = target.LookupParameter(paramName);
                            if (p != null && !p.IsReadOnly)
                            {
                                ApplyParameterValue(p, kvp.Value, doc);
                            }
                        }
                }
            }
        }

        // v164: Extracted helper method for reuse in FamilyPlacementHandler
        public static void ApplyParameterValue(Parameter p, string value, Document doc)
        {
            if (p == null || p.IsReadOnly) return;
            try
            {
                switch (p.StorageType)
                {
                    case StorageType.String:
                        p.Set(value);
                        break;
                    case StorageType.Double:
                        if (double.TryParse(value, out double dVal))
                        {
                            double feet = UnitUtils.ConvertToInternalUnits(dVal, GetLengthUnit(doc));
                            p.Set(feet);
                        }
                        break;
                    case StorageType.Integer:
                        if (int.TryParse(value, out int iVal))
                        {
                            p.Set(iVal);
                        }
                        break;
                    case StorageType.ElementId:
                        if (long.TryParse(value, out long idVal))
                        {
                            var newId = new ElementId(idVal);
                            p.Set(newId);
                        }
                        break;
                }
            }
            catch { }
        }

        private static ForgeTypeId GetLengthUnit(Document doc)
        {
            // Simple helper for 2024+ API
            #if REVIT2024_OR_LATER
                return doc.GetUnits().GetFormatOptions(SpecTypeId.Length).GetUnitTypeId();
            #else
                return UnitTypeId.Millimeters; // Fallback for this specific build if not defined
            #endif
        }

        public string GetAdditionalInformation() { return "Injects preset parameters into newly placed families."; }
        public ChangePriority GetChangePriority() { return ChangePriority.FloorsRoofsStructuralWalls; } // 'Elements' doesn't exist, use generic priority
        public UpdaterId GetUpdaterId() { return _updaterId; }
        public string GetUpdaterName() { return "Parameter Inject Updater"; }

        // v138: Track active preset for UI feedback
        public ElementId? ActiveSymbolId { get; private set; }
        public Dictionary<string, string> ActiveParameters { get; private set; }
        public string ActivePresetName { get; private set; } // v167: Track preset name

        public void SetActivePreset(ElementId symbolId, Dictionary<string, string> parameters, string presetName = null)
        {
            _activePresets.Clear();
            ActiveSymbolId = symbolId;
            ActiveParameters = parameters;
            ActivePresetName = presetName;

            if (parameters != null && parameters.Count > 0)
            {
                _activePresets[symbolId] = parameters;
            }
        }

        public void ClearActivePresets()
        {
            _activePresets.Clear();
            ActiveSymbolId = null;
            ActiveParameters = null;
            ActivePresetName = null;
        }
    }
}
