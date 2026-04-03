using System;
using System.Globalization;
using System.IO.Ports;
using System.Text.RegularExpressions;
using System.Threading;

namespace SystemeCaisse.UI.Services
{
    /// <summary>
    /// Service de communication avec une balance Adam Equipment Swift SWZ via RS-232.
    /// Supporte les modes Continu (PC), Commande et Impression.
    /// Format de données attendu (Format 3) : "+ 0.200kg<cr><lf>"
    /// </summary>
    public class SerialScaleService : IDisposable
    {
        private SerialPort? _serialPort;
        private string _buffer = string.Empty;
        private readonly object _lock = new();
        private bool _disposed;

        /// <summary>Déclenché à chaque nouveau poids lu.</summary>
        public event Action<decimal>? WeightChanged;

        /// <summary>Déclenché quand le statut de connexion change.</summary>
        public event Action<string>? StatusChanged;

        /// <summary>Déclenché pour chaque ligne brute reçue (debug).</summary>
        public event Action<string>? RawDataReceived;

        /// <summary>Indique si le port est ouvert et la balance connectée.</summary>
        public bool IsConnected => _serialPort?.IsOpen == true;

        /// <summary>Dernier poids lu (en kg).</summary>
        public decimal CurrentWeight { get; private set; }

        /// <summary>
        /// Ouvre le port série et commence l'écoute des données de la balance.
        /// </summary>
        /// <param name="portName">Nom du port (ex: COM3)</param>
        /// <param name="baudRate">Vitesse de transmission (défaut: 9600)</param>
        public void Start(string portName, int baudRate = 9600)
        {
            Stop(); // Fermer toute connexion précédente

            try
            {
                _serialPort = new SerialPort
                {
                    PortName = portName,
                    BaudRate = baudRate,
                    DataBits = 8,
                    Parity = Parity.None,
                    StopBits = StopBits.One,
                    Handshake = Handshake.None,
                    ReadTimeout = 2000,
                    WriteTimeout = 1000,
                    Encoding = System.Text.Encoding.ASCII,
                    NewLine = "\r\n",
                    // DTR/RTS activés pour certaines balances qui en ont besoin
                    DtrEnable = true,
                    RtsEnable = true
                };

                _serialPort.DataReceived += OnDataReceived;
                _serialPort.ErrorReceived += OnErrorReceived;

                _serialPort.Open();

                StatusChanged?.Invoke("Connecté");
            }
            catch (UnauthorizedAccessException)
            {
                StatusChanged?.Invoke("Port occupé");
                throw new InvalidOperationException($"Le port {portName} est déjà utilisé par une autre application.");
            }
            catch (System.IO.FileNotFoundException)
            {
                StatusChanged?.Invoke("Port introuvable");
                throw new InvalidOperationException($"Le port {portName} n'existe pas. Vérifiez la connexion USB.");
            }
            catch (Exception ex)
            {
                StatusChanged?.Invoke("Erreur connexion");
                throw new InvalidOperationException($"Impossible d'ouvrir {portName}: {ex.Message}");
            }
        }

        /// <summary>
        /// Ferme le port série et arrête l'écoute.
        /// </summary>
        public void Stop()
        {
            if (_serialPort != null)
            {
                try
                {
                    _serialPort.DataReceived -= OnDataReceived;
                    _serialPort.ErrorReceived -= OnErrorReceived;

                    if (_serialPort.IsOpen)
                    {
                        _serialPort.Close();
                    }

                    _serialPort.Dispose();
                }
                catch { /* Ignore errors during cleanup */ }
                finally
                {
                    _serialPort = null;
                    _buffer = string.Empty;
                    StatusChanged?.Invoke("Déconnecté");
                }
            }
        }

        /// <summary>
        /// Envoie une commande à la balance (P, T ou Z).
        /// Adam Equipment SWZ : Toutes les commandes doivent être en majuscules + CR LF.
        /// </summary>
        public void SendCommand(string command)
        {
            if (_serialPort?.IsOpen == true)
            {
                try
                {
                    _serialPort.Write(command.ToUpper() + "\r\n");
                }
                catch (Exception ex)
                {
                    StatusChanged?.Invoke($"Erreur envoi: {ex.Message}");
                }
            }
        }

