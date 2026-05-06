using System.Windows.Controls;
using System.Windows.Media.Imaging;

namespace AI_Video_ToolKit.UI.Controls
{
    /// <summary>
    /// Элемент управления для отображения видео и изображений
    /// </summary>
    public partial class VideoPreviewControl : UserControl
    {
        public VideoPreviewControl()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Установка кадра видео (BitmapSource)
        /// </summary>
        /// <param name="image">Изображение для отображения</param>
        public void SetFrame(BitmapSource? image)
        {
            PreviewImage.Source = image;
        }

        /// <summary>
        /// Установка статического изображения (например, из файла)
        /// </summary>
        /// <param name="bitmap">BitmapImage для отображения</param>
        public void SetImage(BitmapImage bitmap)
        {
            // BitmapImage наследуется от BitmapSource, поэтому можно передать в SetFrame
            SetFrame(bitmap);
        }
    }
}