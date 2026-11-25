using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Cine.Frontend.Models;

namespace Cine.Frontend.Services;

public sealed class ApiTf09Service(HttpClient client) : ITf09Service
{
    public Sesion? SesionActual { get; private set; }

    public async Task IniciarSesionAsync(LoginInput input)
    {
        var sesion = await Leer<Sesion>(HttpMethod.Post, "/api/auth/login", input, requiereSesion: false);
        if (string.IsNullOrWhiteSpace(sesion.AccessToken) || string.IsNullOrWhiteSpace(sesion.Nombre) ||
            sesion.Roles is not { Length: > 0 })
            throw new InvalidOperationException("La respuesta de inicio de sesión no coincide con el contrato.");
        SesionActual = sesion;
    }

    public void CerrarSesion() => SesionActual = null;
    public Task<Resumen> ResumenAsync() => Leer<Resumen>(HttpMethod.Get, "/api/dashboard");
    public Task<Pelicula[]> PeliculasAsync() => Leer<Pelicula[]>(HttpMethod.Get, "/api/peliculas");
    public Task<Pelicula> GuardarPeliculaAsync(int? id, PeliculaInput input) => id is int value
        ? Leer<Pelicula>(HttpMethod.Put, $"/api/peliculas/{value}", input)
        : Leer<Pelicula>(HttpMethod.Post, "/api/peliculas", input);

    public async Task EliminarPeliculaAsync(int id)
    {
        using var response = await Enviar(HttpMethod.Delete, $"/api/peliculas/{id}");
    }

    public Task<Sala[]> SalasAsync() => Leer<Sala[]>(HttpMethod.Get, "/api/salas");
    public Task<Sala> GuardarSalaAsync(int? id, SalaInput input) => id is int value
        ? Leer<Sala>(HttpMethod.Put, $"/api/salas/{value}", input)
        : Leer<Sala>(HttpMethod.Post, "/api/salas", input);

    public async Task EliminarSalaAsync(int id)
    {
        using var response = await Enviar(HttpMethod.Delete, $"/api/salas/{id}");
    }

    public Task<Funcion[]> FuncionesAsync() => Leer<Funcion[]>(HttpMethod.Get, "/api/funciones");
    public Task<Funcion> CrearFuncionAsync(FuncionInput input) =>
        Leer<Funcion>(HttpMethod.Post, "/api/funciones", input);
    public Task<MapaAsientos> MapaAsync(int funcionId) =>
        Leer<MapaAsientos>(HttpMethod.Get, $"/api/funciones/{funcionId}/asientos");

    public Task<Reserva[]> ReservasAsync() => Leer<Reserva[]>(HttpMethod.Get, "/api/reservas");
    public Task<Reserva> ReservarAsync(ReservaInput input) =>
        Leer<Reserva>(HttpMethod.Post, "/api/reservas", input);
    public Task<Reserva> CancelarAsync(int id) => Leer<Reserva>(HttpMethod.Post, $"/api/reservas/{id}/cancelacion");

    private async Task<T> Leer<T>(HttpMethod method, string path, object? body = null, bool requiereSesion = true)
    {
        using var response = await Enviar(method, path, body, requiereSesion);
        return await response.Content.ReadFromJsonAsync<T>()
            ?? throw new InvalidOperationException("La API devolvió una respuesta vacía.");
    }

    private async Task<HttpResponseMessage> Enviar(HttpMethod method, string path,
        object? body = null, bool requiereSesion = true)
    {
        using var request = new HttpRequestMessage(method, path);
        var sesion = requiereSesion
            ? SesionActual ?? throw new InvalidOperationException("Inicie sesión para usar la API.")
            : null;
        if (sesion is not null)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", sesion.AccessToken);
        if (body is not null)
            request.Content = JsonContent.Create(body);

        var response = await client.SendAsync(request);
        if (response.IsSuccessStatusCode)
            return response;

        using (response)
        {
            if (response.StatusCode == HttpStatusCode.Unauthorized && sesion is not null && ReferenceEquals(SesionActual, sesion))
                CerrarSesion();
            var mensaje = response.StatusCode switch
            {
                HttpStatusCode.Unauthorized when requiereSesion => "Sesión expirada. Inicie sesión nuevamente.",
                HttpStatusCode.Unauthorized => "Credenciales incorrectas.",
                HttpStatusCode.Forbidden => "Su rol carece de permiso para esta operación.",
                _ => $"La API devolvió HTTP {(int)response.StatusCode}."
            };
            if (response.StatusCode is not HttpStatusCode.Unauthorized and not HttpStatusCode.Forbidden
                && (int)response.StatusCode < 500)
            {
                try
                {
                    var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
                    if (problem.ValueKind == JsonValueKind.Object &&
                        problem.TryGetProperty("detail", out var detail) && detail.ValueKind == JsonValueKind.String)
                        mensaje = detail.GetString() ?? mensaje;
                }
                catch (JsonException) { }
                catch (NotSupportedException) { }
            }
            throw new InvalidOperationException(mensaje);
        }
    }
}
