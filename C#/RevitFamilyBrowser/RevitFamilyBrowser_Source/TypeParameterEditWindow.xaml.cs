using System;
using System.Windows;
using System.Windows.Data;
using System.Collections.ObjectModel;
using System.Collections.Generic;

namespace RevitFamilyBrowser
{
    public partial class TypeParameterEditWindow : Window
    {
        public ObservableCollection<ParameterEntry> Entries { get; set; }

        public TypeParameterEditWindow(string familyName, string typeName, IEnumerable<ParameterEntry> typeEntries)
        {
            InitializeComponent();
            
            FamilyNameText.Text = familyName;
            TypeNameText.Text = typeName;

            Entries = new ObservableCollection<ParameterEntry>();
            foreach (var entry in typeEntries)
            {
                // Clone the entries so that canceling doesn't affect the original lists until "OK" is pressed
                Entries.Add(new ParameterEntry
                {
                    Name = entry.Name,
                    IsBoolean = entry.IsBoolean,
                    GroupName = entry.GroupName,
                    IsEnumerable = entry.IsEnumerable,
                    AllowedValues = entry.AllowedValues,
                    HasBrowseButton = entry.HasBrowseButton,
                    IsTypeParameter = entry.IsTypeParameter,
                    IsCompoundStructure = entry.IsCompoundStructure,
                    IsColor = entry.IsColor,
                    IsFillPattern = entry.IsFillPattern,
                    Value = entry.Value
                });
            }
            
            this.DataContext = this;
            var cvs = this.Resources["ParametersCVS"] as CollectionViewSource;
            if (cvs != null) cvs.View?.Refresh();
        }

        private void OnOkClick(object sender, RoutedEventArgs e)
        {
            this.DialogResult = true;
            this.Close();
        }

        private void OnCancelClick(object sender, RoutedEventArgs e)
        {
            this.DialogResult = false;
            this.Close();
        }

        private void Window_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == System.Windows.Input.Key.Escape)
            {
                OnCancelClick(null, null);
            }
        }

        private void OnStructureEditClick(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.Button btn && btn.DataContext is ParameterEntry entry)
            {
                var editWin = new CompoundStructureEditWindow(entry.Value, ParameterFetchHandler.AvailableMaterials);
                editWin.Owner = this;
                if (editWin.ShowDialog() == true)
                {
                    entry.Value = editWin.SerializedStructure;
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
