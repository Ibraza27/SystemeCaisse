using System;
using System.Globalization;
using System.IO.Ports;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SystemeCaisse.Core.Interfaces;

namespace SystemeCaisse.UI.Services
{
    /// <summary>
    /// Service de communication avec un TPE Verifone via port série (USB → COM virtuel).
    /// 
    /// Implémente le Protocole Caisse (CONCERT) simplifié :
    /// 1. Caisse envoie ENQ (0x05) → TPE répond ACK (0x06)
    /// 2. Caisse envoie la trame de demande de paiement (montant)
    /// 3. TPE traite le paiement et renvoie la réponse (accepté/refusé)
    /// 
    /// IMPORTANT : L'application n'a JAMAIS accès aux données de carte bancaire.
    /// Elle envoie uniquement le montant et reçoit un statut.
    /// 
    /// Configuration : Le TPE doit être en mode "Caisse" (intégré), pas "Autonome".
    /// Le driver USB Verifone doit être installé pour que le port COM virtuel apparaisse.
    /// </summary>
    public class VerifonePaymentTerminalService : IPaymentTerminalService, IDisposable
    {
        // Caractères de contrôle du protocole CONCERT
        private const byte ENQ = 0x05;  // Enquiry — demande de connexion
        private const byte ACK = 0x06;  // Acknowledge — confirmation
        private const byte NAK = 0x15;  // Negative Acknowledge — erreur
        private const byte EOT = 0x04;  // End of Transmission
        private const byte STX = 0x02;  // Start of Text
        private const byte ETX = 0x03;  // End of Text

        private SerialPort? _serialPort;
        private volatile bool _disposed;

        /// <summary>Timeout de réponse du TPE en secondes (configurable).</summary>
        public int TransactionTimeoutSeconds { get; set; } = 60;

        /// <summary>Indique si le port est ouvert et le TPE connecté.</summary>
        public bool IsConnected => _serialPort?.IsOpen == true;

        /// <summary>Nom du port COM actif.</summary>
        public string? ActivePortName => _serialPort?.PortName;

        /// <summary>Déclenché quand le statut change (pour l'UI).</summary>
        public event Action<string>? StatusChanged;

        /// <summary>
        /// Ouvre le port COM et vérifie la présence du TPE via handshake ENQ/ACK.
        /// </summary>
        public async Task<bool> InitializeAsync(string portName, int baudRate = 9600)
        {
            Disconnect();

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
                    ReadBufferSize = 4096,
                    WriteBufferSize = 1024,
                    ReadTimeout = 5000,
                    WriteTimeout = 2000,
                    Encoding = Encoding.ASCII,
                    DtrEnable = true,
                    RtsEnable = true
                };

                _serialPort.Open();
                _serialPort.DiscardInBuffer();
                _serialPort.DiscardOutBuffer();

                StatusChanged?.Invoke("Port ouvert, test de connexion...");

                // Test de connexion : envoi ENQ, attente ACK
                bool tpePresent = await Task.Run(() => SendENQAndWaitACK(2000));

