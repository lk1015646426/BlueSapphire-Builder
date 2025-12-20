using System.Windows;

namespace BlueSapphire.Builder
{
    public partial class App : System.Windows.Application
    {
        public App()
        {
            // 🔥 关键修复：注册编码提供程序
            // 这样 .NET 8 才能识别 "GB2312" 这种老式编码，否则会报错
            System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
        }
    }
}