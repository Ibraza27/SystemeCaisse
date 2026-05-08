using System;
using System.Threading.Tasks;

namespace SystemeCaisse.Core.Interfaces
{
    /// <summary>
    /// Résultat structuré d'une transaction TPE.
    /// </summary>
    public class PaymentResult
    {
        public bool Success { get; set; }
        public string Message { get; set; } = "";
        public string? TransactionId { get; set; }
    }

    /// <summary>
    /// Service de communication avec un Terminal de Paiement Électronique (TPE).
    /// L'application envoie uniquement le montant et reçoit un statut (Accepté/Refusé).
    /// Aucune donnée de carte bancaire n'est manipulée.
    /// </summary>
    public interface IPaymentTerminalService
    {
        bool IsConnected { get; }
        string? ActivePortName { get; }
        event Action<string>? StatusChanged;

        Task<bool> InitializeAsync(string portName, int baudRate = 9600);
        Task<PaymentResult> PayAsync(decimal amount);
        Task CancelAsync();
        void Disconnect();
    }
}
