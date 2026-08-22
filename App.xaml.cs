using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using BlueSapphire.Builder.Helpers;

namespace BlueSapphire.Builder
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            // 注册系统代码页，完美支持 GB2312/GBK，彻底根除 InnoSetup 和 Cmd 乱码
            System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);

            // 打包耗时较长，期间常发生锁屏/睡眠/远程桌面切换，DWM 合成暂停会导致
            // GPU 渲染线程连环崩溃（crash.log 2026-08-15：0x80263001 → UCEERR_RENDERTHREADFAILURE）。
            // 本工具 UI 简单，强制软件渲染彻底摆脱 DWM/GPU 依赖，换取无人值守构建的稳定性。
            RenderOptions.ProcessRenderMode = RenderMode.SoftwareOnly;

            // 全局异常兜底：UI 线程 / 非 UI 线程 / 未观察 Task 三道防线
            DispatcherUnhandledException += App_DispatcherUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += App_DomainUnhandledException;
            TaskScheduler.UnobservedTaskException += App_UnobservedTaskException;
        }

        // UI 线程未处理异常：可恢复，标记 Handled 阻止崩溃
        private void App_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            CrashLogger.LogCrash(e.Exception, "Dispatcher");
            MessageBox.Show(
                $"发生未处理的 UI 线程异常：\n\n{e.Exception.Message}\n\n详细信息已记录到：\n{CrashLogger.GetCrashLogPath()}",
                "Blue Sapphire 异常", MessageBoxButton.OK, MessageBoxImage.Error);
            e.Handled = true;
        }

        // 非 UI 线程未处理异常：通常 IsTerminating=true，进程即将结束，只能记录
        private void App_DomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            if (e.ExceptionObject is Exception ex)
            {
                CrashLogger.LogCrash(ex, $"AppDomain(IsTerminating={e.IsTerminating})");
            }
        }

        // 未观察的 Task 异常：调用 SetObserved 阻止其升级为进程崩溃
        private void App_UnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
        {
            CrashLogger.LogCrash(e.Exception, "TaskScheduler");
            e.SetObserved();
        }
    }
}
