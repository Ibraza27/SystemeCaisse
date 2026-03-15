using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace SystemeCaisse.Infrastructure.Data
{
    public class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
    {
        public AppDbContext CreateDbContext(string[] args)
        {
            var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();
            optionsBuilder.UseSqlite(@"Data Source=S:\PROGRAMATION\SystemeCaisse\SystemeCaisse.Infrastructure\caisse.db");

            return new AppDbContext(optionsBuilder.Options);
        }
    }
}
