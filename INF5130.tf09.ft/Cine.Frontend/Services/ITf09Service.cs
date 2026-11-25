using Cine.Frontend.Models;

namespace Cine.Frontend.Services;

public sealed record FrontendOptions(bool UseMockData);

public interface ITf09Service
{
    Sesion? SesionActual { get; }
    Task IniciarSesionAsync(LoginInput input);
    void CerrarSesion();
    Task<Resumen> ResumenAsync();
    Task<Pelicula[]> PeliculasAsync();
    Task<Pelicula> GuardarPeliculaAsync(int? id, PeliculaInput input);
    Task EliminarPeliculaAsync(int id);
    Task<Sala[]> SalasAsync();
    Task<Sala> GuardarSalaAsync(int? id, SalaInput input);
    Task EliminarSalaAsync(int id);
    Task<Funcion[]> FuncionesAsync();
    Task<Funcion> CrearFuncionAsync(FuncionInput input);
    Task<MapaAsientos> MapaAsync(int funcionId);
    Task<Reserva[]> ReservasAsync();
    Task<Reserva> ReservarAsync(ReservaInput input);
    Task<Reserva> CancelarAsync(int id);
}
