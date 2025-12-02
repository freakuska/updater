using System;
using System.Collections.ObjectModel;
using System.Windows.Input;
using LsrUpdaterApp.Models;
using LsrUpdaterApp.Services;

namespace LsrUpdaterApp.ViewModels
{
    public class MainWindowViewModel
    {
        private FirmwareService _firmwareService;
        private LsrCommandExecutor _updateExecutor;
        private ObservableCollection<LsrInfo> _lsrList;
        private ObservableCollection<string> _logs;

        public ObservableCollection<LsrInfo> LsrList
        {
            get { return _lsrList; }
            set { _lsrList = value; }
        }

        public ObservableCollection<string> Logs
        {
            get { return _logs; }
            set { _logs = value; }
        }

        public MainWindowViewModel()
        {
            _lsrList = new ObservableCollection<LsrInfo>();
            _logs = new ObservableCollection<string>();
            _firmwareService = new FirmwareService("10.0.1.89", 3456);
            _updateExecutor = new LsrCommandExecutor("10.0.1.89", 3456);

            SetupEventHandlers();
        }

        private void SetupEventHandlers()
        {
            _firmwareService.OnLog += (s, msg) => AddLog(msg);
            _firmwareService.OnError += (s, msg) => AddLog($"❌ {msg}");

            _updateExecutor.OnProgress += (s, msg) => AddLog(msg);
            _updateExecutor.OnError += (s, msg) => AddLog($"❌ {msg}");
        }

        private void AddLog(string message)
        {
            _logs.Add($"[{DateTime.Now:HH:mm:ss}] {message}");
        }
    }
}
