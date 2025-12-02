using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using LsrUpdaterApp.Models;

namespace LsrUpdaterApp.Services
{
    public class BkrCommandExecutor : IDisposable
    {
        private readonly UdpService _udpService;
        private readonly BkrCommandParser _parser;
        private bool _disposed;

        public event EventHandler<string> OnLog;
        public event EventHandler<string> OnError;

        public BkrCommandExecutor(string bkrIp = "10.0.1.89", int bkrPort = 3456)
        {
            _udpService = new UdpService(bkrIp, bkrPort);
            _parser = new BkrCommandParser();
        }

        public async Task<bool> ConnectAsync()
        {
            Log("📡 Подключение к БКР...");
            bool connected = await _udpService.ConnectAsync();
            if (connected)
                Log("✅ Подключено к БКР");
            else
                LogError("❌ Не удалось подключиться к БКР");
            return connected;
        }

        public void Disconnect()
        {
            Log("📡 Отключение от БКР...");
            _udpService.Disconnect();
        }

        public async Task<bool> StopPhyAsync()
        {
            Log("🛑 Остановка опроса (phy stop)...");
            string response = await _udpService.SendCommandAsync("phy stop");

            if (_parser.IsErrorResponse(response))
            {
                LogError($"❌ Ошибка при остановке: {_parser.ExtractErrorMessage(response)}");
                return false;
            }

            Log("✅ Опрос остановлен");
            await Task.Delay(1000);
            return true;
        }

        public async Task<bool> ClearLsrPollAsync()
        {
            Log("🗑️ Очистка списка запросов (lsr poll clear)...");
            string response = await _udpService.SendCommandAsync("lsr poll clear");

            if (_parser.IsErrorResponse(response))
            {
                LogError($"❌ Ошибка при очистке: {_parser.ExtractErrorMessage(response)}");
                return false;
            }

            Log("✅ Список запросов очищен");
            return true;
        }

        public async Task<bool> PollLsrAsync()
        {
            Log("📊 Сбор списка ЛСР (lsr poll)...");
            string response = await _udpService.SendCommandAsync("lsr poll");

            if (_parser.IsErrorResponse(response))
            {
                LogError($"❌ Ошибка при опросе: {_parser.ExtractErrorMessage(response)}");
                return false;
            }

            Log("✅ Опрос ЛСР завершён");
            return true;
        }

        public async Task<bool> WaitForStatisticsAsync(int maxWaitSeconds = 30)
        {
            Log($"⏳ Ожидание завершения сбора статистики (макс {maxWaitSeconds} сек)...");

            for (int i = 0; i < maxWaitSeconds; i++)
            {
                string bkrStatus = await _udpService.SendCommandAsync("bkr");
                int status = _parser.ParseBkrStatus(bkrStatus);

                if (status == 0)
                {
                    Log("✅ Сбор статистики завершён");
                    return true;
                }

                if (i % 5 == 0)
                    Log($"   ⏳ Ожидание... {i}сек");

                await Task.Delay(1000);
            }

            LogError($"❌ Таймаут ожидания статистики ({maxWaitSeconds} сек)");
            return false;
        }

        public async Task<List<LsrInfo>> GetLsrListVersionsAsync()
        {
            Log("📋 Получение списка версий (lsr llv)...");
            string response = await _udpService.SendCommandAsync("lsr llv");

            if (_parser.IsErrorResponse(response))
            {
                LogError($"❌ Ошибка при получении списка: {_parser.ExtractErrorMessage(response)}");
                return new List<LsrInfo>();
            }

            var lsrList = _parser.ParserLsrListVersions(response);
            Log($"✅ Получено {lsrList.Count} ЛСР");

            foreach (var lsr in lsrList)
                Log($"  {lsr.ToLogString()}");

            return lsrList;
        }

        public async Task<bool> EnablePromiscuousModeAsync()
        {
            Log("📡 Включение promiscuous mode (eth promiscuous 1)...");
            string response = await _udpService.SendCommandAsync("eth promiscuous 1");

            if (_parser.IsErrorResponse(response))
            {
                LogError($"❌ Ошибка: {_parser.ExtractErrorMessage(response)}");
                return false;
            }

            Log("✅ Promiscuous mode включен");
            return true;
        }

        public async Task<bool> DisablePromiscuousModeAsync()
        {
            Log("📡 Отключение promiscuous mode (eth promiscuous 0)...");
            string response = await _udpService.SendCommandAsync("eth promiscuous 0");

            if (_parser.IsErrorResponse(response))
            {
                LogError($"❌ Ошибка: {_parser.ExtractErrorMessage(response)}");
                return false;
            }

            Log("✅ Promiscuous mode отключен");
            return true;
        }

        public async Task<bool> StartPhyAsync()
        {
            Log("▶️ Запуск опроса (phy start)...");
            string response = await _udpService.SendCommandAsync("phy start");

            if (_parser.IsErrorResponse(response))
            {
                LogError($"❌ Ошибка: {_parser.ExtractErrorMessage(response)}");
                return false;
            }

            Log("✅ Опрос запущен");
            return true;
        }

        public async Task<bool> SetIwdgAsync(ushort maupNum, ushort timeoutSeconds = 3600)
        {
            string cmd = $"exe {maupNum:X} eeprom iwdg rst {timeoutSeconds}";
            Log($"⏱️  Установка IWDG ({timeoutSeconds} сек)...");

            string response = await _udpService.SendCommandAsync(cmd);

            if (_parser.IsErrorResponse(response))
            {
                LogError($"❌ Ошибка установки IWDG: {_parser.ExtractErrorMessage(response)}");
                return false;
            }

            Log($"✅ IWDG установлен");
            return true;
        }

