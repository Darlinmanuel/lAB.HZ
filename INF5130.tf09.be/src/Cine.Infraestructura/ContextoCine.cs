using Cine.Dominio;
using Microsoft.EntityFrameworkCore;

namespace Cine.Infraestructura;

// Une las clases del cine con las tablas de SQL Server.
public sealed class ContextoCine : DbContext
{
    // Recibe la conexión SQL configurada en Program.cs.
    public ContextoCine(DbContextOptions<ContextoCine> opciones) : base(opciones) { }

    // Cada DbSet representa una tabla de la base de datos.
    public DbSet<Pelicula> Peliculas => Set<Pelicula>();
    public DbSet<Sala> Salas => Set<Sala>();
    public DbSet<Asiento> Asientos => Set<Asiento>();
    public DbSet<Funcion> Funciones => Set<Funcion>();
    public DbSet<Reserva> Reservas => Set<Reserva>();

    protected override void OnModelCreating(ModelBuilder modelo)
    {
        // Aquí se definen las columnas, reglas y relaciones de cada tabla.
        ConfigurarPeliculas(modelo);
        ConfigurarSalas(modelo);
        ConfigurarAsientos(modelo);
        ConfigurarFunciones(modelo);
        ConfigurarReservas(modelo);
    }

    private static void ConfigurarPeliculas(ModelBuilder modelo)
    {
        // Un título no se puede repetir y la duración debe estar dentro del rango permitido.
        var pelicula = modelo.Entity<Pelicula>();
        pelicula.ToTable("Peliculas", tabla =>
        {
            tabla.HasCheckConstraint("CK_Peliculas_Titulo", "LEN(TRIM([Titulo])) > 0");
            tabla.HasCheckConstraint("CK_Peliculas_Duracion", "[DuracionMinutos] BETWEEN 20 AND 300");
        });
        pelicula.Property(x => x.Titulo)
            .HasMaxLength(150)
            .UseCollation("Latin1_General_100_CI_AS_SC");
        pelicula.HasIndex(x => x.Titulo).IsUnique();
    }

    private static void ConfigurarSalas(ModelBuilder modelo)
    {
        // Una sala contiene muchos asientos; al borrar la sala, SQL elimina sus asientos.
        var sala = modelo.Entity<Sala>();
        sala.ToTable("Salas", tabla =>
        {
            tabla.HasCheckConstraint("CK_Salas_Nombre", "LEN(TRIM([Nombre])) > 0");
            tabla.HasCheckConstraint(
                "CK_Salas_Distribucion",
                "[Filas] BETWEEN 1 AND 12 AND [AsientosPorFila] BETWEEN 1 AND 20");
        });
        sala.Property(x => x.Nombre)
            .HasMaxLength(80)
            .UseCollation("Latin1_General_100_CI_AS_SC");
        sala.Property(x => x.Filas).HasConversion<byte>().HasColumnType("tinyint");
        sala.Property(x => x.AsientosPorFila).HasConversion<byte>().HasColumnType("tinyint");
        sala.HasIndex(x => x.Nombre).IsUnique();
        sala.HasMany(x => x.Asientos)
            .WithOne()
            .HasForeignKey(x => x.SalaId)
            .OnDelete(DeleteBehavior.Cascade);
    }

    private static void ConfigurarAsientos(ModelBuilder modelo)
    {
        // La combinación sala, fila y número identifica un asiento único.
        var asiento = modelo.Entity<Asiento>();
        asiento.ToTable("Asientos");
        asiento.Property(x => x.Fila).HasConversion<byte>().HasColumnType("tinyint");
        asiento.Property(x => x.Numero).HasConversion<byte>().HasColumnType("tinyint");
        asiento.HasIndex(x => new { x.SalaId, x.Fila, x.Numero }).IsUnique();
        asiento.HasAlternateKey(x => new { x.Id, x.SalaId });
    }

    private static void ConfigurarFunciones(ModelBuilder modelo)
    {
        // Cada función se relaciona con una película y una sala.
        var funcion = modelo.Entity<Funcion>();
        funcion.ToTable("Funciones", tabla => tabla.HasCheckConstraint(
            "CK_Funciones_Intervalo",
            "DATEDIFF_BIG(second, [InicioUtc], [FinUtc]) BETWEEN 1200 AND 18000"));
        funcion.Property(x => x.InicioUtc).HasColumnType("datetimeoffset(0)");
        funcion.Property(x => x.FinUtc).HasColumnType("datetimeoffset(0)");
        funcion.HasOne(x => x.Pelicula)
            .WithMany()
            .HasForeignKey(x => x.PeliculaId)
            .OnDelete(DeleteBehavior.Restrict);
        funcion.HasOne(x => x.Sala)
            .WithMany()
            .HasForeignKey(x => x.SalaId)
            .OnDelete(DeleteBehavior.Restrict);
        funcion.HasAlternateKey(x => new { x.Id, x.SalaId });
        funcion.HasIndex(x => new { x.SalaId, x.InicioUtc, x.FinUtc });
    }

    private static void ConfigurarReservas(ModelBuilder modelo)
    {
        // La reserva debe apuntar a un asiento y función de la misma sala.
        var reserva = modelo.Entity<Reserva>();
        reserva.ToTable("Reservas", tabla =>
        {
            tabla.HasCheckConstraint("CK_Reservas_Titular", "LEN(TRIM([Titular])) > 0");
            tabla.HasCheckConstraint(
                "CK_Reservas_EstadoFecha",
                "([Estado] = 'Activa' AND [CanceladaUtc] IS NULL) OR " +
                "([Estado] = 'Cancelada' AND [CanceladaUtc] IS NOT NULL AND [CanceladaUtc] >= [CreadaUtc])");
        });
        reserva.Property(x => x.Titular).HasMaxLength(120);
        reserva.Property(x => x.Estado)
            .HasMaxLength(12)
            .IsUnicode(false)
            .UseCollation("Latin1_General_100_BIN2");
        reserva.Property(x => x.CreadaUtc).HasColumnType("datetimeoffset(0)");
        reserva.Property(x => x.CanceladaUtc).HasColumnType("datetimeoffset(0)");
        reserva.HasOne(x => x.Funcion)
            .WithMany()
            .HasForeignKey(x => new { x.FuncionId, x.SalaId })
            .HasPrincipalKey(x => new { x.Id, x.SalaId })
            .OnDelete(DeleteBehavior.Restrict);
        reserva.HasOne(x => x.Asiento)
            .WithMany()
            .HasForeignKey(x => new { x.AsientoId, x.SalaId })
            .HasPrincipalKey(x => new { x.Id, x.SalaId })
            .OnDelete(DeleteBehavior.Restrict);
        reserva.HasIndex(x => new { x.FuncionId, x.AsientoId })
            .IsUnique()
            .HasFilter("[Estado] = 'Activa'")
            .HasDatabaseName("UX_Reservas_AsientoActivo");
        reserva.HasIndex(x => x.AsientoId);
    }
}
