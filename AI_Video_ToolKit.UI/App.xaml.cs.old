using System.IO;
using System.Windows;

namespace AI_Video_ToolKit.UI
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            var root = Directory.GetCurrentDirectory();
            EnsureDefaultFolders(root);
        }

        private static void EnsureDefaultFolders(string root)
        {
            string[] folders = { "Input", "Output", "Cut", "Frames", "Temp" };
            foreach (var f in folders)
                Directory.CreateDirectory(Path.Combine(root, f));
        }
    }
}