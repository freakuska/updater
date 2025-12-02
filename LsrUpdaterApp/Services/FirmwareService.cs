using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using LsrUpdaterApp.Models;

namespace LsrUpdaterApp.Services
{
    /// <summary>
    /// сервис для работы с файлами прошивок ЛСР
    /// </summary>
    public class FirmwareService
    {
        private readonly BkrCommandExecutor _bkrExecutor;
        private bool _disposed;

        public event EventHandler<string> OnLog;
        public event EventHandler<string> OnError;

        public FirmwareService(string bkrIp = "10.0.1.89", int bkrPort = 3456)
        {
            _bkrExecutor = new BkrCommandExecutor(bkrIp, bkrPort);
            _bkrExecutor.OnLog += (s, msg) => OnLog?.Invoke(this, msg);
            _bkrExecutor.OnError += (s, msg) => OnError?.Invoke(this, msg);
        }

        public async Task<List<LsrInfo>> GetAllLsrInfoAsync()
        {
            try
            {
                Log("");
                Log("╔════════════════════════════════════════════════════╗");
                Log("║  ПОЛУЧЕНИЕ ИНФОРМАЦИИ О ЛСР                        ║");
                Log("╚════════════════════════════════════════════════════╝");
                Log("");

                if (!await _bkrExecutor.ConnectAsync())
                    return new List<LsrInfo>();

                if (!await _bkrExecutor.StopPhyAsync())
                {
                    _bkrExecutor.Disconnect();
                    return new List<LsrInfo>();
                }

                await Task.Delay(2000);

                if (!await _bkrExecutor.ClearLsrPollAsync())
                {
                    _bkrExecutor.Disconnect();
                    return new List<LsrInfo>();
                }

                if (!await _bkrExecutor.PollLsrAsync())
                {
                    _bkrExecutor.Disconnect();
                    return new List<LsrInfo>();
                }

                if (!await _bkrExecutor.WaitForStatisticsAsync(60))
                {
                    _bkrExecutor.Disconnect();
                    return new List<LsrInfo>();
                }

                if (!await _bkrExecutor.EnablePromiscuousModeAsync())
                {
                    _bkrExecutor.Disconnect();
                    return new List<LsrInfo>();
                }

                await Task.Delay(1000);

                var lsrList = await _bkrExecutor.GetLsrListVersionsAsync();

                foreach (var lsr in lsrList)
                {
                    if (ushort.TryParse(lsr.Id, System.Globalization.NumberStyles.HexNumber, null, out ushort maupNum))
                    {
                        string ipAddr = await _bkrExecutor.GetLsrIpAddressAsync(maupNum);
                        if (!string.IsNullOrEmpty(ipAddr))
                            lsr.IpAddress = ipAddr;

                        var sysInfo = await _bkrExecutor.GetLsrSysInfoAsync(maupNum);
                        if (sysInfo.ContainsKey("Status"))
                            lsr.Status = sysInfo["Status"];

                        Log($"  📋 {lsr.ToLogString()}");
                    }

                    await Task.Delay(500);
                }

                await _bkrExecutor.DisablePromiscuousModeAsync();
                await Task.Delay(1000);
                await _bkrExecutor.StartPhyAsync();

                _bkrExecutor.Disconnect();

                Log("");
                Log("✅✅✅ Получение информации завершено! ✅✅✅");
                Log("");

                return lsrList;
            }
            catch (Exception ex)
            {
                LogError($"❌ ОШИБКА: {ex.Message}");
                _bkrExecutor.Disconnect();
                return new List<LsrInfo>();
            }
        }

