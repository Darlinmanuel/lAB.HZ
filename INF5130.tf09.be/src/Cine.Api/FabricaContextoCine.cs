using Cine.Infraestructura;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Cine.Api;

public sealed class FabricaContextoCine : IDesignTimeDbContextFactory<ContextoCine>
{
    public ContextoCine CreateDbContext(string[] args)
    {
        var conexion = Environment.GetEnvironmentVariable("ConnectionStrings__Cine") ??
            "Server=localhost,1433;Database=INF5130_TF09;Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=true";
        var opciones = new DbContextOptionsBuilder<ContextoCine>().UseSqlServer(conexion).Options;
        return new ContextoCine(opciones);
    }
}