                if (tpePresent)
                {
                    StatusChanged?.Invoke($"TPE connecté sur {portName}");
                    return true;
                }
                else
                {
                    // Le port est ouvert mais pas de réponse du TPE
                    // On reste connecté quand même — le TPE peut répondre plus tard
                    StatusChanged?.Invoke($"Port {portName} ouvert (TPE ne répond pas au handshake — mode passif)");
                    return true;
                }
            }
            catch (UnauthorizedAccessException)
            {
                StatusChanged?.Invoke($"Port {portName} occupé par une autre application");
                return false;
            }
            catch (System.IO.FileNotFoundException)
            {
                StatusChanged?.Invoke($"Port {portName} introuvable — vérifiez la connexion USB");
                return false;
            }
            catch (Exception ex)
            {
                StatusChanged?.Invoke($"Erreur connexion : {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Envoie le montant au TPE et attend la réponse (accepté/refusé).
        /// Le TPE gère intégralement la saisie du code PIN et la communication bancaire.
        /// </summary>
        public async Task<PaymentResult> PayAsync(decimal amount)
        {
            if (!IsConnected || _serialPort == null)
                return new PaymentResult { Success = false, Message = "TPE non connecté" };

            try
            {
                StatusChanged?.Invoke("Envoi du montant au TPE...");

                // Étape 1 : Handshake ENQ → ACK
                bool handshakeOk = await Task.Run(() => SendENQAndWaitACK(3000));
                if (!handshakeOk)
                {
                    StatusChanged?.Invoke("Le TPE ne répond pas");
                    return new PaymentResult { Success = false, Message = "Le TPE ne répond pas. Vérifiez qu'il est allumé et en mode Caisse." };
                }

                // Étape 2 : Construire et envoyer la trame de paiement
                string paymentFrame = BuildPaymentFrame(amount);
                StatusChanged?.Invoke("Transaction en cours sur le TPE...");

                byte[] frameBytes = Encoding.ASCII.GetBytes(paymentFrame);
                _serialPort.Write(frameBytes, 0, frameBytes.Length);

                // Étape 3 : Attendre la réponse du TPE (avec timeout long pour la saisie PIN)
                string response = await Task.Run(() => WaitForResponse(TransactionTimeoutSeconds * 1000));

                // Étape 4 : Envoyer EOT pour terminer la communication
                try { _serialPort.Write(new byte[] { EOT }, 0, 1); } catch { }

                // Étape 5 : Parser la réponse
                return ParseResponse(response);
            }
            catch (TimeoutException)
            {
                StatusChanged?.Invoke("Timeout — le TPE n'a pas répondu");
                return new PaymentResult { Success = false, Message = "Délai d'attente dépassé. Le TPE n'a pas répondu à temps." };
            }
            catch (Exception ex)
            {
                StatusChanged?.Invoke($"Erreur : {ex.Message}");
                return new PaymentResult { Success = false, Message = $"Erreur de communication : {ex.Message}" };
            }
        }

        /// <summary>
        /// Annule la transaction en cours (si possible).
        /// </summary>
        public Task CancelAsync()
        {
            if (_serialPort?.IsOpen == true)
            {
                try
                {
                    _serialPort.Write(new byte[] { EOT }, 0, 1);
                    StatusChanged?.Invoke("Annulation envoyée au TPE");
                }
                catch { }
            }
            return Task.CompletedTask;
        }

        /// <summary>
        /// Ferme la connexion au TPE.
        /// </summary>
        public void Disconnect()
        {
            if (_serialPort != null)
            {
                try
                {
                    if (_serialPort.IsOpen)
                    {
                        _serialPort.DiscardInBuffer();
                        _serialPort.DiscardOutBuffer();
                        _serialPort.Close();
                    }
                    _serialPort.Dispose();
                }
                catch { }
                finally
                {
                    _serialPort = null;
                    StatusChanged?.Invoke("TPE déconnecté");
                }
            }
        }

        /// <summary>
        /// Envoie ENQ et attend ACK du TPE.
        /// </summary>
        private bool SendENQAndWaitACK(int timeoutMs)
        {
            if (_serialPort == null || !_serialPort.IsOpen) return false;

            try
            {
                _serialPort.DiscardInBuffer();
                _serialPort.Write(new byte[] { ENQ }, 0, 1);

                int elapsed = 0;
                while (elapsed < timeoutMs)
                {
                    if (_serialPort.BytesToRead > 0)
                    {
                        int b = _serialPort.ReadByte();
                        if (b == ACK) return true;
                        if (b == NAK) return false;
                    }
                    Thread.Sleep(50);
                    elapsed += 50;
                }

                return false;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Construit la trame de demande de paiement (Protocole Caisse simplifié).
        /// Format : STX + données + ETX + LRC
        /// Données : montant en centimes sur 8 caractères + type opération "debit"
        /// </summary>
        private string BuildPaymentFrame(decimal amount)
        {
            // Montant en centimes, formaté sur 8 caractères avec zéros à gauche
            int centimes = (int)(amount * 100);
            string montantStr = centimes.ToString("D8");

            // Trame simplifiée : STX + Code opération (00=débit) + Montant + ETX
            // Le LRC (contrôle parité) est le XOR de tous les octets entre STX exclu et ETX inclus
            string data = $"00{montantStr}";
            
            var sb = new StringBuilder();
            sb.Append((char)STX);
            sb.Append(data);
            sb.Append((char)ETX);

            // Calcul LRC (XOR de tous les caractères de data + ETX)
            byte lrc = 0;
            foreach (char c in data) lrc ^= (byte)c;
            lrc ^= ETX;
            sb.Append((char)lrc);

            return sb.ToString();
        }

        /// <summary>
        /// Attend la réponse complète du TPE (entre STX et ETX).
        /// </summary>
        private string WaitForResponse(int timeoutMs)
        {
            if (_serialPort == null || !_serialPort.IsOpen)
                throw new InvalidOperationException("Port série non ouvert");

            var response = new StringBuilder();
            bool inFrame = false;
            int elapsed = 0;

            while (elapsed < timeoutMs)
            {
                if (_serialPort.BytesToRead > 0)
                {
                    int b = _serialPort.ReadByte();

                    if (b == STX)
                    {
                        inFrame = true;
                        response.Clear();
                        continue;
                    }

                    if (b == ETX && inFrame)
                    {
                        // Lire le LRC (1 octet supplémentaire)
                        try { _serialPort.ReadByte(); } catch { }
                        return response.ToString();
                    }

                    if (inFrame)
                    {
                        response.Append((char)b);
                    }

                    // Reset le timeout à chaque byte reçu (le TPE peut envoyer des données intermédiaires)
                    elapsed = Math.Max(elapsed - 100, 0);
                }
                else
                {
                    Thread.Sleep(100);
                    elapsed += 100;
                }
            }

            throw new TimeoutException("Le TPE n'a pas répondu dans le délai imparti.");
        }

        /// <summary>
        /// Parse la réponse du TPE.
        /// Le premier octet est le code réponse : '0' = accepté, autre = refusé.
        /// </summary>
        private PaymentResult ParseResponse(string response)
        {
            if (string.IsNullOrEmpty(response))
            {
                StatusChanged?.Invoke("Réponse vide du TPE");
                return new PaymentResult { Success = false, Message = "Réponse vide du TPE" };
            }

            // Code statut : premier caractère
            char statusCode = response[0];
            
            // Transaction ID : caractères restants (si présents)
            string transactionId = response.Length > 2 ? response.Substring(2) : "";

            if (statusCode == '0')
            {
                StatusChanged?.Invoke("✅ Paiement accepté");
                return new PaymentResult
                {
                    Success = true,
                    Message = "Paiement accepté",
                    TransactionId = transactionId
                };
            }
            else
            {
                string reason = statusCode switch
                {
                    '1' => "Paiement refusé par la banque",
                    '2' => "Carte invalide",
                    '3' => "Erreur de communication bancaire",
                    '4' => "Transaction annulée par le client",
                    '5' => "Code PIN incorrect",
                    '7' => "Carte retirée prématurément",
                    _ => $"Paiement refusé (code {statusCode})"
                };

                StatusChanged?.Invoke($"❌ {reason}");
                return new PaymentResult
                {
                    Success = false,
                    Message = reason,
                    TransactionId = transactionId
                };
            }
        }

        /// <summary>
        /// Teste la connexion à un port COM pour détecter un TPE.
        /// </summary>
        public static bool TestConnection(string portName, int baudRate = 9600, VerifonePaymentTerminalService? activeService = null)
        {
            // Si le service actif utilise déjà ce port → succès
            if (activeService != null && activeService.IsConnected
                && string.Equals(activeService.ActivePortName, portName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            try
            {
                using var tp = new SerialPort(portName, baudRate, Parity.None, 8, StopBits.One);
                tp.ReadTimeout = 3000;
                tp.WriteTimeout = 1000;
                tp.DtrEnable = true;
                tp.RtsEnable = true;
                tp.Open();
                tp.DiscardInBuffer();

                // Envoyer ENQ et attendre ACK
                tp.Write(new byte[] { ENQ }, 0, 1);
                int elapsed = 0;
                while (elapsed < 3000)
                {
                    if (tp.BytesToRead > 0)
                    {
                        int b = tp.ReadByte();
                        if (b == ACK) { tp.Close(); return true; }
                    }
                    Thread.Sleep(100);
                    elapsed += 100;
                }

                tp.Close();
                return false;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Scanne tous les ports COM disponibles pour détecter un TPE.
        /// </summary>
        public static string? DetectTerminal(int baudRate = 9600)
        {
            foreach (var port in SerialPort.GetPortNames())
            {
                try
                {
                    if (TestConnection(port, baudRate))
                        return port;
                }
                catch { }
            }
            return null;
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
                Disconnect();
            }
        }
    }
}
