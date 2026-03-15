using System.Threading.Tasks;
using SystemeCaisse.Core.Interfaces;

namespace SystemeCaisse.Infrastructure.Services
{
    public class SimulatedPaymentTerminalService : IPaymentTerminalService
    {
        public bool IsConnected { get; private set; } = false;

        public Task<bool> InitializeAsync(string portName)
        {
            IsConnected = true;
            return Task.FromResult(true);
        }

        public async Task<bool> PayAsync(decimal amount)
        {
            if (!IsConnected) throw new System.InvalidOperationException("Terminal not connected");

            // Simulate transaction time
            await Task.Delay(2000);

            // Always succeed for simulation
            return true;
        }

        public Task CancelAsync()
        {
            return Task.CompletedTask;
        }
    }
}
