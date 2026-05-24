// Файл: App.xaml.cs
using System;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using AI_Video_ToolKit.Infrastructure.Services;
using AI_Video_ToolKit.UI.Services;
using AI_Video_ToolKit.UI.ViewModels;
using CommunityToolkit.Mvvm.Messaging; //добавил

namespace AI_Video_ToolKit.UI
{
	public partial class App : Application
	{
		public static ServiceProvider ServiceProvider { get; private set; } = null!;

		protected override void OnStartup(StartupEventArgs e)
		{
			base.OnStartup(e);
			var services = new ServiceCollection();

			// ... существующие регистрации (FFmpegProcessService, FFprobeService, и т.д.)
			string ffmpegPath = Environment.GetEnvironmentVariable("FFMPEG") ?? @"C:\_Portable_\ffmpeg\bin\ffmpeg.exe";
			string ffprobePath = Environment.GetEnvironmentVariable("FFPROBE") ?? @"C:\_Portable_\ffmpeg\bin\ffprobe.exe";
			services.AddSingleton(new FFmpegProcessService(ffmpegPath, ffprobePath));
			services.AddSingleton<FFprobeService>();
			// Регистрируем Messenger как синглтон
			services.AddSingleton<IMessenger, WeakReferenceMessenger>();
			// Остальные регистрации...
			services.AddTransient<BufferedVideoPlayer>();
			services.AddTransient<FrameGrabber>();
			services.AddSingleton<PlaybackService>();

			services.AddSingleton<PlayerViewModel>();
			services.AddSingleton<MainViewModel>();
			services.AddSingleton<PlaylistViewModel>();
			services.AddSingleton<MarkersViewModel>();
			services.AddSingleton<ExportViewModel>();

			services.AddTransient<MainWindow>();
			ServiceProvider = services.BuildServiceProvider();
			var mainWindow = ServiceProvider.GetRequiredService<MainWindow>();
			mainWindow.Show();
		}
	}
}
