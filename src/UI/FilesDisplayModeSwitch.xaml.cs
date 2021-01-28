using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace SourceGit.UI {

    /// <summary>
    ///     Interaction logic for FilesDisplayModeSwitch.xaml
    /// </summary>
    public partial class FilesDisplayModeSwitch : UserControl {

        public static readonly DependencyProperty ModeProperty =
            DependencyProperty.Register(
                "Mode", 
                typeof(Preference.FilesDisplayMode), 
                typeof(FilesDisplayModeSwitch), 
                new PropertyMetadata(Preference.FilesDisplayMode.Grid));

        public Preference.FilesDisplayMode Mode {
            get { return (Preference.FilesDisplayMode)GetValue(ModeProperty); }
            set { SetValue(ModeProperty, value); }
        }

        public FilesDisplayModeSwitch() {
            InitializeComponent();
        }

        private void OpenModeSelector(object sender, RoutedEventArgs e) {
            selector.PlacementTarget = sender as Button;
            selector.IsOpen = true;
            e.Handled = true;
        }

        private void UseGrid(object sender, RoutedEventArgs e) {
            Mode = Preference.FilesDisplayMode.Grid;
        }

        private void UseList(object sender, RoutedEventArgs e) {
            Mode = Preference.FilesDisplayMode.List;
        }

        private void UseTree(object sender, RoutedEventArgs e) {
            Mode = Preference.FilesDisplayMode.Tree;
        }
    }
}