using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
namespace Pointage;
public sealed class DesignFactory : IDesignTimeDbContextFactory<AppDb>
{
    public AppDb CreateDbContext(string[] args) => new(new DbContextOptionsBuilder<AppDb>().UseSqlServer(
        @"Server=.\SQLEXPRESS;Database=API_Sage_Factorial;Integrated Security=True;TrustServerCertificate=True").Options);
}