        public async Task<bool> ResetLsrAsync(ushort maupNum)
        {
            string cmd = $"exe {maupNum:X} reset";
            Log($"🔄 Сброс ЛСР...");

            string response = await _udpService.SendCommandAsync(cmd);

            if (_parser.IsErrorResponse(response))
            {
                LogError($"❌ Ошибка при сбросе: {_parser.ExtractErrorMessage(response)}");
                return false;
            }

            Log($"✅ ЛСР сброшен");
            await Task.Delay(2000);
            return true;
        }

        public async Task<string> GetLsrIpAddressAsync(ushort maupNum)
        {
            string cmd = $"exe {maupNum:X} phy ipaddr";
            Log($"📍 Получение IP адреса...");

            string response = await _udpService.SendCommandAsync(cmd);

            if (_parser.IsErrorResponse(response))
            {
                LogError($"❌ Ошибка при получении IP: {_parser.ExtractErrorMessage(response)}");
                return null;
            }

            string ipAddr = _parser.ParsePhyIpAddr(response);
            if (!string.IsNullOrEmpty(ipAddr))
                Log($"✅ IP адрес: {ipAddr}");
            else
                LogError("❌ Не удалось распарсить IP адрес");

            return ipAddr;
        }

        public async Task<bool> CheckWwdgStatusAsync(ushort maupNum)
        {
            string cmd = $"exe {maupNum:X} wwdg";
            Log($"🔍 Проверка WWDG...");

            string response = await _udpService.SendCommandAsync(cmd);

            if (_parser.IsErrorResponse(response))
            {
                LogError($"❌ Ошибка при проверке WWDG: {_parser.ExtractErrorMessage(response)}");
                return false;
            }

            bool wwdgEnabled = _parser.ParseWwdgStatus(response);
            Log($"✅ WWDG статус: {(wwdgEnabled ? "ВКЛЮЧЕН" : "ОТКЛЮЧЕН")}");
            return wwdgEnabled;
        }

        public async Task<bool> DisableWwdgAsync(ushort maupNum)
        {
            string cmd = $"exe {maupNum:X} eeprom wwdg";
            Log($"⚙️  Отключение WWDG...");

            string response = await _udpService.SendCommandAsync(cmd);

            if (_parser.IsErrorResponse(response))
            {
                LogError($"❌ Ошибка при отключении WWDG: {_parser.ExtractErrorMessage(response)}");
                return false;
            }

            Log($"✅ WWDG отключен");
            return true;
        }

        public async Task<Dictionary<string, string>> GetLsrSysInfoAsync(ushort maupNum)
        {
            string cmd = $"exe {maupNum:X} sys info";
            Log($"📊 Получение sys info...");

            string response = await _udpService.SendCommandAsync(cmd);

            if (_parser.IsErrorResponse(response))
            {
                LogError($"❌ Ошибка при получении sys info: {_parser.ExtractErrorMessage(response)}");
                return new Dictionary<string, string>();
            }

            var info = _parser.ParseSysInfo(response);
            Log($"✅ Получено {info.Count} параметров");
            return info;
        }

        public async Task<bool> EraseFlashAsync(ushort maupNum)
        {
            string cmd = $"exe {maupNum:X} flash erase1";
            Log($"🔥 Очистка флешки...");

            string response = await _udpService.SendCommandAsync(cmd, 10000);

            if (_parser.IsErrorResponse(response))
            {
                LogError($"❌ Ошибка при очистке флешки: {_parser.ExtractErrorMessage(response)}");
                return false;
            }

            Log($"✅ Флешка очищена");
            return true;
        }

        public async Task<bool> DisableIwdgAsync(ushort maupNum)
        {
            string cmd = $"exe {maupNum:X} eeprom iwdg rst 0";
            Log($"⏱️  Отключение IWDG...");

            string response = await _udpService.SendCommandAsync(cmd);

            if (_parser.IsErrorResponse(response))
            {
                LogError($"❌ Ошибка при отключении IWDG: {_parser.ExtractErrorMessage(response)}");
                return false;
            }

            Log($"✅ IWDG отключен");
            return true;
        }

        public async Task<bool> InitializeBkrAsync()
        {
            Log("");
            Log("╔════════════════════════════════════════════════════╗");
            Log("║  ИНИЦИАЛИЗАЦИЯ БКР                                 ║");
            Log("╚════════════════════════════════════════════════════╝");
            Log("");

            if (!await StopPhyAsync()) return false;
            await Task.Delay(2000);

            if (!await ClearLsrPollAsync()) return false;
            await Task.Delay(1000);

            if (!await PollLsrAsync()) return false;
            await Task.Delay(1000);

            if (!await WaitForStatisticsAsync(60)) return false;
            await Task.Delay(1000);

            if (!await EnablePromiscuousModeAsync()) return false;
            await Task.Delay(1000);

            Log("");
            Log("✅✅✅ БКР инициализирован! ✅✅✅");
            Log("");
            return true;
        }

        public async Task<bool> FinalizeBkrAsync()
        {
            Log("");
            Log("╔════════════════════════════════════════════════════╗");
            Log("║  ФИНАЛИЗАЦИЯ БКР                                   ║");
            Log("╚════════════════════════════════════════════════════╝");
            Log("");

            if (!await DisablePromiscuousModeAsync()) return false;
            await Task.Delay(1000);

            if (!await StartPhyAsync()) return false;
            await Task.Delay(2000);

            Log("");
            Log("✅✅✅ БКР восстановлен! ✅✅✅");
            Log("");
            return true;
        }

        private void Log(string message)
        {
            OnLog?.Invoke(this, message);
        }

        private void LogError(string message)
        {
            OnError?.Invoke(this, message);
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _udpService?.Dispose();
                _disposed = true;
            }
        }
    }
}
