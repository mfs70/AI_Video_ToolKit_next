using System.Windows.Controls;
using System.Windows.Media.Imaging;

namespace AI_Video_ToolKit.UI.Controls
{
    /// <summary>
    /// Элемент управления для отображения видео и изображений.
    /// Поддерживает установку как видеокадров (BitmapSource), так и статических изображений (BitmapImage).
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
        public void SetFrame(BitmapSource? image)
        {
            PreviewImage.Source = image;
        }

        /// <summary>
        /// Установка статического изображения (например, из файла на диске)
        /// </summary>
        public void SetImage(BitmapImage bitmap)
        {
            PreviewImage.Source = bitmap;
        }
    }
}