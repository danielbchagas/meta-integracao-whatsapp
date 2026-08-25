using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Meta.WhatsApp.Infrastructure.Persistence;

public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<WhatsAppDbContext>
{
    public WhatsAppDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("WHATSAPP_SQL_CONNECTION")
            ?? "Server=(localdb)\\mssqllocaldb;Database=WhatsAppDesign;Trusted_Connection=True;TrustServerCertificate=True";
        var options = new DbContextOptionsBuilder<WhatsAppDbContext>()
            .UseSqlServer(connectionString)
            .Options;
        return new WhatsAppDbContext(options);
    }
}
