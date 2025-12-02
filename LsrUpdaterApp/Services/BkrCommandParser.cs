using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using LsrUpdaterApp.Models;

namespace LsrUpdaterApp.Services
{
    public class BkrCommandParser
    {
        public bool IsErrorResponse(string response)
        {
            if (string.IsNullOrEmpty(response))
                return true;

            response = response.ToLower();
            return response.Contains("error") ||
                   response.Contains("err") ||
                   response.Contains("fail") ||
                   response.Contains("unknown") ||
                   response.Contains("invalid");
        }

        public string ExtractErrorMessage(string response)
        {
            if (string.IsNullOrEmpty(response))
                return "Пустой ответ";

            var match = Regex.Match(response, @"(?:error|err|fail):\s*(.+?)(?:\n|$)", RegexOptions.IgnoreCase);
            if (match.Success)
                return match.Groups[1].Value.Trim();

            match = Regex.Match(response, @"(?:error|err|fail)\s*(.+?)$", RegexOptions.IgnoreCase | RegexOptions.Multiline);
            if (match.Success)
                return match.Groups[1].Value.Trim();

            var lines = response.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
            return lines.Length > 0 ? lines[0] : response;
        }

        public int ParseBkrStatus(string response)
        {
            if (string.IsNullOrEmpty(response))
                return -1;

            var match = Regex.Match(response, @"\[(\d+)\]\s+(\d+)");
            if (match.Success)
            {
                if (int.TryParse(match.Groups[2].Value, out int status))
                    return status;
            }

            return -1;
        }

        public List<LsrInfo> ParserLsrListVersions(string response)
        {
            var lsrList = new List<LsrInfo>();

            if (string.IsNullOrEmpty(response))
                return lsrList;

            try
            {
                var lines = response.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);

                foreach (var line in lines)
                {
                    var trimmed = line.Trim();
                    if (string.IsNullOrEmpty(trimmed))
                        continue;

                    var parts = trimmed.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);

                    if (parts.Length >= 3)
                    {
                        string id = parts[0];
                        string ip = parts[1];
                        string version = parts[2];

                        if (version.Contains("?"))
                            continue;

                        var lsr = new LsrInfo(id, ip, version)
                        {
                            NeedsUpdate = true,
                            IsAvailable = true,
                            Status = "🆗 Доступен"
                        };

                        lsrList.Add(lsr);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠️ Ошибка парсинга списка ЛСР: {ex.Message}");
            }

            return lsrList;
        }

        public string ParsePhyIpAddr(string response)
        {
            if (string.IsNullOrEmpty(response))
                return null;

            var match = Regex.Match(response, @"(\d{1,3}\.\d{1,3}\.\d{1,3}\.\d{1,3})");
            if (match.Success)
                return match.Groups[1].Value;

            return null;
        }

        public bool ParseWwdgStatus(string response)
        {
            if (string.IsNullOrEmpty(response))
                return false;

            response = response.Trim();

            if (response.Contains("1"))
                return true;

            if (response.Contains("0"))
                return false;

            var match = Regex.Match(response, @"[\[\s]([01])[\]\s]");
            if (match.Success)
                return match.Groups[1].Value == "1";

            return false;
        }

        public Dictionary<string, string> ParseSysInfo(string response)
        {
            var info = new Dictionary<string, string>();

            if (string.IsNullOrEmpty(response))
                return info;

            try
            {
                var lines = response.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);

                foreach (var line in lines)
                {
                    var trimmed = line.Trim();
                    if (string.IsNullOrEmpty(trimmed))
                        continue;

                    var match = Regex.Match(trimmed, @"^([^:=]+)[:=]\s*(.+)$");
                    if (match.Success)
                    {
                        string key = match.Groups[1].Value.Trim();
                        string value = match.Groups[2].Value.Trim();
                        info[key] = value;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠️ Ошибка парсинга sys info: {ex.Message}");
            }

            return info;
        }

        public bool IsSuccessResponse(string response)
        {
            if (string.IsNullOrEmpty(response))
                return false;

            response = response.ToLower();
            return response.Contains("ok") ||
                   response.Contains("success") ||
                   response.Contains("done") ||
                   (!response.Contains("error") && !response.Contains("fail"));
        }

        public string NormalizeResponse(string response)
        {
            if (string.IsNullOrEmpty(response))
                return string.Empty;

            response = Regex.Replace(response, @"\x1B\[[0-9;]*m", "");
            response = Regex.Replace(response, @"\s+", " ");

            return response.Trim();
        }
    }
}
