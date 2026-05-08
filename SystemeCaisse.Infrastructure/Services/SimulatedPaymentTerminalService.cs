using System;
using System.Threading.Tasks;
using SystemeCaisse.Core.Interfaces;

namespace SystemeCaisse.Infrastructure.Services
{
    /// <summary>
    /// Service simulé de TPE pour les tests et le mode formation.
    /// Retourne toujours un paiement réussi après un délai de 2 secondes.
    /// </summary>
    public class SimulatedPaymentTerminalService : IPaymentTerminalService
    {
        public bool IsConnected { get; private set; } = false;
        public string? ActivePortName { get; private set; }
        public event Action<string>? StatusChanged;

        public Task<bool> InitializeAsync(string portName, int baudRate = 9600)
        {
            IsConnected = true;
            ActivePortName = portName;
            StatusChanged?.Invoke("Connecté (simulé)");
            return Task.FromResult(true);
        }

        public async Task<PaymentResult> PayAsync(decimal amount)
        {
            if (!IsConnected)
                return new PaymentResult { Success = false, Message = "Terminal non connecté" };

            StatusChanged?.Invoke("Transaction en cours...");

            // Simulate transaction time
            await Task.Delay(2000);

            StatusChanged?.Invoke("Transaction acceptée");

            return new PaymentResult
            {
                Success = true,
                Message = "Paiement accepté (simulation)",
                TransactionId = $"SIM-{DateTime.Now.Ticks}"
            };
        }

        public Task CancelAsync()
        {
            StatusChanged?.Invoke("Transaction annulée");
            return Task.CompletedTask;
        }

        public void Disconnect()
        {
            IsConnected = false;
            ActivePortName = null;
            StatusChanged?.Invoke("Déconnecté");
        }
    }
}
