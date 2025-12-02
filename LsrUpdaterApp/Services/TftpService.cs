using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace LsrUpdaterApp.Services
{
    /// <summary>
    /// TFTP-клиент (RFC 1350) - написано с нуля на чистом UDP
    /// Поддерживает upload и download прошивок
    /// </summary>
    public class TftpService : IDisposable
    {
        private const int TFTP_PORT = 69;
        private const int BLOCK_SIZE = 512;
        private const int TIMEOUT = 5000; // 5 сек
        private const int MAX_RETRIES = 3;

        private readonly string _serverIp;
        private bool _disposed;

        public event EventHandler<string> OnProgress;
        public event EventHandler<string> OnError;
        public event EventHandler<string> OnSuccess;

        public TftpService(string serverIp = "10.0.1.89")
        {
            _serverIp = serverIp;
        }

        /// <summary>
        /// UPLOAD прошивки (PUT файла на сервер)
        /// </summary>
        public async Task<bool> SendFirmwareAsync(string localFilePath, string remoteFileName)
        {
            if (!File.Exists(localFilePath))
            {
                OnError?.Invoke(this, $"❌ Файл не найден: {localFilePath}");
                return false;
            }

            return await Task.Run(() =>
            {
                using (var client = new UdpClient())
                {
                    try
                    {
                        var fileInfo = new FileInfo(localFilePath);
                        long fileLength = fileInfo.Length;
                        long transferred = 0;

                        OnProgress?.Invoke(this, $"📡 TFTP: Подключение к {_serverIp}:69");
                        OnProgress?.Invoke(this, $"📦 TFTP: Отправка {remoteFileName} ({fileLength} байт)");

                        client.Client.SendTimeout = TIMEOUT;
                        client.Client.ReceiveTimeout = TIMEOUT;

                        // 1. Отправляем WRQ (Write Request)
                        byte[] wrqPacket = BuildWRQPacket(remoteFileName);
                        client.Send(wrqPacket, wrqPacket.Length, _serverIp, TFTP_PORT);

                        IPEndPoint remoteEp = new IPEndPoint(IPAddress.Parse(_serverIp), TFTP_PORT);
                        byte[] ackBuffer = client.Receive(ref remoteEp);

                        if (ackBuffer.Length < 4)
                        {
                            OnError?.Invoke(this, "❌ TFTP: Ошибка подтверждения начала передачи");
                            return false;
                        }

                        short blockNum = 0;

                        // 2. Читаем файл блоками и отправляем
                        using (var fileStream = File.OpenRead(localFilePath))
                        {
                            byte[] buffer = new byte[BLOCK_SIZE];
                            int bytesRead;

                            while ((bytesRead = fileStream.Read(buffer, 0, BLOCK_SIZE)) > 0)
                            {
                                blockNum++;
                                byte[] dataPacket = BuildDATAPacket(blockNum, buffer, bytesRead);

                                client.Send(dataPacket, dataPacket.Length, remoteEp);

                                // Ждём ACK
                                ackBuffer = client.Receive(ref remoteEp);
                                if (ackBuffer.Length >= 4)
                                {
                                    short ackNum = (short)((ackBuffer[2] << 8) | ackBuffer[3]);
                                    if (ackNum != blockNum)
                                    {
                                        OnError?.Invoke(this, $"❌ TFTP: Ошибка номера блока (ожидалось {blockNum}, получено {ackNum})");
                                        return false;
                                    }
                                }

                                transferred += bytesRead;
                                double percent = (transferred / (double)fileLength) * 100;
                                OnProgress?.Invoke(this, $"📊 TFTP: {percent:F1}% ({transferred}/{fileLength} байт)");
                            }
                        }

                        OnSuccess?.Invoke(this, $"✅ TFTP: Файл {remoteFileName} успешно передан ({fileLength} байт)");
                        return true;
                    }
                    catch (Exception ex)
                    {
                        OnError?.Invoke(this, $"❌ TFTP Upload: {ex.Message}");
                        return false;
                    }
                }
            });
        }

        /// <summary>
        /// DOWNLOAD файла (GET файла с сервера)
        /// </summary>
        public async Task<bool> ReceiveFileAsync(string remoteFileName, string localFilePath)
        {
            return await Task.Run(() =>
            {
                using (var client = new UdpClient())
                {
                    try
                    {
                        OnProgress?.Invoke(this, $"📥 TFTP: Загрузка {remoteFileName} с {_serverIp}...");

                        client.Client.SendTimeout = TIMEOUT;
                        client.Client.ReceiveTimeout = TIMEOUT;

                        // 1. Отправляем RRQ (Read Request)
                        byte[] rrqPacket = BuildRRQPacket(remoteFileName);
                        client.Send(rrqPacket, rrqPacket.Length, _serverIp, TFTP_PORT);

                        IPEndPoint remoteEp = new IPEndPoint(IPAddress.Parse(_serverIp), TFTP_PORT);
                        long totalReceived = 0;

                        // 2. Получаем файл блоками
                        using (var fileStream = File.Create(localFilePath))
                        {
                            short lastBlockNum = 0;

                            while (true)
                            {
                                byte[] dataBuffer = client.Receive(ref remoteEp);

                                if (dataBuffer.Length < 4)
                                    break;

                                short opCode = (short)((dataBuffer[0] << 8) | dataBuffer[1]);
                                short blockNum = (short)((dataBuffer[2] << 8) | dataBuffer[3]);

                                // Отправляем ACK
                                byte[] ackPacket = BuildACKPacket(blockNum);
                                client.Send(ackPacket, ackPacket.Length, remoteEp);

                                if (opCode == 3) // DATA packet
                                {
                                    int dataLength = dataBuffer.Length - 4;
                                    fileStream.Write(dataBuffer, 4, dataLength);
                                    totalReceived += dataLength;

                                    OnProgress?.Invoke(this, $"📥 TFTP: {totalReceived} байт");

                                    // Если блок меньше размера, это последний блок
                                    if (dataLength < BLOCK_SIZE)
                                        break;

                                    lastBlockNum = blockNum;
                                }
                            }
                        }

                        OnSuccess?.Invoke(this, $"✅ TFTP: Файл загружен ({totalReceived} байт) → {localFilePath}");
                        return true;
                    }
                    catch (Exception ex)
                    {
                        OnError?.Invoke(this, $"❌ TFTP Download: {ex.Message}");
                        return false;
                    }
                }
            });
        }

        #region TFTP Packet Builders

        /// <summary>
        /// Построить WRQ (Write Request) пакет
        /// Формат: opcode (2 байта) + filename + 0 + mode + 0
        /// </summary>
        private byte[] BuildWRQPacket(string filename)
        {
            using (var ms = new MemoryStream())
            {
                ms.WriteByte(0x00);
                ms.WriteByte(0x02); // WRQ opcode
                WriteString(ms, filename);
                WriteString(ms, "octet"); // Binary mode
                return ms.ToArray();
            }
        }

        /// <summary>
        /// Построить RRQ (Read Request) пакет
        /// Формат: opcode (2 байта) + filename + 0 + mode + 0
        /// </summary>
        private byte[] BuildRRQPacket(string filename)
        {
            using (var ms = new MemoryStream())
            {
                ms.WriteByte(0x00);
                ms.WriteByte(0x01); // RRQ opcode
                WriteString(ms, filename);
                WriteString(ms, "octet"); // Binary mode
                return ms.ToArray();
            }
        }

        /// <summary>
        /// Построить DATA пакет
        /// Формат: opcode (2) + block# (2) + data (0-512)
        /// </summary>
        private byte[] BuildDATAPacket(short blockNum, byte[] data, int length)
        {
            byte[] packet = new byte[4 + length];
            packet[0] = 0x00;
            packet[1] = 0x03; // DATA opcode
            packet[2] = (byte)(blockNum >> 8);
            packet[3] = (byte)(blockNum & 0xFF);
            Array.Copy(data, 0, packet, 4, length);
            return packet;
        }

        /// <summary>
        /// Построить ACK (Acknowledgement) пакет
        /// Формат: opcode (2) + block# (2)
        /// </summary>
        private byte[] BuildACKPacket(short blockNum)
        {
            byte[] packet = new byte[4];
            packet[0] = 0x00;
            packet[1] = 0x04; // ACK opcode
            packet[2] = (byte)(blockNum >> 8);
            packet[3] = (byte)(blockNum & 0xFF);
            return packet;
        }

        /// <summary>
        /// Записать строку в TFTP формате (строка + null terminator)
        /// </summary>
        private void WriteString(MemoryStream ms, string str)
        {
            byte[] strBytes = System.Text.Encoding.ASCII.GetBytes(str);
            ms.Write(strBytes, 0, strBytes.Length);
            ms.WriteByte(0x00);
        }

        #endregion

        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
            }
        }
    }
}
