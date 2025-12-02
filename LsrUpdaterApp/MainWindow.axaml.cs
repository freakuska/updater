using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using LsrUpdaterApp.ViewModels;
using System.Collections.ObjectModel;

namespace LsrUpdaterApp
{
    public partial class MainWindow : Window
    {
        private ListBox? _lsrListBox;
        private ListBox? _logsListBox;
        private TextBlock? _statusText;
        private TextBlock? _filePathText;

        private ObservableCollection<string> _logs;

        public MainWindow()
        {
            InitializeComponent();

            _logs = new ObservableCollection<string>();

            this.DataContext = new MainWindowViewModel();
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);

            _lsrListBox = this.FindControl<ListBox>("LsrListBox");
            _logsListBox = this.FindControl<ListBox>("LogsListBox");
            _statusText = this.FindControl<TextBlock>("StatusText");
            _filePathText = this.FindControl<TextBlock>("FilePathText");

            if (_logsListBox != null)
            {
                _logsListBox.ItemsSource = _logs;
            }
        }

        private void LoadLsrButton_Click(object? sender, RoutedEventArgs e)
        {
            AddLog("?? Загрузка списка ЛСР...");
            AddLog("? Загружено 5 ЛСР");
            UpdateStatus("? Список загружен", "#4CAF50");
        }

        private void SelectFileButton_Click(object? sender, RoutedEventArgs e)
        {
            AddLog("?? Выбран файл: firmware.bin");
            _filePathText!.Text = "/home/user/firmware/firmware.bin";
            UpdateStatus("?? Файл выбран", "#2196F3");
        }

        private void UpdateButton_Click(object? sender, RoutedEventArgs e)
        {
            AddLog("?? Начало обновления...");
            AddLog("?? Подключение к БКР...");
            AddLog("? Подключено к БКР");
            AddLog("?? Отправка прошивки ЛСР 2561...");
            AddLog("??? ЛСР 2561 успешно обновлён!");
            UpdateStatus("? Обновление завершено", "#4CAF50");
        }

        private void RevertButton_Click(object? sender, RoutedEventArgs e)
        {
            AddLog("?? Откат прошивки...");
            AddLog("?? Очистка флешки ЛСР 2561...");
            AddLog("? Откат завершён");
            UpdateStatus("? Откат выполнен", "#4CAF50");
        }

        private void SelectAllButton_Click(object? sender, RoutedEventArgs e)
        {
            AddLog("? Выбраны все ЛСР");
            UpdateStatus("? Все ЛСР выбраны", "#4CAF50");
        }

        private void DeselectAllButton_Click(object? sender, RoutedEventArgs e)
        {
            AddLog("? Выделение снято");
            UpdateStatus("? Готово", "#4CAF50");
        }

        private void ClearLogsButton_Click(object? sender, RoutedEventArgs e)
        {
            
            _logs.Clear();
            AddLog("??? Логи очищены");
        }

        private void AddLog(string message)
        {
            
            _logs.Add($"[{System.DateTime.Now:HH:mm:ss}] {message}");

            if (_logsListBox != null && _logs.Count > 0)
            {
                _logsListBox.ScrollIntoView(_logs.Count - 1);
            }
        }

        private void UpdateStatus(string message, string color)
        {
            if (_statusText != null)
            {
                _statusText.Text = message;
                _statusText.Foreground = new Avalonia.Media.SolidColorBrush(
                    Avalonia.Media.Color.Parse(color));
            }
        }
    }
}
