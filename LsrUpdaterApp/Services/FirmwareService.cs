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
        private const string FirmwareDirName = "firmware";
        private const string LsrSubDirName = "lsr4";

        /// <summary>
        /// событие об ошибке
        /// </summary>
        public event EventHandler<string> OnError;

        /// <summary>
        /// событие с информационным сообщением 
        /// </summary>
        public event EventHandler<string> OnInfo;

        /// <summary>
        /// получение пути к директории хранения прошивок
        /// </summary>
        /// <returns></returns>
        public string GetFirmwareDirectory()
        {
            string homeDir  = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string firmwareDir = Path.Combine(homeDir,FirmwareDirName, LsrSubDirName);
            return firmwareDir;
        }

        /// <summary>
        /// создание директории для хранения прошивок
        /// </summary>
        /// <returns></returns>
        public bool CreateFirmwareDirectory()
        {
            try
            {
                string firmwareDir = GetFirmwareDirectory();
                if (!Directory.Exists(firmwareDir))
                {
                    Directory.CreateDirectory(firmwareDir);
                    OnInfo?.Invoke(this, $"✅ Директория создана: {firmwareDir}");
                }
                else
                {
                    OnInfo?.Invoke(this, $"✅ Директория существует: {firmwareDir}");
                }
                return true;
            }
            catch (Exception ex)
            {
                OnError?.Invoke(this, $"Ошибка создания директории: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// копирование файла прошивки в целевую директорию
        /// </summary>
        /// <param name="sourcePath"></param>
        /// <returns></returns>
        public async Task<bool> CopyFirmwareFileAsync(string sourcePath)
        {
            return await Task.Run(() =>
            {
                try
                {
                    if (!File.Exists(sourcePath))
                    {
                        OnError?.Invoke(this, $"Файл не найден: {sourcePath}");
                        return false;
                    }

                    CreateFirmwareDirectory();

                    string targetDir = GetFirmwareDirectory();
                    string fileName = Path.GetFileName(sourcePath);
                    string targetPath = Path.Combine(targetDir, fileName);

                    //копирование с перезаписью
                    File.Copy(sourcePath, targetPath, true);
                    OnInfo?.Invoke(this, $"✅ Файл скопирован: {targetPath}");
                    return true;
                }
                catch (Exception ex)
                {
                    OnError?.Invoke(this, $"Ошибка копирования файла: {ex.Message}");
                    return false;
                }
            });
        }

        /// <summary>
        /// получить FirmwareInfo объект с информацией о файле
        /// </summary>
        public FirmwareInfo GetFirmwareInfo(string sourcePath)
        {
            try
            {
                if (!File.Exists(sourcePath))
                {
                    OnError?.Invoke(this, $"Файл не найден: {sourcePath}");
                    return null;
                }

                var info = new FirmwareInfo(sourcePath);

                // расчет MD5 хеша
                info.Md5Hash = CalculateMd5(sourcePath);

                OnInfo?.Invoke(this, $"📦 Информация о прошивке: {info}");
                return info;
            }
            catch (Exception ex)
            {
                OnError?.Invoke(this, $"Ошибка получения информации о файле: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// получить путь к скопированному файлу прошивки в директории
        /// </summary>
        public string GetFirmwareFilePath(string fileName)
        {
            string firmwareDir = GetFirmwareDirectory();
            return Path.Combine(firmwareDir, fileName);
        }

        /// <summary>
        /// проверка на существование файла прошивки в директории
        /// </summary>
        public bool FirmwareFileExists(string fileName)
        {
            string filePath = GetFirmwareFilePath(fileName);
            return File.Exists(filePath);
        }

        /// <summary>
        /// получение размера файла прошивки в байтах
        /// </summary>
        public long GetFirmwareFileSize(string fileName)
        {
            try
            {
                string filePath = GetFirmwareFilePath(fileName);
                if (File.Exists(filePath))
                {
                    var fileInfo = new FileInfo(filePath);
                    return fileInfo.Length;
                }
                return 0;
            }
            catch
            {
                return 0;
            }
        }

        /// <summary>
        /// получение размера файла в MB (читаемый формат)
        /// </summary>
        public string GetFirmwareFileSizeFormatted(string fileName)
        {
            long bytes = GetFirmwareFileSize(fileName);
            if (bytes == 0) return "0 MB";

            double mb = bytes / (1024.0 * 1024.0);
            return $"{mb:F2} MB";
        }

        /// <summary>
        /// удаление файла прошивки из директории
        /// </summary>
        public bool DeleteFirmwareFile(string fileName)
        {
            try
            {
                string filePath = GetFirmwareFilePath(fileName);
                if (File.Exists(filePath))
                {
                    File.Delete(filePath);
                    OnInfo?.Invoke(this, $"✅ Файл удален: {filePath}");
                    return true;
                }
                return false;
            }
            catch (Exception ex)
            {
                OnError?.Invoke(this, $"Ошибка удаления файла: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// открыть директорию с прошивками в проводнике
        /// </summary>
        public bool OpenFirmwareDirectory()
        {
            try
            {
                string firmwareDir = GetFirmwareDirectory();
                CreateFirmwareDirectory();

                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = firmwareDir,
                    UseShellExecute = true
                });
                return true;
            }
            catch (Exception ex)
            {
                OnError?.Invoke(this, $"Ошибка открытия директории: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// получение списока всех файлов прошивок в директории
        /// </summary>
        public string[] GetAllFirmwareFiles()
        {
            try
            {
                string firmwareDir = GetFirmwareDirectory();
                CreateFirmwareDirectory();

                if (Directory.Exists(firmwareDir))
                {
                    return Directory.GetFiles(firmwareDir, "*.bin");
                }
                return Array.Empty<string>();
            }
            catch (Exception ex)
            {
                OnError?.Invoke(this, $"Ошибка получения списка файлов: {ex.Message}");
                return Array.Empty<string>();
            }
        }

        /// <summary>
        /// вычисление MD5 хеша файла для проверки целостности
        /// </summary>
        /// <param name="filePath"></param>
        /// <returns></returns>
        private string CalculateMd5(string filePath)
        {
            try
            {
                using (var md5 = MD5.Create())
                {
                    using (var stream = File.OpenRead(filePath))
                    {
                        var hash = md5.ComputeHash(stream);
                        return BitConverter.ToString(hash).Replace("-", "").ToLower();
                    }
                }
            }
            catch
            {
                return string.Empty;
            }
        }

        /// <summary>
        /// проверка целостности файла по MD5 хешу
        /// </summary>
        public bool VerifyFileIntegrity(string filePath, string expectedHash)
        {
            try
            {
                string actualHash = CalculateMd5(filePath);
                return actualHash.Equals(expectedHash, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }
    }
}