        public async Task<bool> RollbackFirmwareAsync(LsrInfo lsr)
        {
            try
            {
                Log("");
                Log("╔════════════════════════════════════════════════════╗");
                Log("║  ОТКАТ ПРОШИВКИ ЛСР                                ║");
                Log("╚════════════════════════════════════════════════════╝");
                Log("");
                Log($"🔄 Откат прошивки для {lsr.ToLogString()}");
                Log("");

                if (!await _bkrExecutor.ConnectAsync())
                    return false;

                if (!await _bkrExecutor.InitializeBkrAsync())
                {
                    _bkrExecutor.Disconnect();
                    return false;
                }

                if (!ushort.TryParse(lsr.Id, System.Globalization.NumberStyles.HexNumber, null, out ushort maupNum))
                {
                    LogError("❌ Неверный ID ЛСР");
                    _bkrExecutor.Disconnect();
                    return false;
                }

                Log("");
                Log("═══════════════════════════════════════════════════════");
                Log("ПОДГОТОВКА К ОТКАТУ");
                Log("═══════════════════════════════════════════════════════");
                Log("");

                if (!await _bkrExecutor.SetIwdgAsync(maupNum, 3600))
                {
                    _bkrExecutor.Disconnect();
                    return false;
                }

                await Task.Delay(1000);

                if (!await _bkrExecutor.ResetLsrAsync(maupNum))
                {
                    _bkrExecutor.Disconnect();
                    return false;
                }

                string ipAddr = await _bkrExecutor.GetLsrIpAddressAsync(maupNum);
                if (string.IsNullOrEmpty(ipAddr))
                {
                    LogError("❌ ЛСР не отвечает");
                    _bkrExecutor.Disconnect();
                    return false;
                }

                if (await _bkrExecutor.CheckWwdgStatusAsync(maupNum))
                {
                    Log("⚙️  WWDG включен, отключаем...");

                    if (!await _bkrExecutor.DisableWwdgAsync(maupNum))
                    {
                        Log("⚠️ Ошибка при отключении WWDG, продолжаем...");
                    }

                    if (!await _bkrExecutor.ResetLsrAsync(maupNum))
                    {
                        LogError("⚠️ Ошибка при сбросе, продолжаем...");
                    }
                    await Task.Delay(2000);
                }

                Log("");
                Log("═══════════════════════════════════════════════════════");
                Log("ОТКАТ ЧЕРЕЗ FLASH ERASE");
                Log("═══════════════════════════════════════════════════════");
                Log("");

                Log("🔥 Выполняем откат прошивки через очистку флешки...");
                Log("   (ЛСР загрузится с версией, хранящейся в ПЗУ)");

                if (!await _bkrExecutor.EraseFlashAsync(maupNum))
                {
                    LogError("⚠️ Ошибка при очистке флешки, пытаемся продолжить...");
                }

                await Task.Delay(2000);

                Log("");
                Log("═══════════════════════════════════════════════════════");
                Log("ФИНАЛИЗАЦИЯ");
                Log("═══════════════════════════════════════════════════════");
                Log("");

                if (!await _bkrExecutor.DisableIwdgAsync(maupNum))
                {
                    LogError("⚠️ Ошибка при отключении IWDG, продолжаем...");
                }

                if (!await _bkrExecutor.ResetLsrAsync(maupNum))
                {
                    LogError("⚠️ Ошибка при финальном сбросе, продолжаем...");
                }

                await Task.Delay(3000);

                if (!await _bkrExecutor.FinalizeBkrAsync())
                {
                    LogError("⚠️ Ошибка при финализации БКР");
                }

                _bkrExecutor.Disconnect();

                Log("");
                Log("╔════════════════════════════════════════════════════╗");
                Log($"║  ОТКАТ ЛСР {lsr.Id} ЗАВЕРШЁН!                       ║");
                Log("╚════════════════════════════════════════════════════╝");
                Log("");
                Log("⚠️  ВНИМАНИЕ: После отката ЛСР загрузится со встроенной версией");
                Log($"   Для установки новой версии выполните перепрошивку!");
                Log("");

                return true;
            }
            catch (Exception ex)
            {
                LogError($"❌ КРИТИЧЕСКАЯ ОШИБКА: {ex.Message}");
                _bkrExecutor.Disconnect();
                return false;
            }
        }

        public async Task<(int total, int needsUpdate, int upToDate, int unavailable)> GetStatisticsAsync()
        {
            var lsrList = await GetAllLsrInfoAsync();

            int total = lsrList.Count;
            int needsUpdate = 0;
            int upToDate = 0;
            int unavailable = 0;

            foreach (var lsr in lsrList)
            {
                if (!lsr.IsAvailable)
                    unavailable++;
                else if (lsr.NeedsUpdate)
                    needsUpdate++;
                else
                    upToDate++;
            }

            return (total, needsUpdate, upToDate, unavailable);
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
                _bkrExecutor?.Dispose();
                _disposed = true;
            }
        }
    }
}
