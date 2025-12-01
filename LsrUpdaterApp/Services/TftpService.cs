using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace LsrUpdaterApp.Services
{
    /// <summary>
    /// реализация TFTP протокола без внешних зависимостей 
    /// </summary>
    public class TftpService
    {
        private const int TFTP_PORT = 69;
        private const int BLOCK_SIZE = 512;
        private const int TIMEOUT_MS = 5000;
        private const int MAX_RETRIES = 3;

        private readonly string _serverIp;
        private UdpClient _udpClient;
        private bool _disposed = false;

        public event EventHandler<string> OnProgress;
        public event EventHandler<string> OnError;
        public event EventHandler<string> OnSuccess;

        public TftpService(string serverIp = "10.0.1.89")
        {
            _serverIp = serverIp;
        }

        /// <summary>
        /// отправка файла прошивки на устройство через TFTP
        /// </summary>
        /// <param name="localFilePath"></param>
        /// <param name="remoteFileName"></param>
        /// <returns></returns>
        public async Task<bool> SendFirmwareAsync(string localFilePath, string remoteFileName)
        {
            if (!File.Exists(localFilePath))
            {
                OnError?.Invoke(this, $"❌ Файл не найден: {localFilePath}");
                return false;
            }

            return await Task.Run(() =>
            {
                try
                {
                    var fileInfo = new FileInfo(localFilePath);
                    long fileLength = fileInfo.Length;

                    OnProgress?.Invoke(this, $"📡 TFTP: Инициализация подключения к {_serverIp}:69");
                    OnProgress?.Invoke(this, $"📦 TFTP: Отправка файла {remoteFileName} ({fileLength} байт)");

                    using (var fileStream = new FileStream(localFilePath, FileMode.Open, FileAccess.Read))
                    {
                        byte[] buffer = new byte[BLOCK_SIZE];
                        int bytesRead;
                        ushort blockNumber = 1;
                        bool success = true;
                        int retryCount = 0;

                        _udpClient = new UdpClient();
                        _udpClient.Client.ReceiveTimeout = TIMEOUT_MS;
                        _udpClient.Client.SendTimeout = TIMEOUT_MS;

                        IPEndPoint serverEndPoint = new IPEndPoint(IPAddress.Parse(_serverIp), TFTP_PORT);

                        // шаг 1 - отправка WRQ (Write Request)
                        byte[] wrqPacket = CreateWriteRequest(remoteFileName);
                        OnProgress?.Invoke(this, $"📨 TFTP: Отправка WRQ запроса...");

                        _udpClient.Send(wrqPacket, wrqPacket.Length, serverEndPoint);

                        // шаг 2 - получение ACK от сервера
                        try
                        {
                            IPEndPoint remoteEp = new IPEndPoint(IPAddress.Any, 0);
                            byte[] ackBuffer = _udpClient.Receive(ref remoteEp);

                            if (!IsAck(ackBuffer, 0))
                            {
                                OnError?.Invoke(this, "❌ TFTP: Не получен ACK на WRQ");
                                return false;
                            }

                            serverEndPoint = remoteEp;
                            OnProgress?.Invoke(this, $"✅ TFTP: Сервер готов, начинаем передачу...");
                        }
                        catch (IOException)
                        {
                            OnError?.Invoke(this, "❌ TFTP: Таймаут при получении ACK на WRQ");
                            return false;
                        }

                        // шаг 3 - передача блоков данных
                        long totalBytesSent = 0;

                        while ((bytesRead = fileStream.Read(buffer, 0, BLOCK_SIZE)) > 0)
                        {
                            byte[] dataPacket = CreateDataPacket(blockNumber, buffer, bytesRead);

                            // повтор отправки, если нет ответа
                            for (int attempt = 0; attempt < MAX_RETRIES; attempt++)
                            {
                                try
                                {
                                    _udpClient.Send(dataPacket, dataPacket.Length, serverEndPoint);
                                    totalBytesSent += bytesRead;

                                    // Получаем ACK
                                    IPEndPoint remoteEp = new IPEndPoint(IPAddress.Any, 0);
                                    byte[] ackBuffer = _udpClient.Receive(ref remoteEp);

                                    if (IsAck(ackBuffer, blockNumber))
                                    {
                                        double progressPercent = (totalBytesSent / (double)fileLength) * 100;
                                        OnProgress?.Invoke(this,
                                            $"📊 TFTP: {totalBytesSent}/{fileLength} байт ({progressPercent:F1}%) [блок {blockNumber}]");
                                        break;
                                    }
                                }
                                catch (IOException)
                                {
                                    if (attempt == MAX_RETRIES - 1)
                                    {
                                        OnError?.Invoke(this, $"❌ TFTP: Ошибка передачи блока {blockNumber}");
                                        success = false;
                                        break;
                                    }
                                    OnProgress?.Invoke(this, $"⚠️  TFTP: Повтор блока {blockNumber}...");
                                }
                            }

                            if (!success) break;

                            blockNumber++;
                            if (blockNumber > 65535) blockNumber = 1;

                            if (bytesRead < BLOCK_SIZE) break; // последний блок
                        }

                        _udpClient?.Close();

                        if (success)
                        {
                            OnSuccess?.Invoke(this,
                                $"✅ TFTP: Файл {remoteFileName} успешно передан ({totalBytesSent} байт)");
                            return true;
                        }
                        else
                        {
                            OnError?.Invoke(this, "❌ TFTP: Ошибка при передаче файла");
                            return false;
                        }
                    }
                }
                catch (Exception ex)
                {
                    OnError?.Invoke(this, $"❌ TFTP: {ex.Message}");
                    return false;
                }
                finally
                {
                    _udpClient?.Close();
                }
            });
        }

        /// <summary>
        /// получение файла с устройства через TFTP
        /// </summary>
        public async Task<bool> ReceiveFileAsync(string remoteFileName, string localFilePath)
        {
            return await Task.Run(() =>
            {
                try
                {
                    OnProgress?.Invoke(this, $"📥 TFTP: Загрузка файла {remoteFileName} с {_serverIp}...");

                    using (var fileStream = new FileStream(localFilePath, FileMode.Create, FileAccess.Write))
                    {
                        _udpClient = new UdpClient();
                        _udpClient.Client.ReceiveTimeout = TIMEOUT_MS;
                        _udpClient.Client.SendTimeout = TIMEOUT_MS;

                        IPEndPoint serverEndPoint = new IPEndPoint(IPAddress.Parse(_serverIp), TFTP_PORT);

                        // отправка RRQ (Read Request)
                        byte[] rrqPacket = CreateReadRequest(remoteFileName);
                        _udpClient.Send(rrqPacket, rrqPacket.Length, serverEndPoint);
                        OnProgress?.Invoke(this, $"📨 TFTP: Отправка RRQ запроса...");

                        IPEndPoint remoteEp = new IPEndPoint(IPAddress.Any, 0);
                        long totalBytesReceived = 0;
                        ushort expectedBlockNumber = 1;

                        while (true)
                        {
                            try
                            {
                                byte[] dataBuffer = _udpClient.Receive(ref remoteEp);

                                if (IsData(dataBuffer, expectedBlockNumber))
                                {
                                    int dataLength = dataBuffer.Length - 4;
                                    fileStream.Write(dataBuffer, 4, dataLength);
                                    totalBytesReceived += dataLength;

                                    OnProgress?.Invoke(this,
                                        $"📥 TFTP: {totalBytesReceived} байт [блок {expectedBlockNumber}]");

                                    // отправка ACK
                                    byte[] ackPacket = CreateAck(expectedBlockNumber);
                                    _udpClient.Send(ackPacket, ackPacket.Length, remoteEp);

                                    if (dataLength < BLOCK_SIZE)
                                    {
                                        OnSuccess?.Invoke(this,
                                            $"✅ TFTP: Файл загружен успешно ({totalBytesReceived} байт)");
                                        return true;
                                    }

                                    expectedBlockNumber++;
                                    if (expectedBlockNumber > 65535) expectedBlockNumber = 1;
                                }
                            }
                            catch (IOException)
                            {
                                OnError?.Invoke(this, "❌ TFTP: Таймаут при получении данных");
                                return false;
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    OnError?.Invoke(this, $"❌ TFTP: Ошибка загрузки: {ex.Message}");
                    return false;
                }
                finally
                {
                    _udpClient?.Close();
                }
            });
        }

        // ==================== ВСПОМОГАТЕЛЬНЫЕ МЕТОДЫ ====================

        private byte[] CreateWriteRequest(string filename)
        {
            using (var ms = new MemoryStream())
            {
                ms.WriteByte(0x00);
                ms.WriteByte(0x02);
                ms.Write(System.Text.Encoding.ASCII.GetBytes(filename), 0, filename.Length);
                ms.WriteByte(0x00);
                ms.Write(System.Text.Encoding.ASCII.GetBytes("octet"), 0, 5);
                ms.WriteByte(0x00);
                return ms.ToArray();
            }
        }

        private byte[] CreateReadRequest(string filename)
        {
            using (var ms = new MemoryStream())
            {
                ms.WriteByte(0x00);
                ms.WriteByte(0x01);
                ms.Write(System.Text.Encoding.ASCII.GetBytes(filename), 0, filename.Length);
                ms.WriteByte(0x00);
                ms.Write(System.Text.Encoding.ASCII.GetBytes("octet"), 0, 5);
                ms.WriteByte(0x00);
                return ms.ToArray();
            }
        }

        private byte[] CreateDataPacket(ushort blockNumber, byte[] data, int length)
        {
            byte[] packet = new byte[length + 4];
            packet[0] = 0x00;
            packet[1] = 0x03;
            packet[2] = (byte)((blockNumber >> 8) & 0xFF);
            packet[3] = (byte)(blockNumber & 0xFF);
            Array.Copy(data, 0, packet, 4, length);
            return packet;
        }

        private byte[] CreateAck(ushort blockNumber)
        {
            byte[] ack = new byte[4];
            ack[0] = 0x00;
            ack[1] = 0x04;
            ack[2] = (byte)((blockNumber >> 8) & 0xFF);
            ack[3] = (byte)(blockNumber & 0xFF);
            return ack;
        }

        private bool IsAck(byte[] packet, ushort expectedBlock)
        {
            if (packet.Length < 4 || packet[0] != 0x00 || packet[1] != 0x04)
                return false;

            ushort blockNumber = (ushort)(((packet[2] & 0xFF) << 8) | (packet[3] & 0xFF));
            return blockNumber == expectedBlock;
        }

        private bool IsData(byte[] packet, ushort expectedBlock)
        {
            if (packet.Length < 4 || packet[0] != 0x00 || packet[1] != 0x03)
                return false;

            ushort blockNumber = (ushort)(((packet[2] & 0xFF) << 8) | (packet[3] & 0xFF));
            return blockNumber == expectedBlock;
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _udpClient?.Close();
                _udpClient?.Dispose();
                _disposed = true;
            }
        }
    }
}
