using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using LsrUpdaterApp.Models;
using LsrUpdaterApp.Services;

namespace LsrUpdaterApp.Services
{
    public class LsrCommandExecutor : IDisposable
    {
        private readonly string _bkrIp;
        private readonly int _bkrPort;
        private UdpService _udpService;
        private bool _disposed;

        public event EventHandler<string> OnProgress;
        public event EventHandler<string> OnError;

        public LsrCommandExecutor(string bkrIp = "10.0.1.89", int bkrPort = 3456)
        {
            _bkrIp = bkrIp;
            _bkrPort = bkrPort;
            _udpService = new UdpService(_bkrIp, _bkrPort);
        }

        public async Task<bool> UpdateAllAsync(string firmwareFilePath)
        {
            try
            {
                Log("");
                Log("╔════════════════════════════════════════════════════╗");
                Log("║  НАЧАЛО ПРОЦЕССА ОБНОВЛЕНИЯ ЛСР                    ║");
                Log("╚════════════════════════════════════════════════════╝");
                Log("");

                Log($"📡 Подключение к БКР {_bkrIp}:{_bkrPort}...");

                bool connected = await _udpService.ConnectAsync();
                if (!connected)
                {
                    LogError("❌ Не удалось подключиться к БКР!");
                    return false;
                }
                Log("✅ Подключение к БКР установлено");
                Log("");

                Log("═══════════════════════════════════════════════════════");
                Log("ИНИЦИАЛИЗАЦИЯ БКР");
                Log("═══════════════════════════════════════════════════════");

                Log("🛑 Остановка опроса (phy stop)...");
                await _udpService.SendCommandAsync("phy stop");
                Log("✅ Опрос остановлен");
                await Task.Delay(5000);

                Log("🗑️ Очистка списка запросов (lsr poll clear)...");
                await _udpService.SendCommandAsync("lsr poll clear");

                Log("📊 Сбор списка ЛСР (lsr poll)...");
                await _udpService.SendCommandAsync("lsr poll");

                Log("⏳ Ожидание завершения сбора статистики...");
                int maxRetries = 30;
                for (int i = 0; i < maxRetries; i++)
                {
                    string bkrStatus = await _udpService.SendCommandAsync("bkr");
                    if (bkrStatus != null && bkrStatus.Contains("[0] 0"))
                    {
                        Log("✅ Сбор статистики завершён");
                        break;
                    }
                    await Task.Delay(1000);
                    if (i % 5 == 0)
                        Log($"   ⏳ Ожидание... {i}сек");
                }

                Log("📋 Получение списка версий (lsr llv)...");
                string llvResponse = await _udpService.SendCommandAsync("lsr llv");
                Log($"✅ Ответ: {llvResponse}");

                Log("📡 Включение promiscuous mode (eth promiscuous 1)...");
                await _udpService.SendCommandAsync("eth promiscuous 1");
                Log("✅ Promiscuous mode включен");
                Log("");

                Log("═══════════════════════════════════════════════════════");
                Log("ОБНОВЛЕНИЕ ЛСР");
                Log("═══════════════════════════════════════════════════════");
                Log("");

                List<LsrInfo> lsrList = ParseLsrList(llvResponse);
                Log($"📊 Найдено ЛСР: {lsrList.Count}");

                if (lsrList.Count == 0)
                {
                    LogError("⚠️ ЛСР не найдены!");
                    Log("");
                    Log("═══════════════════════════════════════════════════════");
                    Log("📡 Отключение promiscuous mode (eth promiscuous 0)...");
                    await _udpService.SendCommandAsync("eth promiscuous 0");
                    return false;
                }

                Log("");

                int successCount = 0;
                int failCount = 0;

                for (int i = 0; i < lsrList.Count; i++)
                {
                    LsrInfo lsr = lsrList[i];
                    Log($"");
                    Log($"[{i + 1}/{lsrList.Count}] Обновление {lsr.ToLogString()}");

                    bool updateSuccess = await UpdateOneAsync(lsr, firmwareFilePath);

                    if (updateSuccess)
                    {
                        successCount++;
                        lsr.Status = "✅ Успешно";
                        lsr.IsAvailable = true;
                    }
                    else
                    {
                        failCount++;
                        lsr.Status = "❌ Ошибка";
                        lsr.IsAvailable = false;
                    }

                    lsr.UpdateAttempts++;
                    lsr.LastStatusUpdate = DateTime.Now;

                    if (i < lsrList.Count - 1)
                    {
                        Log("");
                        Log("⏳ Пауза перед следующим ЛСР (5 сек)...");
                        await Task.Delay(5000);
                    }
                }

                Log("");
                Log("═══════════════════════════════════════════════════════");
                Log("ФИНАЛИЗАЦИЯ БКР");
                Log("═══════════════════════════════════════════════════════");

                Log("📡 Отключение promiscuous mode (eth promiscuous 0)...");
                await _udpService.SendCommandAsync("eth promiscuous 0");

                Log("▶️ Запуск опроса (phy start)...");
                await _udpService.SendCommandAsync("phy start");
                Log("✅ Опрос запущен");

                Log("");
                Log("╔════════════════════════════════════════════════════╗");
                Log($"║  РЕЗУЛЬТАТ: ✅ {successCount} успешно, ❌ {failCount} ошибок        ║");
                Log("╚════════════════════════════════════════════════════╝");
                Log("");

                return failCount == 0;
            }
            catch (Exception ex)
            {
                LogError($"❌ КРИТИЧЕСКАЯ ОШИБКА: {ex.Message}");
                return false;
            }
        }

