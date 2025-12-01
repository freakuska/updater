using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace LsrUpdaterApp.Models
{
    /// <summary>
    /// модель статистики обновления ЛСР
    /// </summary>
    public class UpdateStatistics
    {
        /// <summary>
        /// время начала обновления
        /// </summary>
        public DateTime StartTime { get; set; }
        /// <summary>
        /// время окончания обновления
        /// </summary>
        public DateTime EndTime { get; set; }

        /// <summary>
        /// общее количество ЛСР
        /// </summary>
        public int TotalLsr { get; set; }

        /// <summary>
        /// количество успешно обновленных ЛСР
        /// </summary>
        public int SuccessfulUpdates { get; set; }

        /// <summary>
        /// количество ошибок при обновлении
        /// </summary>
        public int FailedUpdates { get; set; }

        /// <summary>
        /// количество пропущенных ЛСР (уже актуальная версия)
        /// </summary>
        public int SkippedUpdates { get; set; }

        /// <summary>
        /// количество недоступных ЛСР
        /// </summary>
        public int UnavailableLsr { get; set; }

        /// <summary>
        /// список всех ошибок с описанием
        /// </summary>
        public List<string> Errors { get; set; }

        /// <summary>
        /// список всех предупреждений
        /// </summary>
        public List<string> Warnings { get; set; }

        /// <summary>
        /// статус обновления (успешно, отменено, ошибка)
        /// </summary>
        public UpdateStatus Status { get; set; }

        /// <summary>
        /// процент выполнения (0-100)
        /// </summary>
        public double ProgressPercentage { get; set; }

        /// <summary>
        /// текущее описание операции
        /// </summary>
        public string CurrentOperation { get; set; }

        public UpdateStatistics()
        {
            StartTime = DateTime.Now;
            EndTime = DateTime.MinValue;
            TotalLsr = 0;
            SuccessfulUpdates = 0;
            FailedUpdates = 0;
            SkippedUpdates = 0;
            UnavailableLsr = 0;
            Errors = new List<string>();
            Warnings = new List<string>();
            Status = UpdateStatus.Idle;
            ProgressPercentage = 0;
            CurrentOperation = string.Empty;
        }

        /// <summary>
        /// получить длительность обновления в формате "HH:MM:SS"
        /// </summary>
        public string GetDuration()
        {
            DateTime end = EndTime == DateTime.MinValue ? DateTime.Now : EndTime;
            TimeSpan duration = end - StartTime;
            return $"{(int)duration.TotalHours:D2}:{duration.Minutes:D2}:{duration.Seconds:D2}";
        }

        /// <summary>
        /// Получить процент успешных обновлений
        /// </summary>
        public double GetSuccessPercentage()
        {
            if (TotalLsr == 0) return 0;
            return (SuccessfulUpdates / (double)TotalLsr) * 100;
        }

        /// <summary>
        /// Получить строку с итоговой статистикой
        /// </summary>
        public string GetSummary()
        {
            return $"Итого: {TotalLsr} | ✅ {SuccessfulUpdates} | ❌ {FailedUpdates} | ⏭️  {SkippedUpdates} | ⚠️  {UnavailableLsr}";
        }

        /// <summary>
        /// Переопределение ToString
        /// </summary>
        public override string ToString()
        {
            return GetSummary();
        }

        /// <summary>
        /// Перечисление статусов обновления
        /// </summary>
        public enum UpdateStatus
        {
            /// <summary>
            /// В ожидании
            /// </summary>
            Idle,

            /// <summary>
            /// Инициализация подключения
            /// </summary>
            Initializing,

            /// <summary>
            /// Сбор информации о ЛСР
            /// </summary>
            GatheringInfo,

            /// <summary>
            /// Анализ информации
            /// </summary>
            Analyzing,

            /// <summary>
            /// Выполнение обновления
            /// </summary>
            Updating,

            /// <summary>
            /// Восстановление системы
            /// </summary>
            Restoring,

            /// <summary>
            /// Обновление завершено успешно
            /// </summary>
            Completed,

            /// <summary>
            /// Обновление отменено пользователем
            /// </summary>
            Cancelled,

            /// <summary>
            /// Ошибка при обновлении
            /// </summary>
            Error
        }
    }
}
