// Файл: App.xaml.cs
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using AI_Video_ToolKit.Infrastructure.Services;
using AI_Video_ToolKit.UI.Services;
using AI_Video_ToolKit.UI.ViewModels;

namespace AI_Video_ToolKit.UI
{
    public partial class App : Application
    {
        public static ServiceProvider ServiceProvider { get; private set; } = null!;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            var services = new ServiceCollection();
            string ffmpegPath = @"C:\_Portable_\ffmpeg\bin\ffmpeg.exe";
            string ffprobePath = @"C:\_Portable_\ffmpeg\bin\ffprobe.exe";
            services.AddSingleton(new FFmpegProcessService(ffmpegPath, ffprobePath));
            services.AddSingleton<FFprobeService>();
            services.AddTransient<BufferedVideoPlayer>();
            services.AddTransient<FrameGrabber>();
            services.AddSingleton<PlaybackService>();
            services.AddSingleton<MainViewModel>();
            services.AddTransient<MainWindow>();
            ServiceProvider = services.BuildServiceProvider();
            var mainWindow = ServiceProvider.GetRequiredService<MainWindow>();
            mainWindow.Show();
        }
    }
}