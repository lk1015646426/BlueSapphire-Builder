using System;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace BlueSapphire.Builder.Controls
{
    /// <summary>
    /// 路径选择用户控件：TextBox 只显示路径 + 浏览按钮统一触发对话框。
    /// 减少主界面 8 处重复的 StackPanel + Button + Browse handler 样板代码。
    /// </summary>
    public partial class PathPicker : UserControl
    {
        public static readonly DependencyProperty PathProperty =
            DependencyProperty.Register(
                nameof(Path),
                typeof(string),
                typeof(PathPicker),
                new PropertyMetadata(string.Empty));

        public static readonly DependencyProperty FilterProperty =
            DependencyProperty.Register(
                nameof(Filter),
                typeof(string),
                typeof(PathPicker),
                new PropertyMetadata("文件夹|*.*"));

        public static readonly DependencyProperty DialogTitleProperty =
            DependencyProperty.Register(
                nameof(DialogTitle),
                typeof(string),
                typeof(PathPicker),
                new PropertyMetadata("请选择"));

        public static readonly DependencyProperty BrowseToolTipProperty =
            DependencyProperty.Register(
                nameof(BrowseToolTip),
                typeof(string),
                typeof(PathPicker),
                new PropertyMetadata("选择路径"));

        public static readonly DependencyProperty IsFolderPickerProperty =
            DependencyProperty.Register(
                nameof(IsFolderPicker),
                typeof(bool),
                typeof(PathPicker),
                new PropertyMetadata(true));

        /// <summary>当前选中的路径。双向绑定到 ViewModel 字段。</summary>
        public string Path
        {
            get => (string)GetValue(PathProperty);
            set => SetValue(PathProperty, value);
        }

        /// <summary>文件选择时的过滤器，如 "图标文件|*.ico"。文件夹模式忽略此属性。</summary>
        public string Filter
        {
            get => (string)GetValue(FilterProperty);
            set => SetValue(FilterProperty, value);
        }

        public string DialogTitle
        {
            get => (string)GetValue(DialogTitleProperty);
            set => SetValue(DialogTitleProperty, value);
        }

        public string BrowseToolTip
        {
            get => (string)GetValue(BrowseToolTipProperty);
            set => SetValue(BrowseToolTipProperty, value);
        }

        /// <summary>true 选择文件夹，false 选择文件（按 Filter 过滤）。</summary>
        public bool IsFolderPicker
        {
            get => (bool)GetValue(IsFolderPickerProperty);
            set => SetValue(IsFolderPickerProperty, value);
        }

        /// <summary>路径被用户手动改变时触发（通过浏览或直接输入）。回传到 MainWindow 的 Btn handler。</summary>
        public event EventHandler<string?>? PathChanged;

        public PathPicker()
        {
            InitializeComponent();
        }

        private void BrowseBtn_Click(object sender, RoutedEventArgs e)
        {
            string? current = Path;

            if (IsFolderPicker)
            {
                var dlg = new OpenFolderDialog
                {
                    Title = DialogTitle,
                    Multiselect = false
                };
                if (!string.IsNullOrWhiteSpace(current) && System.IO.Directory.Exists(current))
                {
                    dlg.InitialDirectory = current;
                }

                if (dlg.ShowDialog() == true)
                {
                    Path = dlg.FolderName;
                    PathChanged?.Invoke(this, dlg.FolderName);
                }
            }
            else
            {
                var dlg = new OpenFileDialog
                {
                    Title = DialogTitle,
                    Filter = Filter
                };
                if (!string.IsNullOrWhiteSpace(current) && System.IO.File.Exists(current))
                {
                    dlg.InitialDirectory = System.IO.Path.GetDirectoryName(current);
                    dlg.FileName = System.IO.Path.GetFileName(current);
                }

                if (dlg.ShowDialog() == true)
                {
                    Path = dlg.FileName;
                    PathChanged?.Invoke(this, dlg.FileName);
                }
            }
        }
    }
}
