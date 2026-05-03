using System.Windows.Controls;
using System.Windows.Media.Imaging;

namespace AI_Video_ToolKit.UI.Controls
{
    public partial class VideoPreviewControl : UserControl
    {
        public VideoPreviewControl()
        {
            InitializeComponent();
        }

        public void SetFrame(BitmapSource? image)
        {
            PreviewImage.Source = image;
        }
    }
}