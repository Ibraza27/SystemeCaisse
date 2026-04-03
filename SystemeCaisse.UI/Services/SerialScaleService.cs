using System;
using System.Globalization;
using System.IO.Ports;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace SystemeCaisse.UI.Services
{
    /// <summary>
    /// Service ultra-réactif de communication avec une balance Adam Equipment Swift SWZ via RS-232.
    /// 
    /// Architecture : Thread dédié avec lecture active en boucle serrée.
    /// Mode hybride : fonctionne en continu (réception passive) ET en polling actif
    /// (envoi de commande P toutes les ~30ms pour forcer la balance à répondre).
    /// 
    /// CONFIGURATION OPTIMALE :
    /// - Balance : Mode PC/Continuous, Format 3, Baud 115200
    /// - FTDI : Latency Timer = 1ms, Réception/Transmission = 64 octets
    /// - Application : Baud Rate = 115200
    /// </summary>
    public class SerialScaleService : IDisposable
    {
        private SerialPort? _serialPort;
        private Thread? _readThread;
        private volatile bool _running;
        private bool _disposed;

        // Regex pré-compilé en code IL natif
        private static readonly Regex WeightRegex = new(
            @"([+-])?\s*(\d+\.?\d*)\s*(kg|g|lb|oz)?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

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

        /// <summary>Active le mode polling (envoie P\r\n en boucle).</summary>
        public bool PollingEnabled { get; set; } = false;

        /// <summary>Nom du port actuellement ouvert.</summary>
        public string? ActivePortName => _serialPort?.PortName;

        /// <summary>
        /// Ouvre le port série et démarre le thread de lecture ultra-rapide.
        /// </summary>
        public void Start(string portName, int baudRate = 9600)
        {
            Stop();

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

                    // Buffers au minimum absolu
                    ReadBufferSize = 128,
                    WriteBufferSize = 64,

                    // Notification immédiate
                    ReceivedBytesThreshold = 1,

                    // Timeout très court
                    ReadTimeout = 30,
                    WriteTimeout = 30,

                    // Ignorer les zéros parasites
                    DiscardNull = true,

                    Encoding = Encoding.ASCII,
                    NewLine = "\r\n",

                    // FTDI signaux
                    DtrEnable = true,
                    RtsEnable = true
                };

                _serialPort.Open();

                // Purge complète
                _serialPort.DiscardInBuffer();
                _serialPort.DiscardOutBuffer();
                _serialPort.BaseStream.Flush();

                // Thread haute priorité
                _running = true;
                _readThread = new Thread(ReadLoop)
                {
                    Name = "ScaleReader",
                    IsBackground = true,
                    Priority = ThreadPriority.Highest
                };
                _readThread.Start();

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
        /// Boucle de lecture ultra-rapide sur thread dédié (priorité max).
        /// Lit en continu + polling actif optionnel.
        /// </summary>
        private void ReadLoop()
        {
            var buffer = new StringBuilder(32);
            int pollCounter = 0;

            while (_running)
            {
                try
                {
                    if (_serialPort == null || !_serialPort.IsOpen)
                    {
                        Thread.Sleep(50);
                        continue;
                    }

                    // === POLLING ACTIF : envoyer P toutes les ~30ms ===
                    if (PollingEnabled)
                    {
                        pollCounter++;
                        if (pollCounter >= 3) // Toutes les 3 itérations (~30ms)
                        {
                            pollCounter = 0;
                            try
                            {
                                _serialPort.Write("P\r\n");
                            }
                            catch { }
                        }
                    }

                    // === LECTURE INSTANTANÉE ===
                    if (_serialPort.BytesToRead > 0)
                    {
                        // Lire tout d'un coup via le Stream sous-jacent (plus rapide que ReadExisting)
                        byte[] readBuffer = new byte[_serialPort.BytesToRead];
                        int bytesRead = _serialPort.BaseStream.Read(readBuffer, 0, readBuffer.Length);

                        if (bytesRead > 0)
                        {
                            string data = Encoding.ASCII.GetString(readBuffer, 0, bytesRead);
                            buffer.Append(data);

                            // Traiter dès qu'on a un \n
                            string bufStr = buffer.ToString();
                            int lastNl = bufStr.LastIndexOf('\n');

                            if (lastNl >= 0)
                            {
                                string remaining = lastNl + 1 < bufStr.Length ? bufStr.Substring(lastNl + 1) : "";
                                string complete = bufStr.Substring(0, lastNl + 1);

                                buffer.Clear();
                                if (remaining.Length > 0) buffer.Append(remaining);

                                // DERNIÈRE ligne valide uniquement
                                int start = complete.LastIndexOf('\n', lastNl > 0 ? lastNl - 1 : 0);
                                string lastLine;
                                if (start >= 0)
                                {
                                    lastLine = complete.Substring(start + 1).Trim('\r', '\n', ' ');
                                    if (string.IsNullOrWhiteSpace(lastLine))
                                    {
                                        // Fallback : parcourir depuis la fin
                                        var lines = complete.Split('\n');
                                        lastLine = "";
                                        for (int i = lines.Length - 1; i >= 0; i--)
                                        {
                                            lastLine = lines[i].Trim('\r', '\n', ' ');
                                            if (!string.IsNullOrWhiteSpace(lastLine)) break;
                                        }
                                    }
                                }
                                else
                                {
                                    lastLine = complete.Trim('\r', '\n', ' ');
                                }

                                if (!string.IsNullOrWhiteSpace(lastLine))
                                {
                                    RawDataReceived?.Invoke(lastLine);
                                    ParseWeight(lastLine);
                                }
                            }

                            // Anti-overflow
                            if (buffer.Length > 64) buffer.Clear();
                        }
                    }
                    else
                    {
                        // Pas de données : spin-wait ultra-court
                        // SpinWait est plus rapide que Thread.Sleep(1) car il ne yield pas au scheduler
                        Thread.SpinWait(500);
                        // Puis yield léger pour pas brûler le CPU
                        Thread.Sleep(0); // Yield uniquement si un autre thread attend
                    }
                }
                catch (TimeoutException) { } // Normal
                catch (InvalidOperationException)
                {
                    if (_running)
                    {
                        _running = false;
                        StatusChanged?.Invoke("Déconnecté");
                    }
                    break;
                }
                catch (Exception ex)
                {
                    if (_running)
                    {
                        StatusChanged?.Invoke($"Erreur: {ex.Message}");
                        Thread.Sleep(50);
                    }
                }
            }
        }

        /// <summary>Ferme le port et arrête le thread.</summary>
        public void Stop()
        {
            _running = false;

            if (_readThread != null && _readThread.IsAlive)
            {
                _readThread.Join(150);
                _readThread = null;
            }

            if (_serialPort != null)
            {
                try
                {
                    if (_serialPort.IsOpen)
                    {
                        _serialPort.DiscardInBuffer();
                        _serialPort.Close();
                    }
                    _serialPort.Dispose();
                }
                catch { }
                finally
                {
                    _serialPort = null;
                    StatusChanged?.Invoke("Déconnecté");
                }
            }
        }

        /// <summary>Envoie une commande à la balance.</summary>
        public void SendCommand(string command)
        {
            if (_serialPort?.IsOpen == true)
            {
                try { _serialPort.Write(command.ToUpper() + "\r\n"); }
                catch (Exception ex) { StatusChanged?.Invoke($"Erreur envoi: {ex.Message}"); }
            }
        }

        public void Tare() => SendCommand("T");
        public void Zero() => SendCommand("Z");
        public void RequestWeight() => SendCommand("P");

        /// <summary>
        /// Envoie le prix unitaire à l'afficheur de la balance.
        /// Format Adam Equipment SWZ : "$XXXX.XX" suivi de CR LF.
        /// La balance calcule automatiquement PRICE TO PAY = UNIT PRICE × poids.
        /// </summary>
        public void SendUnitPrice(decimal unitPrice)
        {
            if (_serialPort?.IsOpen == true)
            {
                try
                {
                    // Format avec point décimal, max 6 chiffres + 2 décimales
                    string priceStr = unitPrice.ToString("F2", CultureInfo.InvariantCulture);
                    _serialPort.Write($"${priceStr}\r\n");
                }
                catch (Exception ex)
                {
                    StatusChanged?.Invoke($"Erreur envoi prix: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Teste la connexion. Si le port est déjà ouvert par ce service, retourne true.
        /// Sinon tente d'ouvrir brièvement le port.
        /// </summary>
        public static bool TestConnection(string portName, int baudRate = 9600, SerialScaleService? activeService = null)
        {
            // Si le service courant utilise déjà ce port, il est connecté → succès
            if (activeService != null && activeService.IsConnected 
                && string.Equals(activeService.ActivePortName, portName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            try
            {
                using var tp = new SerialPort(portName, baudRate, Parity.None, 8, StopBits.One);
                tp.ReadTimeout = 500;
                tp.DtrEnable = true;
                tp.RtsEnable = true;
                tp.Open();
                tp.DiscardInBuffer();
                tp.Write("P\r\n");
                Thread.Sleep(200);
                tp.Close();
                return true;
            }
            catch { return false; }
        }

        public static string[] GetAvailablePorts() => SerialPort.GetPortNames();

        /// <summary>Parse rapide du poids. Format SWZ : "+ 0.200kg"</summary>
        private void ParseWeight(string line)
        {
            var match = WeightRegex.Match(line);
            if (!match.Success) return;

            if (!decimal.TryParse(match.Groups[2].Value, NumberStyles.Any,
                CultureInfo.InvariantCulture, out decimal weight)) return;

            if (match.Groups[1].Value == "-") weight = -weight;
            if (match.Groups[3].Success &&
                match.Groups[3].Value.Equals("g", StringComparison.OrdinalIgnoreCase))
                weight /= 1000m;

            CurrentWeight = weight;
            WeightChanged?.Invoke(weight);
        }

        public void Dispose()
        {
            if (!_disposed) { _disposed = true; Stop(); }
        }
    }
}
