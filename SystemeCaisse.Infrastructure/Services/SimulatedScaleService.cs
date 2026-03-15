using System;
using System.Threading.Tasks;
using SystemeCaisse.Core.Interfaces;

namespace SystemeCaisse.Infrastructure.Services
{
    public class SimulatedScaleService : IScaleService
    {
        public bool IsConnected { get; private set; } = false;

        public Task ConnectAsync(string portName)
        {
            IsConnected = true;
            return Task.CompletedTask;
        }

        public Task DisconnectAsync()
        {
            IsConnected = false;
            return Task.CompletedTask;
        }

        public async Task<decimal> GetWeightAsync()
        {
            if (!IsConnected) throw new InvalidOperationException("Scale not connected");
            
            // Simulate reading delay
            await Task.Delay(500);
            
            // Return random weight between 0.1 and 5.0 kg
            var random = new Random();
            return (decimal)(random.NextDouble() * 4.9 + 0.1);
        }

        public async Task<bool> ZeroAsync()
        {
            if (!IsConnected) return false;
            await Task.Delay(200);
            return true;
        }
    }
}
