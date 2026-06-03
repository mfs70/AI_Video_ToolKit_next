using System.Windows;
using System.Windows.Controls;

namespace AI_Video_ToolKit.UI.Dialogs
{
    public partial class FrameBuildOptionsDialog : Window
    {
        public FrameBuildOptionsDialog(string folderPath)
        {
            FolderPath = folderPath;
            InitializeComponent();
        }

        public string FolderPath { get; }

        public string SelectedQuality
            => (QualityCombo.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Medium";

        public string SelectedFormat
            => (FormatCombo.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? ".mp4";

        private void Build_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
        }
    }
}
