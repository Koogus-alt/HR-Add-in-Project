using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Input;

namespace Heerim_HIS
{
    public partial class SmartFilterView : Window
    {
        private FilterEngine _engine;
        private List<Element> _currentResults = new List<Element>();

        public SmartFilterView(UIApplication uiApp)
        {
            InitializeComponent();
            _engine = new FilterEngine(uiApp);
        }

        private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            this.DragMove();
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        private void Search_Click(object sender, RoutedEventArgs e)
        {
            PerformSearch();
        }

        private void SearchBox_KeyUp(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == System.Windows.Input.Key.Enter) PerformSearch();
        }

        private void PerformSearch()
        {
            string text = SearchBox.Text.Trim();
            _currentResults = _engine.SearchElementsByText(text);
            ResultListBox.ItemsSource = _currentResults;
        }

        private void SelectAll_Click(object sender, RoutedEventArgs e)
        {
            var ids = _currentResults.Select(x => x.Id).ToList();
            _engine.SelectElements(ids);
        }
    }
}
