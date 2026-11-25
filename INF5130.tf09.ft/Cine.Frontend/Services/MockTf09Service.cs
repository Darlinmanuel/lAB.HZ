using Cine.Frontend.Models;
using System.Text.Json;
using Microsoft.JSInterop;

namespace Cine.Frontend.Services;

public sealed class MockTf09Service : ITf09Service
{
    public const string CorreoAdministrador = "admin@tf09.example.invalid";
    public const string CorreoGestor = "gestor@tf09.example.invalid";
    public const string ClaveDemostracion = "TF09-Demo";

    private const string Activa = "Activa";
    private const string Cancelada = "Cancelada";
    private const string ClaveAlmacenamiento = "tf09.demo.state.v1";

    private readonly IJSRuntime js;
    private readonly SemaphoreSlim bloqueoInicializacion = new(1, 1);
    private bool inicializado;

    private readonly List<Pelicula> peliculas = [];
    private readonly List<Sala> salas = [];
    private readonly List<Asiento> asientos = [];
    private readonly List<Funcion> funciones = [];
    private readonly List<Reserva> reservas = [];
    private int siguientePelicula = 4, siguienteSala = 3, siguienteAsiento = 1, siguienteFuncion = 1, siguienteReserva = 4;

    public MockTf09Service(IJSRuntime js)
    {
        this.js = js;
    }

    private void InicializarDatosDeEjemplo()
    {
        peliculas.AddRange([
            new(1, "Horizonte del Caribe", 110),
            new(2, "La última función", 95),
            new(3, "Rutas de la ciudad", 125)
        ]);
        salas.AddRange([new(1, "Sala 1", 3, 6), new(2, "Sala 2", 2, 5)]);

        foreach (var sala in salas)
            CrearAsientos(sala);

        var ahora = DateTimeOffset.UtcNow;
        var manana = CineFormato.HoraLocal(ahora).Date.AddDays(1);
        AgregarFuncion(peliculas[0], salas[0], CineFormato.AUniversal(manana.AddHours(18)));
        AgregarFuncion(peliculas[1], salas[0], CineFormato.AUniversal(manana.AddHours(20).AddMinutes(30)));
        AgregarFuncion(peliculas[1], salas[1], CineFormato.AUniversal(manana.AddHours(18).AddMinutes(30)));
        reservas.AddRange([
            new(1, 1, 2, "A2", "María Reyes", Activa, ahora.AddMinutes(-30), null),
            new(2, 1, 9, "B3", "Luis Peña", Activa, ahora.AddMinutes(-25), null),
            new(3, 1, 4, "A4", "Sofía Díaz", Cancelada, ahora.AddMinutes(-20), ahora.AddMinutes(-10))
        ]);
    }

    private async Task AsegurarInicializacionAsync()
    {
        if (inicializado)
            return;

        await bloqueoInicializacion.WaitAsync();
        try
        {
            if (inicializado)
                return;

            var json = await js.InvokeAsync<string?>("localStorage.getItem", ClaveAlmacenamiento);
            if (!string.IsNullOrWhiteSpace(json))
            {
                try
                {
                    var estado = JsonSerializer.Deserialize<EstadoPersistido>(json);
                    if (estado is not null && estado.Peliculas is not null && estado.Salas is not null &&
                        estado.Asientos is not null && estado.Funciones is not null && estado.Reservas is not null)
                    {
                        peliculas.AddRange(estado.Peliculas);
                        salas.AddRange(estado.Salas);
                        asientos.AddRange(estado.Asientos);
                        funciones.AddRange(estado.Funciones);
                        reservas.AddRange(estado.Reservas);
                        siguientePelicula = estado.SiguientePelicula;
                        siguienteSala = estado.SiguienteSala;
                        siguienteAsiento = estado.SiguienteAsiento;
                        siguienteFuncion = estado.SiguienteFuncion;
                        siguienteReserva = estado.SiguienteReserva;
                        inicializado = true;
                        return;
                    }
                }
                catch (JsonException)
                {
                    // Un estado local dañado se reemplaza por los datos de demostración.
                }

                await js.InvokeVoidAsync("localStorage.removeItem", ClaveAlmacenamiento);
            }

            InicializarDatosDeEjemplo();
            inicializado = true;
            await GuardarEstadoAsync();
        }
        finally
        {
            bloqueoInicializacion.Release();
        }
    }

    private Task GuardarEstadoAsync()
    {
        var estado = new EstadoPersistido(peliculas.ToArray(), salas.ToArray(), asientos.ToArray(),
            funciones.ToArray(), reservas.ToArray(), siguientePelicula, siguienteSala, siguienteAsiento,
            siguienteFuncion, siguienteReserva);
        return js.InvokeVoidAsync("localStorage.setItem", ClaveAlmacenamiento, JsonSerializer.Serialize(estado)).AsTask();
    }

    private sealed record EstadoPersistido(Pelicula[] Peliculas, Sala[] Salas, Asiento[] Asientos,
        Funcion[] Funciones, Reserva[] Reservas, int SiguientePelicula, int SiguienteSala,
        int SiguienteAsiento, int SiguienteFuncion, int SiguienteReserva);

