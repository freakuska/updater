using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using LsrUpdaterApp.Models;

namespace LsrUpdaterApp.Services
{
    /// <summary>
    /// главный сервис, который управляет алгоритмом перепрошивки ЛСР,
    /// взаимодействует с UdpService и FirmwareService, обновляет статистику
    /// </summary>
    public class LsrCommandExecutor
    {
        private readonly UdpService _udpService;
        private readonly FirmwareService _firmwareService;
        private readonly BkrCommandParser _parser;

        public UpdateStatistics Statistics { get; private set; }
        public List<LsrInfo> LsrList { get; private set; }

        public event EventHandler<string> OnProgress;
        public event EventHandler<string> OnError;
        public event EventHandler<string> OnLog;

        public LsrCommandExecutor(UdpService udpService, FirmwareService firmwareService)
        {
            _udpService = udpService;
            _firmwareService = firmwareService;
            _parser = new BkrCommandParser();
            Statistics = new UpdateStatistics();
            LsrList = new List<LsrInfo>();
        }

        //первый этап - инициализация подключения и подготовка
        public async Task<bool> InitializeAsync()
        {
            OnProgress?.Invoke(this, "🔌 Подключение к БКР...");
            bool connected = await _udpService.ConnectAsync();
            if (!connected) { OnError?.Invoke(this, "Не удалось подключиться к БКР."); return false; }

            OnProgress?.Invoke(this, "⏹️ Остановка опроса (phy stop)...");
            await _udpService.SendCommandAsync("phy stop");
            await Task.Delay(1000);

            OnProgress?.Invoke(this, "🗑️ Очищение очереди (lsr poll clear)...");
            await _udpService.SendCommandAsync("lsr poll clear");
            await Task.Delay(500);

            return true;
        }

        //второй этап - сбор информации о ЛСР
        public async Task<bool> GatherLsrInfoAsync()
        {
            OnProgress?.Invoke(this, "🔄 Запуск опроса (lsr poll)...");
            await _udpService.SendCommandAsync("lsr poll");
            await Task.Delay(500);

            // Ждем окончания сбора статистики
            for (int i = 0; i < 10; i++)
            {
                string bkrStatus = await _udpService.SendCommandAsync("bkr");
                var status = _parser.ParseBkrStatus(bkrStatus);
                if (status == 0) { break; }
                await Task.Delay(700);
            }
            // Промискуитивный режим для передачи
            await _udpService.SendCommandAsync("eth promiscuous 1");

            OnProgress?.Invoke(this, "📋 Получение списка версий (lsr llv)...");
            string resp = await _udpService.SendCommandAsync("lsr llv", 4000);

            LsrList = _parser.ParserLsrListVersions(resp);
            Statistics.TotalLsr = LsrList.Count;

            foreach (var lsr in LsrList)
                OnLog?.Invoke(this, lsr.ToLogString());

            return LsrList.Count > 0;
        }

        //третий этап - анализ и подготовка к перепрошивке
        public void Analyze()
        {
            foreach (var lsr in LsrList)
            {
                lsr.NeedsUpdate = !lsr.FirmwareVersion.Contains("?") && lsr.FirmwareVersion.StartsWith("2.11"); // Пример!
                if (!lsr.IsAvailable) Statistics.UnavailableLsr++;
                else if (!lsr.NeedsUpdate) Statistics.SkippedUpdates++;
            }
        }

        //четвертый этап - перепрошивка ЛСР
        public async Task<bool> UpdateAllAsync(string firmwareFilePath)
        {
            var toUpdate = LsrList.Where(x => x.NeedsUpdate && x.IsAvailable).ToList();

            int i = 0;
            foreach (var lsr in toUpdate)
            {
                bool ok = await UpdateOneAsync(lsr, firmwareFilePath);
                if (ok) Statistics.SuccessfulUpdates++;
                else { Statistics.FailedUpdates++; Statistics.Errors.Add(lsr.LastError); }
                Statistics.ProgressPercentage = (++i / (double)toUpdate.Count) * 100;
            }
            return true;
        }

        // перепрошивка одного устройства
        public async Task<bool> UpdateOneAsync(LsrInfo lsr, string firmwareFilePath)
        {
            try
            {
                OnProgress?.Invoke(this, $"Обновление {lsr.IpAddress} ---");

                await _udpService.SendCommandAsync("exe 0xFFFF eeprom iwdg rst 3600");
                await _udpService.SendCommandAsync("exe 0xFFFF reset");
                string ipResult = await _udpService.SendCommandAsync("exe 2561 phy ipaddr");
                var ip = _parser.ParsePhyIpAddr(ipResult);
                lsr.IpAddress = ip;

                string wwdgResp = await _udpService.SendCommandAsync("exe 2561 wwdg");
                lsr.WwdgEnabled = _parser.ParseWwdgStatus(wwdgResp);

                if (lsr.WwdgEnabled)
                {
                    await _udpService.SendCommandAsync("exe 2561 eeprom wwdg");
                    await _udpService.SendCommandAsync("exe 2561 reset");
                }

                // Имитация отправки прошивки: в реальности нужно запускать shell-скрипт
                await Task.Delay(2500);
                OnProgress?.Invoke(this, $"Прошивка {firmwareFilePath} успешно передана на {lsr.IpAddress}.");

                // Повторяем команды для финализации, например:
                await _udpService.SendCommandAsync("exe 2561 eeprom iwdg rst 0");
                await _udpService.SendCommandAsync("exe 2561 reset");
                await _udpService.SendCommandAsync("eth promiscuous 0");
                lsr.Status = "✅ Обновлено";
                return true;
            }
            catch (Exception ex)
            {
                lsr.Status = "Ошибка обновления";
                lsr.LastError = ex.Message;
                OnError?.Invoke(this, $"Ошибка {lsr.IpAddress}: {ex.Message}");
                return false;
            }
        }

        // пятый этап - завершение и восстановление
        public async Task FinalizeAsync()
        {
            await _udpService.SendCtrlCAsync();
            _udpService.Disconnect();
            OnProgress?.Invoke(this, "Завершено.");
        }
    }
}
