using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace MoneroUserForum.Data;

/// <summary>
/// Lets `dotnet ef migrations` build an <see cref="AppDbContext"/> at design time
/// without spinning up the web host. The connection string is only used when a
/// command actually touches the database; `migrations add` does not connect.
/// </summary>
public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var conn = Environment.GetEnvironmentVariable("MUF_CONNECTION")
                   ?? "Host=localhost;Port=5432;Database=monerouserforum;Username=postgres;Password=postgres";

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(conn)
            .Options;

        return new AppDbContext(options);
    }
}