    public Sesion? SesionActual { get; private set; }

    public async Task IniciarSesionAsync(LoginInput input)
    {
        await AsegurarInicializacionAsync();
        var rol = input.Email.Trim().ToLowerInvariant() switch
        {
            CorreoAdministrador => "Administrador",
            CorreoGestor => "Gestor",
            _ => null
        };
        if (rol is null || input.Contrasena != ClaveDemostracion)
            throw new InvalidOperationException("Correo o contraseña incorrectos.");

        SesionActual = new Sesion(string.Empty, rol + " de prueba", [rol]);
    }

    public void CerrarSesion() => SesionActual = null;

    public async Task<Resumen> ResumenAsync()
    {
        await AsegurarInicializacionAsync();
        var ahora = DateTimeOffset.UtcNow;
        return new Resumen(peliculas.Count, salas.Count, funciones.Count(f => f.InicioUtc > ahora),
            reservas.Count(r => r.Estado == Activa), reservas.Count(r => r.Estado == Cancelada));
    }

    public async Task<Pelicula[]> PeliculasAsync()
    {
        await AsegurarInicializacionAsync();
        return peliculas.ToArray();
    }

    public async Task<Sala[]> SalasAsync()
    {
        await AsegurarInicializacionAsync();
        return salas.ToArray();
    }

    public async Task<Funcion[]> FuncionesAsync()
    {
        await AsegurarInicializacionAsync();
        return funciones.OrderBy(f => f.InicioUtc).ThenBy(f => f.Id).Select(ParaMostrar).ToArray();
    }

    public async Task<Reserva[]> ReservasAsync()
    {
        await AsegurarInicializacionAsync();
        return reservas.OrderByDescending(r => r.Id).ToArray();
    }

