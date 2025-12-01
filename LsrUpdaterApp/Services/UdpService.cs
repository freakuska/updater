using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace LsrUpdaterApp.Services
{
    /// <summary>
    /// сервис для работы с UDP соединением к БКР
    /// управляет отправкой команд и получением ответов
    /// </summary>
    public class UdpService : IDisposable
    {
        private UdpClient _udpClient;
        private string _bkrIp;
        private int _bkrPort;
        private bool _isConnected;
        private byte[] _receiveBuffer;
        private IPEndPoint _remoteIpEndPoint;
        private int _commandTimeoutMs;

        /// <summary>
        /// событие получения данных от БКР
        /// </summary>
        public event EventHandler<string> OnDataReceived;

        /// <summary>
        /// ошибка при работе с UDP
        /// </summary>
        public event EventHandler<string> OnError;

        /// <summary>
        /// событие изменения статуса подключения
        /// </summary>
        public event EventHandler<bool> OnConnectionStatusChanged;

        /// <summary>
        /// конструктор
        /// </summary>
        /// <param name="bkrIp">IP-адрес БКР (по умолчанию 10.0.1.89)</param>
        /// <param name="bkrPort">порт БКР (по умолчанию 3456)</param>
        /// <param name="commandTimeoutMs">таймаут команды в миллисекундах (по умолчанию 3000)</param>
        public UdpService(string bkrIp = "10.0.1.89", int bkrPort = 3456, int commandTimeoutMs = 3000)
        {
            _bkrIp = bkrIp;
            _bkrPort = bkrPort;
            _isConnected = false;
            _commandTimeoutMs = commandTimeoutMs;
            _receiveBuffer = new byte[65507]; // максимальный размер UDP пакета
            _remoteIpEndPoint = new IPEndPoint(IPAddress.Any, 0);
        }

        /// <summary>
        /// свойство для проверки статуса подключения
        /// </summary>
        public bool IsConnected => _isConnected && _udpClient != null;

        /// <summary>
        /// подключение к БКР
        /// </summary>
        /// <returns></returns>
        public async Task<bool> ConnectAsync()
        {
            return await Task.Run(() =>
            {
                try
                {
                    Disconnect();
                    _udpClient = new UdpClient();
                    _udpClient.Connect(_bkrIp, _bkrPort);
                    _isConnected = true;

                    OnConnectionStatusChanged?.Invoke(this, true);
                    OnDataReceived?.Invoke(this, $"✅ Подключено к БКР {_bkrIp}:{_bkrPort}");

                    return true;
                }
                catch (Exception ex)
                {
                    _isConnected = false;
                    OnConnectionStatusChanged?.Invoke(this, false);
                    OnError?.Invoke(this, $"Ошибка подключения к БКР: {ex.Message}");
                    return false;
                }
            });
        }
            

        /// <summary>
        /// отключение от БКР и очистка ресурсов 
        /// </summary>
        public void Disconnect()
        {
            try
            {
                if (_udpClient != null)
                {
                    _udpClient.Close();
                    _udpClient.Dispose();
                    _udpClient = null;
                    _isConnected = false;
                    OnConnectionStatusChanged?.Invoke(this, false);
                    OnDataReceived?.Invoke(this, "❌ Отключено от БКР");
                }
            }
            catch (Exception ex)
            {
                OnError?.Invoke(this, $"Ошибка при отключении: {ex.Message}");
            }

        }

        /// <summary>
        /// отправка команды в БКР и получение ответа с таймаутом
        /// </summary>
        /// <param name="command"></param>
        /// <param name="timeoutMs"></param>
        /// <returns></returns>
        public async Task<string> SendCommandAsync(string command, int timeoutMs = -1)
        {
            if (!_isConnected || _udpClient == null)
            {
                OnError?.Invoke(this, "UDP клиент не подключен");
                return string.Empty;
            }

            int timeout = timeoutMs > 0 ? timeoutMs : _commandTimeoutMs;

            try
            {
                // отправляем команду
                byte[] data = Encoding.UTF8.GetBytes(command + "\n");
                await _udpClient.SendAsync(data, data.Length);

                OnDataReceived?.Invoke(this, $"→ Отправлено: {command}");

                // получаем ответ с таймаутом
                using (CancellationTokenSource cts = new CancellationTokenSource(timeout))
                {
                    var result = await _udpClient.ReceiveAsync();
                    string response = Encoding.UTF8.GetString(result.Buffer);
                    OnDataReceived?.Invoke(this, $"← Получено: {response.Substring(0, Math.Min(100, response.Length))}");
                    return response;
                }
            }
            catch (OperationCanceledException)
            {
                OnError?.Invoke(this, $"Таймаут при выполнении команды: {command} (ожидание {timeout}ms)");
                return string.Empty;
            }
            catch (Exception ex)
            {
                OnError?.Invoke(this, $"Ошибка отправки команды '{command}': {ex.Message}");
                return string.Empty;
            }
        }

        /// <summary>
        /// отправка команды без ожидания ответа (Fire and Forget)
        /// используется для длительных операций
        /// </summary>
        /// <param name="command"></param>
        /// <returns></returns>
        public async Task<bool> SendCommandFireAndForgetAsync(string command)
        {
            if (!_isConnected || _udpClient == null)
            {
                OnError?.Invoke(this, "UDP клиент не подключен");
                return false;
            }

            try
            {
                byte[] data = Encoding.UTF8.GetBytes(command + "\n");
                await _udpClient.SendAsync(data, data.Length);
                OnDataReceived?.Invoke(this, $"→ Отправлено (без ответа): {command}");
                return true;
            }
            catch (Exception ex)
            {
                OnError?.Invoke(this, $"Ошибка отправки команды: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// отправка Enter (для нажатия Enter в терминале)
        /// </summary>
        public async Task<bool> SendEnterAsync()
        {
            return await SendCommandFireAndForgetAsync(string.Empty);
        }

        /// <summary>
        /// отправка Ctrl+C (для выхода из команды)
        /// </summary>
        public async Task<bool> SendCtrlCAsync()
        {
            return await SendCommandFireAndForgetAsync("\u0003");
        }
 
        /// <summary>
        /// очистка ресурсов
        /// </summary>
        public void Dispose()
        {
            Disconnect();
        }

        /// <summary>
        /// установка нового IP-адреса БКР
        /// </summary>
        /// <param name="ip"></param>
        /// <param name="port"></param>
        public void SetBkrAddress(string ip, int port)
        {
            _bkrIp = ip;
            _bkrPort = port;
            if(_isConnected)
            {
                Disconnect();
            }
        }

        /// <summary>
        /// установка таймаута для команд
        /// </summary>
        /// <param name="timeoutMs"></param>
        public void SetCommandTimeOut(int timeoutMs)
        {
            _commandTimeoutMs = timeoutMs;
        }
    }
}
