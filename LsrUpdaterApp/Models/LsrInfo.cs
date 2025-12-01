using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace LsrUpdaterApp.Models
{
    /// <summary>
    /// модель данных об одном считывателе
    /// </summary>
    public class LsrInfo
    {
        public string Id { get; set; }
        public string IpAddress { get; set; }

        /// <summary>
        /// версия текущей прошивки
        /// </summary>
        public string FirmwareVersion { get; set; }

        /// <summary>
        /// доступен ли ЛСР
        /// </summary>
        public bool IsAvailable { get; set; }

        /// <summary>
        /// требует ли обновления прошивки
        /// </summary>
        public bool NeedsUpdate { get; set; } 
        public string Status { get; set; }
        public bool WwdgEnabled { get; set; }
        public int UpdateAttempts { get; set; }
        public string LastError { get; set; }
        public DateTime LastStatusUpdate { get; set; }
        public bool IsSelected { get; set; }

        public LsrInfo()
        {
            Id = string.Empty;
            IpAddress = string.Empty;
            FirmwareVersion = string.Empty;
            Status = "Инициализация";
            IsAvailable = true;
            NeedsUpdate = false;
            WwdgEnabled = false;
            UpdateAttempts = 0;
            LastError = string.Empty;
            LastStatusUpdate = DateTime.Now;
            IsSelected = false;
        }

        public LsrInfo(string id, string ip, string version)
        {
            Id = id;
            IpAddress = ip;
            FirmwareVersion = version;
            Status = "Инициализация";
            IsAvailable = !version.Contains("?");
            NeedsUpdate = false;
            WwdgEnabled = false;
            UpdateAttempts = 0;
            LastError = string.Empty;
            LastStatusUpdate = DateTime.Now;
            IsSelected = false;
        }

        /// <summary>
        /// переопределение ToString для отладки
        /// </summary>
        public override string ToString()
        {
            return $"ЛСР {Id} ({IpAddress}): {FirmwareVersion} - {Status}";
        }

        /// <summary>
        /// получить строку для вывода
        /// </summary>
        public string ToLogString()
        {
            string available = IsAvailable ? "✅" : "❌";
            string needsUpdate = NeedsUpdate ? "🔄" : "✓";
            return $"{available} ID:{Id} IP:{IpAddress} Ver:{FirmwareVersion} {needsUpdate} {Status}";
        }
    }
}
