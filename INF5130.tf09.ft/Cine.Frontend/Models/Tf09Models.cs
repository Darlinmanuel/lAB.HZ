using System.Globalization;

namespace Cine.Frontend.Models;

public sealed record Pelicula(int Id, string Titulo, int DuracionMinutos);
public sealed record Sala(int Id, string Nombre, int Filas, int AsientosPorFila);
public sealed record Asiento(int Id, int SalaId, int Fila, int Numero);
public sealed record Funcion(int Id, int PeliculaId, string PeliculaTitulo, int SalaId, string SalaNombre,
    DateTimeOffset InicioUtc, DateTimeOffset FinUtc);
public sealed record EstadoAsiento(int Id, int Fila, int Numero, bool Ocupado);
public sealed record MapaAsientos(int FuncionId, int SalaId, bool ReservaAbierta, EstadoAsiento[] Asientos);
public sealed record Reserva(int Id, int FuncionId, int AsientoId, string CodigoAsiento, string Titular, string Estado,
    DateTimeOffset CreadaUtc, DateTimeOffset? CanceladaUtc);
public sealed record Resumen(int Peliculas, int Salas, int FuncionesProximas, int ReservasActivas, int ReservasCanceladas);

public sealed record PeliculaInput(string Titulo, int DuracionMinutos);
public sealed record SalaInput(string Nombre, int Filas, int AsientosPorFila);
public sealed record FuncionInput(int PeliculaId, int SalaId, DateTimeOffset InicioUtc);
public sealed record ReservaInput(int FuncionId, int AsientoId, string Titular);
public sealed record LoginInput(string Email, string Contrasena);
public sealed record Sesion(string AccessToken, string Nombre, string[] Roles);

public static class CineFormato
{
    private static readonly TimeZoneInfo Zona = TimeZoneInfo.FindSystemTimeZoneById("America/Santo_Domingo");

    public static DateTime HoraLocal(DateTimeOffset instante) => TimeZoneInfo.ConvertTime(instante, Zona).DateTime;

    public static DateTimeOffset AUniversal(DateTime horaLocal) =>
        new(TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(horaLocal, DateTimeKind.Unspecified), Zona));

    public static string Fecha(DateTimeOffset instante) =>
        HoraLocal(instante).ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture);

    public static string Fila(int fila) => ((char)('A' + fila - 1)).ToString();
    public static string Asiento(int fila, int numero) => Fila(fila) + numero.ToString(CultureInfo.InvariantCulture);
}
