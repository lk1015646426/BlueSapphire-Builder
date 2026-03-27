using System.Windows;

namespace BlueSapphire.Builder
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            // ✅ 极客修复：注册系统代码页，完美支持 GB2312/GBK，彻底根除 InnoSetup 和 Cmd 乱码
            System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
        }
    }
}