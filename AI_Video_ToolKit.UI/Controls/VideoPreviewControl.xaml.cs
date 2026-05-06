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
        /// Установка кадра видео (BitmapSource).
        /// Используется для динамического видео, получаемого из FFmpeg.
        /// </summary>
        /// <param name="image">Изображение кадра (может быть null для очистки)</param>
        public void SetFrame(BitmapSource? image)
        {
            PreviewImage.Source = image;
        }

        /// <summary>
        /// Установка статического изображения (например, из файла на диске).
        /// BitmapImage является подклассом BitmapSource, поэтому передаётся в тот же Image.Source.
        /// </summary>
        /// <param name="bitmap">Объект BitmapImage, представляющий картинку</param>
        public void SetImage(BitmapImage bitmap)
        {
            PreviewImage.Source = bitmap;
        }
    }
}