        private async Task<bool> UpdateOneAsync(LsrInfo lsr, string firmwareFilePath)
        {
            try
            {
                Log($"  🔧 ФАЗА 1: Подготовка ЛСР {lsr.IpAddress}...");

                Log($"     ⏱️  Установка IWDG (3600 сек)...");
                await _udpService.SendCommandAsync("exe 0xFFFF eeprom iwdg rst 3600");

                Log($"     🔄 Сброс ЛСР...");
                await _udpService.SendCommandAsync("exe 0xFFFF reset");

                Log($"     ⏳ Ожидание включения ЛСР (2 сек)...");
                await Task.Delay(2000);

                Log($"     📍 Получение IP адреса ЛСР...");
                string ipAddrResponse = await _udpService.SendCommandAsync("exe 2561 phy ipaddr");
                if (string.IsNullOrEmpty(ipAddrResponse))
                {
                    LogError($"     ❌ ЛСР не отвечает");
                    lsr.LastError = "Нет ответа";
                    lsr.Status = "❌ Нет ответа";
                    lsr.IsAvailable = false;
                    return false;
                }
                Log($"     ✅ Ответ: {ipAddrResponse}");

                Log($"     🔍 Проверка WWDG...");
                string wwdgResponse = await _udpService.SendCommandAsync("exe 2561 wwdg");
                if (wwdgResponse != null && wwdgResponse.Contains("1"))
                {
                    Log($"     ⚙️  WWDG включен, отключаем...");
                    lsr.WwdgEnabled = true;
                    await _udpService.SendCommandAsync("exe 2561 eeprom wwdg");
                    await _udpService.SendCommandAsync("exe 2561 reset");
                    Log($"     ⏳ Ожидание после WWDG отключения...");
                    await Task.Delay(2000);
                }

                Log($"  ✅ ФАЗА 1 завершена!");

                Log($"");
                Log($"  📤 ФАЗА 2: TFTP передача прошивки на {lsr.IpAddress}...");

                TftpService tftpService = new TftpService(lsr.IpAddress);

                tftpService.OnProgress += (s, msg) => Log($"     {msg}");
                tftpService.OnError += (s, msg) => LogError($"     {msg}");
                tftpService.OnSuccess += (s, msg) => Log($"     {msg}");

                string remoteFileName = Path.GetFileName(firmwareFilePath);
                bool uploadSuccess = await tftpService.SendFirmwareAsync(firmwareFilePath, remoteFileName);
                tftpService.Dispose();

                if (!uploadSuccess)
                {
                    LogError($"     ❌ TFTP передача не удалась");
                    lsr.LastError = "Ошибка TFTP";
                    lsr.Status = "❌ Ошибка TFTP";
                    return false;
                }

                Log($"  ✅ ФАЗА 2 завершена!");

                Log($"  ⏳ Пауза перед финализацией (3 сек)...");
                await Task.Delay(3000);

                Log($"");
                Log($"  ✨ ФАЗА 3: Финализация...");

                Log($"     ⏱️  Отключение IWDG...");
                await _udpService.SendCommandAsync("exe 2561 eeprom iwdg rst 0");

                Log($"     🔄 Финальный сброс ЛСР...");
                await _udpService.SendCommandAsync("exe 2561 reset");

                Log($"     ⏳ Ожидание финализации (2 сек)...");
                await Task.Delay(2000);

                Log($"  ✅ ФАЗА 3 завершена!");

                lsr.Status = "✅ Успешно";
                lsr.LastError = null;
                lsr.NeedsUpdate = false;
                Log($"");
                Log($"✅✅✅ ЛСР {lsr.IpAddress} ({lsr.Id}) успешно обновлён! ✅✅✅");
                Log($"");

                return true;
            }
            catch (Exception ex)
            {
                lsr.LastError = ex.Message;
                lsr.Status = "❌ Ошибка";
                LogError($"❌ ОШИБКА {lsr.IpAddress}: {ex.Message}");
                return false;
            }
        }

        private List<LsrInfo> ParseLsrList(string llvResponse)
        {
            var lsrList = new List<LsrInfo>();

            if (string.IsNullOrEmpty(llvResponse))
            {
                return lsrList;
            }

            try
            {
                var lines = llvResponse.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);

                foreach (var line in lines)
                {
                    var parts = line.Trim().Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);

                    if (parts.Length >= 3)
                    {
                        string id = parts[0];
                        string ip = parts[1];
                        string version = parts[2];

                        if (!version.Contains("?"))
                        {
                            var lsr = new LsrInfo(id, ip, version)
                            {
                                NeedsUpdate = true
                            };
                            lsrList.Add(lsr);
                            Log($"  {lsr.ToLogString()}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LogError($"⚠️ Ошибка парсинга списка ЛСР: {ex.Message}");
            }

            return lsrList;
        }

        private void Log(string message)
        {
            OnProgress?.Invoke(this, message);
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
