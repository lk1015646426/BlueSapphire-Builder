using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace BlueSapphire.Builder
{
    /// <summary>
    /// 深色主题预设保存对话框：展示配置摘要 + 输入预设名。
    /// </summary>
    public sealed class InputBoxDialog : Window
    {
        private readonly TextBox _textBox;
        public string Input { get; private set; } = string.Empty;

        // 深色主题色板（与主界面一致）
        private static readonly Brush BgBrush = new SolidColorBrush(Color.FromRgb(0x0D, 0x11, 17));
        private static readonly Brush CardBrush = new SolidColorBrush(Color.FromRgb(0x16, 0x1B, 0x22));
        private static readonly Brush AccentBrush = new SolidColorBrush(Color.FromRgb(0x22, 0xD3, 0xEE));
        private static readonly Brush TextBrush = new SolidColorBrush(Color.FromRgb(0xE2, 0xE8, 0xF0));
        private static readonly Brush SubTextBrush = new SolidColorBrush(Color.FromRgb(0x64, 0x74, 0x8B));
        private static readonly Brush InputBgBrush = new SolidColorBrush(Color.FromRgb(0x0B, 0x10, 0x1A));
        private static readonly Brush DialogBorderBrush = new SolidColorBrush(Color.FromRgb(0x1E, 0x29, 0x3B));

        static InputBoxDialog()
        {
            // Freeze 画刷以提升性能
            BgBrush.Freeze();
            CardBrush.Freeze();
            AccentBrush.Freeze();
            TextBrush.Freeze();
            SubTextBrush.Freeze();
            InputBgBrush.Freeze();
            DialogBorderBrush.Freeze();
        }

        public static string? Show(string prompt, string title, string defaultValue = "", List<KeyValuePair<string, string>>? summaryItems = null)
        {
            var dlg = new InputBoxDialog(prompt, defaultValue, summaryItems)
            {
                Title = title,
                Width = 520,
                Height = summaryItems != null && summaryItems.Count > 0 ? 520 : 220,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                ResizeMode = ResizeMode.NoResize,
                Background = BgBrush
            };
            return dlg.ShowDialog() == true ? dlg.Input : null;
        }

        private InputBoxDialog(string prompt, string defaultValue, List<KeyValuePair<string, string>>? summaryItems)
        {
            var scrollViewer = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Padding = new Thickness(0)
            };

            var panel = new StackPanel { Margin = new Thickness(24) };

            // 标题提示
            panel.Children.Add(new TextBlock
            {
                Text = prompt,
                FontSize = 14,
                FontWeight = FontWeights.SemiBold,
                Foreground = TextBrush,
                Margin = new Thickness(0, 0, 0, 12)
            });

            // 配置摘要卡片
            if (summaryItems != null && summaryItems.Count > 0)
            {
                panel.Children.Add(new TextBlock
                {
                    Text = "当前配置预览",
                    FontSize = 11,
                    Foreground = SubTextBrush,
                    Margin = new Thickness(0, 0, 0, 6)
                });

                var summaryBorder = new Border
                {
                    Background = CardBrush,
                    CornerRadius = new CornerRadius(8),
                    Padding = new Thickness(14, 10, 14, 10),
                    Margin = new Thickness(0, 0, 0, 16),
                    BorderBrush = DialogBorderBrush,
                    BorderThickness = new Thickness(1)
                };

                var summaryPanel = new StackPanel();
                foreach (var kv in summaryItems)
                {
                    var row = new Grid { Margin = new Thickness(0, 3, 0, 3) };
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                    var label = new TextBlock
                    {
                        Text = kv.Key,
                        FontSize = 11,
                        Foreground = SubTextBrush,
                        VerticalAlignment = VerticalAlignment.Top,
                        TextWrapping = TextWrapping.NoWrap,
                        TextTrimming = TextTrimming.CharacterEllipsis
                    };
                    Grid.SetColumn(label, 0);

                    var value = new TextBlock
                    {
                        Text = string.IsNullOrWhiteSpace(kv.Value) ? "（未设置）" : kv.Value,
                        FontSize = 11,
                        Foreground = string.IsNullOrWhiteSpace(kv.Value) ? SubTextBrush : TextBrush,
                        VerticalAlignment = VerticalAlignment.Top,
                        TextWrapping = TextWrapping.Wrap,
                        FontFamily = new FontFamily("Cascadia Code, Consolas, Microsoft YaHei UI")
                    };
                    Grid.SetColumn(value, 1);

                    row.Children.Add(label);
                    row.Children.Add(value);
                    summaryPanel.Children.Add(row);
                }

                summaryBorder.Child = summaryPanel;
                panel.Children.Add(summaryBorder);
            }

            // 预设名输入框
            panel.Children.Add(new TextBlock
            {
                Text = "预设名称",
                FontSize = 11,
                Foreground = SubTextBrush,
                Margin = new Thickness(0, 0, 0, 5)
            });

            _textBox = new TextBox
            {
                Text = defaultValue,
                Padding = new Thickness(10, 7, 10, 7),
                FontSize = 13,
                Background = InputBgBrush,
                Foreground = TextBrush,
                BorderBrush = DialogBorderBrush,
                BorderThickness = new Thickness(1),
                CaretBrush = AccentBrush
            };
            _textBox.Loaded += (s, e) => { _textBox.Focus(); _textBox.SelectAll(); };
            panel.Children.Add(_textBox);

            // 按钮组
            var btnPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 20, 0, 0)
            };

            var cancel = CreateStyledButton("取消", isPrimary: false);
            cancel.Margin = new Thickness(0, 0, 8, 0);
            cancel.Click += (s, e) => { DialogResult = false; };

            var ok = CreateStyledButton("保存", isPrimary: true);
            ok.Click += (s, e) => { Input = _textBox.Text; DialogResult = true; };

            btnPanel.Children.Add(cancel);
            btnPanel.Children.Add(ok);
            panel.Children.Add(btnPanel);

            scrollViewer.Content = panel;
            Content = scrollViewer;
        }

        private Button CreateStyledButton(string text, bool isPrimary)
        {
            var btn = new Button
            {
                Content = text,
                Padding = new Thickness(24, 8, 24, 8),
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Cursor = System.Windows.Input.Cursors.Hand,
                MinWidth = 80
            };

            if (isPrimary)
            {
                var gradBrush = new LinearGradientBrush
                {
                    StartPoint = new Point(0, 0),
                    EndPoint = new Point(1, 1)
                };
                gradBrush.GradientStops.Add(new GradientStop(Color.FromRgb(0x00, 0xF0, 0xFF), 0));
                gradBrush.GradientStops.Add(new GradientStop(Color.FromRgb(0x2E, 0x5C, 0xFF), 1));
                gradBrush.Freeze();

                var border = new Border
                {
                    CornerRadius = new CornerRadius(8),
                    Background = gradBrush,
                    BorderBrush = new SolidColorBrush(Color.FromArgb(0x80, 0xFF, 0xFF, 0xFF)),
                    BorderThickness = new Thickness(1)
                };

                var content = new ContentPresenter
                {
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
                border.Child = content;

                var ctrlTemplate = new ControlTemplate(typeof(Button));
                var elemFactory = new FrameworkElementFactory(typeof(Border));
                elemFactory.SetValue(Border.BackgroundProperty, gradBrush);
                elemFactory.SetValue(Border.CornerRadiusProperty, new CornerRadius(8));
                var contentFactory = new FrameworkElementFactory(typeof(ContentPresenter));
                contentFactory.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
                contentFactory.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
                elemFactory.AppendChild(contentFactory);
                ctrlTemplate.VisualTree = elemFactory;

                btn.Template = ctrlTemplate;
                btn.Foreground = new SolidColorBrush(Colors.Black);
            }
            else
            {
                var ctrlTemplate = new ControlTemplate(typeof(Button));
                var elemFactory = new FrameworkElementFactory(typeof(Border));
                elemFactory.SetValue(Border.BackgroundProperty, new SolidColorBrush(Color.FromArgb(0x10, 0x00, 0xF0, 0xFF)));
                elemFactory.SetValue(Border.BorderBrushProperty, new SolidColorBrush(Color.FromArgb(0x80, 0x00, 0xF0, 0xFF)));
                elemFactory.SetValue(Border.BorderThicknessProperty, new Thickness(1));
                elemFactory.SetValue(Border.CornerRadiusProperty, new CornerRadius(8));
                var contentFactory = new FrameworkElementFactory(typeof(ContentPresenter));
                contentFactory.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
                contentFactory.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
                elemFactory.AppendChild(contentFactory);
                ctrlTemplate.VisualTree = elemFactory;

                btn.Template = ctrlTemplate;
                btn.Foreground = AccentBrush;
            }

            return btn;
        }
    }
}
