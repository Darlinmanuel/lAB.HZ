using Cine.Dominio;
using System.Text.Json.Serialization;

namespace Cine.Aplicacion;

// Define los datos que la API recibe y devuelve. No son las tablas de SQL.
// Entrada representa lo que envía el frontend; Respuesta, lo que recibe.
public sealed record EntradaPelicula(string Titulo, int DuracionMinutos);
public sealed record RespuestaPelicula(int Id, string Titulo, int DuracionMinutos);

// Salas
public sealed record EntradaSala(string Nombre, int Filas, int AsientosPorFila);
public sealed record RespuestaSala(int Id, string Nombre, int Filas, int AsientosPorFila);

// Funciones y mapa de asientos
public sealed record EntradaFuncion(int PeliculaId, int SalaId, DateTimeOffset InicioUtc);
public sealed record RespuestaFuncion(
    int Id,
    int PeliculaId,
    string PeliculaTitulo,
    int SalaId,
    string SalaNombre,
    DateTimeOffset InicioUtc,
    DateTimeOffset FinUtc);

public sealed record EstadoAsientoRespuesta(int Id, int Fila, int Numero, bool Ocupado);
public sealed record MapaAsientosRespuesta(int FuncionId, int SalaId, bool ReservaAbierta, EstadoAsientoRespuesta[] Asientos);

// Reservas y acceso
public sealed record EntradaReserva(int FuncionId, int AsientoId, string Titular);
public sealed record RespuestaReserva(
    int Id,
    int FuncionId,
    int AsientoId,
    string CodigoAsiento,
    string Titular,
    string Estado,
    DateTimeOffset CreadaUtc,
    DateTimeOffset? CanceladaUtc);

public sealed record ResumenPanel(
    int Peliculas,
    int Salas,
    int FuncionesProximas,
    int ReservasActivas,
    int ReservasCanceladas);

public sealed record EntradaInicioSesion(
    [property: JsonPropertyName("email")] string Correo,
    string Contrasena);
public sealed record RespuestaSesion(
    [property: JsonPropertyName("accessToken")] string TokenAcceso,
    string Nombre,
    string[] Roles);

// Lista las operaciones que el servicio del cine ofrece a la API.
public interface IServicioCine
{
    // Resumen y operaciones de películas.
    Task<ResumenPanel> ObtenerResumen(CancellationToken tokenCancelacion);

    Task<RespuestaPelicula[]> Peliculas(CancellationToken tokenCancelacion);
    Task<RespuestaPelicula> GuardarPelicula(int? id, EntradaPelicula entrada, CancellationToken tokenCancelacion);
    Task EliminarPelicula(int id, CancellationToken tokenCancelacion);

    // Operaciones de salas y funciones.
    Task<RespuestaSala[]> Salas(CancellationToken tokenCancelacion);
    Task<RespuestaSala> GuardarSala(int? id, EntradaSala entrada, CancellationToken tokenCancelacion);
    Task EliminarSala(int id, CancellationToken tokenCancelacion);

    Task<RespuestaFuncion[]> Funciones(CancellationToken tokenCancelacion);
    Task<RespuestaFuncion> CrearFuncion(EntradaFuncion entrada, CancellationToken tokenCancelacion);
    Task<MapaAsientosRespuesta> Mapa(int id, CancellationToken tokenCancelacion);

    // Operaciones de reservas.
    Task<RespuestaReserva[]> Reservas(CancellationToken tokenCancelacion);
    Task<RespuestaReserva> CrearReserva(EntradaReserva entrada, CancellationToken tokenCancelacion);
    Task<RespuestaReserva> Cancelar(int id, CancellationToken tokenCancelacion);
}

public static class ReglasCine
{
    // El asiento y la función deben pertenecer a la misma sala.
    public static void ValidarSalaDelAsiento(int salaFuncionId, int salaAsientoId)
    {
        if (salaFuncionId != salaAsientoId)
        {
            throw new ExcepcionValidacionDominio("El asiento no pertenece a la sala de esta función.");
        }
    }

    // Solo se aceptan reservas antes de que comience la función.
    public static void ValidarReservaAbierta(DateTimeOffset inicioFuncionUtc, DateTimeOffset ahoraUtc)
    {
        if (inicioFuncionUtc <= ahoraUtc)
        {
            throw new ExcepcionConflictoDominio("La función ya comenzó.", "FUNCION_INICIADA");
        }
    }

    // Comprueba que el horario nuevo no se cruce con otra función de esa sala.
    public static void ValidarSinCruces(
        DateTimeOffset inicioNuevoUtc,
        DateTimeOffset finNuevoUtc,
        IEnumerable<(DateTimeOffset InicioUtc, DateTimeOffset FinUtc)> funcionesExistentes)
    {
        foreach (var funcion in funcionesExistentes)
        {
            bool seSolapa = funcion.InicioUtc < finNuevoUtc && inicioNuevoUtc < funcion.FinUtc;

            if (seSolapa)
            {
                throw new ExcepcionConflictoDominio(
                    "La función se solapa con otra función de la misma sala.",
                    "FUNCION_SOLAPADA");
            }
        }
    }
}
