using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace LsrUpdaterApp.Models
{
    /// <summary>
    /// модель данных о файле прошивке
    /// </summary>
    public class FirmwareInfo
    {
        public string FilePath { get; set; }
        public string FileName { get; set; }
        public long FileSize { get; set; }
        public string Version { get; set; }

        /// <summary>
        /// дата создания файла
        /// </summary>
        public DateTime CreatedDate { get; set; }

        /// <summary>
        /// дата последнего изменения файла
        /// </summary>
        public DateTime ModifiedDate { get; set; }

        /// <summary>
        /// MD5 хеш файла для проверки целостности
        /// </summary>
        public string Md5Hash { get; set; } 
        public bool IsValid { get; set; }

        public FirmwareInfo()
        {
            FilePath = string.Empty;
            FileName = string.Empty;
            FileSize = 0;
            Version = string.Empty;
            CreatedDate = DateTime.MinValue;
            ModifiedDate = DateTime.MinValue;
            Md5Hash = string.Empty;
            IsValid = false;
        }

        public FirmwareInfo(string filePath)
        {
            FilePath = filePath;
            FileName = System.IO.Path.GetFileName(filePath);
            Version = ExtractVersionFromFileName(FileName);
            IsValid = false;

            try
            {
                var fileInfo = new System.IO.FileInfo(filePath);
                FileSize = fileInfo.Length;
                CreatedDate = fileInfo.CreationTime;
                ModifiedDate = fileInfo.LastWriteTime;
                IsValid = fileInfo.Exists && FileSize > 0;
            }
            catch
            {
                FileSize = 0;
                IsValid = false;
            }
        }

        /// <summary>
        /// парсинг версии из имени файла
        /// формат: lsr4-20221202.bin -> 2022-12-02
        /// </summary>
        private string ExtractVersionFromFileName(string fileName)
        {
            try
            {
                string namePart = System.IO.Path.GetFileNameWithoutExtension(fileName);

                string[] parts = namePart.Split('-');
                if (parts.Length >= 2 && parts[1].Length >= 8)
                {
                    string dateStr = parts[1].Substring(0, 8);
                    
                    return $"{dateStr.Substring(0, 4)}-{dateStr.Substring(4, 2)}-{dateStr.Substring(6, 2)}";
                }
            }
            catch { }
            return string.Empty;
        }

        /// <summary>
        /// получить размер файла в MB
        /// </summary>
        public double GetSizeInMb()
        {
            return FileSize / (1024.0 * 1024.0);
        }

        /// <summary>
        /// переопределение ToString
        /// </summary>
        public override string ToString()
        {
            return $"{FileName} ({GetSizeInMb():F2} MB) - Version: {Version}";
        }
    }
}
