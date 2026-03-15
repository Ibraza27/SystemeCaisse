using System.Threading.Tasks;

namespace SystemeCaisse.Core.Interfaces
{
    public interface IScaleService
    {
        bool IsConnected { get; }
        Task<decimal> GetWeightAsync();
        Task<bool> ZeroAsync();
        Task ConnectAsync(string portName);
        Task DisconnectAsync();
    }
}