        /// <summary>Envoie la commande Tare (T) à la balance.</summary>
        public void Tare() => SendCommand("T");

        /// <summary>Envoie la commande Zéro (Z) à la balance.</summary>
        public void Zero() => SendCommand("Z");

        /// <summary>Demande le poids actuel (P) en mode Commande.</summary>
        public void RequestWeight() => SendCommand("P");

        /// <summary>
        /// Teste la connexion en ouvrant le port brièvement.
        /// Retourne true si le port peut être ouvert.
        /// </summary>
        public static bool TestConnection(string portName, int baudRate = 9600)
        {
            try
            {
                using var testPort = new SerialPort(portName, baudRate, Parity.None, 8, StopBits.One);
                testPort.ReadTimeout = 1000;
                testPort.Open();
                // Try to send a Print command and wait for response
                testPort.Write("P\r\n");
                Thread.Sleep(500);
                bool hasData = testPort.BytesToRead > 0;
                testPort.Close();
                return true; // Port opens successfully
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Retourne la liste des ports COM disponibles sur le système.
        /// </summary>
        public static string[] GetAvailablePorts() => SerialPort.GetPortNames();

        private void OnDataReceived(object sender, SerialDataReceivedEventArgs e)
        {
            if (_serialPort == null || !_serialPort.IsOpen) return;

            try
            {
                string data = _serialPort.ReadExisting();
                
                lock (_lock)
                {
                    _buffer += data;

                    // Traiter chaque ligne complète (terminée par \r\n ou \n)
                    while (_buffer.Contains('\n'))
                    {
                        int idx = _buffer.IndexOf('\n');
                        string line = _buffer.Substring(0, idx).Trim('\r', '\n', ' ');
                        _buffer = _buffer.Substring(idx + 1);

                        if (!string.IsNullOrWhiteSpace(line))
                        {
                            RawDataReceived?.Invoke(line);
                            ParseWeight(line);
                        }
                    }

                    // Prevent buffer overflow
                    if (_buffer.Length > 1024)
                    {
                        _buffer = _buffer.Substring(_buffer.Length - 256);
                    }
                }
            }
            catch (Exception ex)
            {
                StatusChanged?.Invoke($"Erreur lecture: {ex.Message}");
            }
        }

        private void OnErrorReceived(object sender, SerialErrorReceivedEventArgs e)
        {
            StatusChanged?.Invoke($"Erreur série: {e.EventType}");
        }

        /// <summary>
        /// Parse le poids depuis une ligne de données de la balance.
        /// Supporte multiples formats Adam Equipment SWZ :
        /// - Format 3 simple : "+ 0.200kg" ou "- 1.500kg"
        /// - Format avec statut : "ST,GS,+  0.200  kg"
        /// - Poids brut numérique : "0.200"
        /// </summary>
        private void ParseWeight(string line)
        {
            decimal weight = 0;
            bool parsed = false;

            // ===== Format 3 Adam Equipment SWZ : "+ 0.200kg" ou "+0.200 kg" =====
            // Regex: optional sign, spaces, decimal number, optional spaces, optional unit
            var match = Regex.Match(line, @"([+-])?\s*(\d+\.?\d*)\s*(kg|g|lb|oz)?", RegexOptions.IgnoreCase);
            if (match.Success)
            {
                string numStr = match.Groups[2].Value;
                if (decimal.TryParse(numStr, NumberStyles.Any, CultureInfo.InvariantCulture, out weight))
                {
                    // Apply sign
                    if (match.Groups[1].Value == "-")
                        weight = -weight;

                    // Convert grams to kg if unit is 'g'
                    if (match.Groups[3].Success && match.Groups[3].Value.Equals("g", StringComparison.OrdinalIgnoreCase))
                        weight /= 1000m;

                    parsed = true;
                }
            }

            if (parsed)
            {
                CurrentWeight = weight;
                WeightChanged?.Invoke(weight);
            }
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
                Stop();
            }
        }
    }
}
