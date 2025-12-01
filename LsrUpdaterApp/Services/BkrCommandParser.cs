using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using LsrUpdaterApp.Models;

namespace LsrUpdaterApp.Services
{
    /// <summary>
    /// сервис для парсинга ответов от БКР
    /// </summary>
    public class BkrCommandParser
    {
        /// <summary>
        /// событие об ошибке парсинга
        /// </summary>
        public event EventHandler<string> OnParsingError;

        /// <summary>
        /// парсинг ответа команды "lsr llv" (list last versions)
        /// возвращает список ЛСР с информацией о версиях 
        /// </summary>
        /// <param name="responseData"></param>
        /// <returns></returns>
        public List<LsrInfo> ParserLsrListVersions(string responseData)
        {
            var lsrList = new List<LsrInfo>();

            try
            {
                if (string.IsNullOrEmpty(responseData))
                    return lsrList;

                string[] lines = responseData.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
                Regex regex = new Regex(@"lsr\s+(\d+)\s+\(([^)]+)\):\s+(.+)");

                foreach (string line in lines)
                {
                    if (string.IsNullOrWhiteSpace(line) || !line.Contains("lsr"))
                        continue;

                    Match match = regex.Match(line);
                    if (match.Success && match.Groups.Count >= 4)
                    {
                        try
                        {
                            string id = match.Groups[1].Value.Trim();
                            string ip = match.Groups[2].Value.Trim();
                            string version = match.Groups[3].Value.Trim();

                            var lsr = new LsrInfo(id, ip, version);
                            lsrList.Add(lsr);
                        }
                        catch (Exception ex)
                        {
                            OnParsingError?.Invoke(this, $"Ошибка парсинга строки '{line}': {ex.Message}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                OnParsingError?.Invoke(this, $"Критическая ошибка парсинга: {ex.Message}");
            }

            return lsrList;
        }

        /// <summary>
        /// парсинг ответа команды "bkr" (проверка статуса сбора)
        /// возвращает статус сбора: 0 = завершен, 4 = в процессе 
        /// </summary>
        /// <param name="responseData"></param>
        /// <returns></returns>
        public int ParseBkrStatus(string responseData)
        {
            try
            {
                if (string.IsNullOrEmpty(responseData))
                    return -1;

                // ищем паттерн [0] 0 или [0] 4
                Regex regex = new Regex(@"\[0\]\s+(\d+)");
                Match match = regex.Match(responseData);

                if (match.Success)
                {
                    if (int.TryParse(match.Groups[1].Value, out int status))
                    {
                        return status; // 0 = завершен, 4 = в процессе
                    }
                }
            }
            catch (Exception ex)
            {
                OnParsingError?.Invoke(this, $"Ошибка парсинга BKR статуса: {ex.Message}");
            }

            return -1;
        }

        /// <summary>
        /// парсинг ответа команды "exe 2561 phy ipaddr" (получить IP адрес)
        /// </summary>
        public string ParsePhyIpAddr(string responseData)
        {
            try
            {
                if (string.IsNullOrEmpty(responseData))
                    return string.Empty;

                // Ищем IP адрес в формате XXX.XXX.XXX.XXX
                Regex regex = new Regex(@"(\d{1,3}\.\d{1,3}\.\d{1,3}\.\d{1,3})");
                Match match = regex.Match(responseData);

                if (match.Success)
                {
                    return match.Groups[1].Value;
                }
            }
            catch (Exception ex)
            {
                OnParsingError?.Invoke(this, $"Ошибка парсинга IP адреса: {ex.Message}");
            }

            return string.Empty;
        }

        /// <summary>
        /// парсинг ответа команды "exe 2561 wwdg" (проверить WWDG)
        /// возвращает true если WWDG включен (ответ содержит 1)
        /// </summary>
        public bool ParseWwdgStatus(string responseData)
        {
            try
            {
                if (string.IsNullOrEmpty(responseData))
                    return false;

                // Ищем значение 1 или 0
                if (responseData.Contains("1"))
                    return true;

                return false;
            }
            catch (Exception ex)
            {
                OnParsingError?.Invoke(this, $"Ошибка парсинга WWDG статуса: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// парсинг ответа команды "exe 2561 sys info" (системная информация)
        /// возвращает словарь с информацией
        /// </summary>
        public Dictionary<string, string> ParseSysInfo(string responseData)
        {
            var info = new Dictionary<string, string>();

            try
            {
                if (string.IsNullOrEmpty(responseData))
                    return info;

                string[] lines = responseData.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);

                foreach (string line in lines)
                {
                    if (string.IsNullOrWhiteSpace(line) || !line.Contains(":"))
                        continue;

                    string[] parts = line.Split(new[] { ":" }, StringSplitOptions.None);
                    if (parts.Length >= 2)
                    {
                        string key = parts[0].Trim();
                        string value = parts[1].Trim();
                        info[key] = value;
                    }
                }
            }
            catch (Exception ex)
            {
                OnParsingError?.Invoke(this, $"Ошибка парсинга sys info: {ex.Message}");
            }

            return info;
        }

        /// <summary>
        /// проверка, указывает ли ответ на ошибку
        /// </summary>
        public bool IsErrorResponse(string responseData)
        {
            if (string.IsNullOrEmpty(responseData))
                return false;

            return responseData.Contains("Error")
                || responseData.Contains("error")
                || responseData.Contains("ERROR")
                || responseData.Contains("failed")
                || responseData.Contains("Failed")
                || responseData.Contains("FAILED");
        }

        /// <summary>
        /// получение сообщения об ошибке из ответа
        /// </summary>
        public string ExtractErrorMessage(string responseData)
        {
            if (string.IsNullOrEmpty(responseData))
                return string.Empty;

            // Ищем строки, содержащие ошибку
            string[] lines = responseData.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
            var errorLines = lines.Where(l => !string.IsNullOrWhiteSpace(l)
                && (l.Contains("Error") || l.Contains("error")
                || l.Contains("ERROR") || l.Contains("failed"))).ToList();

            return string.Join(" | ", errorLines);
        }
    }
}

