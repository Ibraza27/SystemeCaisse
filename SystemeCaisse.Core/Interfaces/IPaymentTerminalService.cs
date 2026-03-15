using System.Threading.Tasks;

namespace SystemeCaisse.Core.Interfaces
{
    public interface IPaymentTerminalService
    {
        bool IsConnected { get; }
        Task<bool> InitializeAsync(string portName);
        Task<bool> PayAsync(decimal amount);
        Task CancelAsync();
    }
}
