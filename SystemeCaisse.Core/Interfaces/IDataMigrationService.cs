using System.Threading.Tasks;

namespace SystemeCaisse.Core.Interfaces
{
    public interface IDataMigrationService
    {
        Task MigrateDataAsync(string pythonDbPath);
    }
}