    public async Task<Pelicula> GuardarPeliculaAsync(int? id, PeliculaInput input)
    {
        await AsegurarInicializacionAsync();
        var anterior = id is int value ? BuscarPelicula(value) : null;
        var titulo = Texto(input.Titulo, 150, "El título");
        if (input.DuracionMinutos is < 20 or > 300)
            throw new InvalidOperationException("La duración debe ser de 20 a 300 minutos.");
        if (peliculas.Any(p => p.Id != id && p.Titulo.Equals(titulo, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("Ya existe una película con ese título.");

        var pelicula = new Pelicula(anterior?.Id ?? siguientePelicula++, titulo, input.DuracionMinutos);
        Guardar(peliculas, anterior, pelicula);
        await GuardarEstadoAsync();
        return pelicula;
    }

    public async Task EliminarPeliculaAsync(int id)
    {
        await AsegurarInicializacionAsync();
        var pelicula = BuscarPelicula(id);
        if (funciones.Any(f => f.PeliculaId == id))
            throw new InvalidOperationException("La película tiene funciones registradas.");
        peliculas.Remove(pelicula);
        await GuardarEstadoAsync();
    }

    public async Task<Sala> GuardarSalaAsync(int? id, SalaInput input)
    {
        await AsegurarInicializacionAsync();
        var anterior = id is int value ? BuscarSala(value) : null;
        var nombre = Texto(input.Nombre, 80, "El nombre");
        if (input.Filas is < 1 or > 12 || input.AsientosPorFila is < 1 or > 20)
            throw new InvalidOperationException("Use de 1 a 12 filas y de 1 a 20 asientos por fila.");
        if (anterior is not null && (anterior.Filas != input.Filas || anterior.AsientosPorFila != input.AsientosPorFila))
            throw new InvalidOperationException("La distribución de asientos se fija al crear la sala.");
        if (salas.Any(s => s.Id != id && s.Nombre.Equals(nombre, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("Ya existe una sala con ese nombre.");

        var sala = new Sala(anterior?.Id ?? siguienteSala++, nombre, input.Filas, input.AsientosPorFila);
        Guardar(salas, anterior, sala);
        if (anterior is null)
            CrearAsientos(sala);
        await GuardarEstadoAsync();
        return sala;
    }

    public async Task EliminarSalaAsync(int id)
    {
        await AsegurarInicializacionAsync();
        var sala = BuscarSala(id);
        if (funciones.Any(f => f.SalaId == id))
            throw new InvalidOperationException("La sala tiene funciones registradas.");
        asientos.RemoveAll(a => a.SalaId == id);
        salas.Remove(sala);
        await GuardarEstadoAsync();
    }

    public async Task<Funcion> CrearFuncionAsync(FuncionInput input)
    {
        await AsegurarInicializacionAsync();
        var pelicula = BuscarPelicula(input.PeliculaId);
        var sala = BuscarSala(input.SalaId);
        var ahora = DateTimeOffset.UtcNow;
        var inicio = input.InicioUtc.ToUniversalTime();
        if (inicio <= ahora || inicio > ahora.AddDays(90))
            throw new InvalidOperationException("Programe la función en los próximos 90 días.");

        var fin = inicio.AddMinutes(pelicula.DuracionMinutos);
        if (funciones.Any(f => f.SalaId == sala.Id && inicio < f.FinUtc && f.InicioUtc < fin))
            throw new InvalidOperationException("La sala tiene una función que se solapa con ese horario.");

        var funcion = AgregarFuncion(pelicula, sala, inicio);
        await GuardarEstadoAsync();
        return funcion;
    }

    public async Task<MapaAsientos> MapaAsync(int funcionId)
    {
        await AsegurarInicializacionAsync();
        var funcion = BuscarFuncion(funcionId);
        var ocupados = reservas.Where(r => r.FuncionId == funcionId && r.Estado == Activa).Select(r => r.AsientoId).ToHashSet();
        var mapa = asientos.Where(a => a.SalaId == funcion.SalaId).OrderBy(a => a.Fila).ThenBy(a => a.Numero)
            .Select(a => new EstadoAsiento(a.Id, a.Fila, a.Numero, ocupados.Contains(a.Id))).ToArray();
        return new MapaAsientos(funcion.Id, funcion.SalaId, funcion.InicioUtc > DateTimeOffset.UtcNow, mapa);
    }

    public async Task<Reserva> ReservarAsync(ReservaInput input)
    {
        await AsegurarInicializacionAsync();
        var funcion = BuscarFuncion(input.FuncionId);
        var asiento = asientos.Find(a => a.Id == input.AsientoId)
            ?? throw new InvalidOperationException("No se encontró el asiento.");
        if (asiento.SalaId != funcion.SalaId)
            throw new InvalidOperationException("El asiento no pertenece a la sala de esta función.");
        var ahora = DateTimeOffset.UtcNow;
        if (funcion.InicioUtc <= ahora)
            throw new InvalidOperationException("La función ya comenzó.");
        if (reservas.Any(r => r.FuncionId == funcion.Id && r.AsientoId == asiento.Id && r.Estado == Activa))
            throw new InvalidOperationException("El asiento ya está ocupado en esta función.");

        var titular = Texto(input.Titular, 120, "El titular");
        var reserva = new Reserva(siguienteReserva++, funcion.Id, asiento.Id, CineFormato.Asiento(asiento.Fila, asiento.Numero),
            titular, Activa, ahora, null);
        reservas.Add(reserva);
        await GuardarEstadoAsync();
        return reserva;
    }

    public async Task<Reserva> CancelarAsync(int id)
    {
        await AsegurarInicializacionAsync();
        var reserva = reservas.Find(r => r.Id == id) ?? throw new InvalidOperationException("No se encontró la reserva.");
        var ahora = DateTimeOffset.UtcNow;
        if (reserva.Estado != Activa)
            throw new InvalidOperationException("La reserva ya fue cancelada.");
        if (BuscarFuncion(reserva.FuncionId).InicioUtc <= ahora)
            throw new InvalidOperationException("La función ya comenzó.");

        var cancelada = reserva with { Estado = Cancelada, CanceladaUtc = ahora >= reserva.CreadaUtc ? ahora : reserva.CreadaUtc };
        Guardar(reservas, reserva, cancelada);
        await GuardarEstadoAsync();
        return cancelada;
    }

    private Funcion AgregarFuncion(Pelicula pelicula, Sala sala, DateTimeOffset inicio)
    {
        var funcion = new Funcion(siguienteFuncion++, pelicula.Id, pelicula.Titulo, sala.Id, sala.Nombre,
            inicio, inicio.AddMinutes(pelicula.DuracionMinutos));
        funciones.Add(funcion);
        return funcion;
    }

    private void CrearAsientos(Sala sala)
    {
        for (var fila = 1; fila <= sala.Filas; fila++)
        {
            for (var numero = 1; numero <= sala.AsientosPorFila; numero++)
                asientos.Add(new Asiento(siguienteAsiento++, sala.Id, fila, numero));
        }
    }

    private Funcion ParaMostrar(Funcion funcion) => funcion with
    {
        PeliculaTitulo = BuscarPelicula(funcion.PeliculaId).Titulo,
        SalaNombre = BuscarSala(funcion.SalaId).Nombre
    };

    private Pelicula BuscarPelicula(int id) => peliculas.Find(p => p.Id == id)
        ?? throw new InvalidOperationException("No se encontró la película.");
    private Sala BuscarSala(int id) => salas.Find(s => s.Id == id)
        ?? throw new InvalidOperationException("No se encontró la sala.");
    private Funcion BuscarFuncion(int id) => funciones.Find(f => f.Id == id)
        ?? throw new InvalidOperationException("No se encontró la función.");

    private static void Guardar<T>(List<T> lista, T? anterior, T actual) where T : class
    {
        if (anterior is null)
            lista.Add(actual);
        else
            lista[lista.IndexOf(anterior)] = actual;
    }

    private static string Texto(string entrada, int maximo, string campo)
    {
        var valor = entrada.Trim();
        if (valor.Length == 0 || valor.Length > maximo)
            throw new InvalidOperationException($"{campo} requiere entre 1 y {maximo} caracteres.");
        return valor;
    }
}
