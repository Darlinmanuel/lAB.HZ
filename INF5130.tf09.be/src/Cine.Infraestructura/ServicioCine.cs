using Cine.Aplicacion;
using Cine.Dominio;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

using EntidadReserva = Cine.Dominio.Reserva;

namespace Cine.Infraestructura;

public sealed class ServicioCine : IServicioCine
{
    private readonly ContextoCine db;

    public ServicioCine(ContextoCine db)
    {
        this.db = db;
    }

    private static DateTimeOffset AhoraUtc => DateTimeOffset.UtcNow;

    // Cuenta los datos principales para la pantalla de inicio.
    public async Task<ResumenPanel> ObtenerResumen(CancellationToken tokenCancelacion)
    {
        int cantidadPeliculas = await db.Peliculas.CountAsync(tokenCancelacion);
        int cantidadSalas = await db.Salas.CountAsync(tokenCancelacion);
        int funcionesProximas = await db.Funciones.CountAsync(
            funcion => funcion.InicioUtc > AhoraUtc,
            tokenCancelacion);
        int reservasActivas = await db.Reservas.CountAsync(
            reserva => reserva.Estado == EntidadReserva.Activa,
            tokenCancelacion);
        int reservasCanceladas = await db.Reservas.CountAsync(
            reserva => reserva.Estado == EntidadReserva.Cancelada,
            tokenCancelacion);

        return new ResumenPanel(
            cantidadPeliculas,
            cantidadSalas,
            funcionesProximas,
            reservasActivas,
            reservasCanceladas);
    }

    // PELÍCULAS: listar, guardar o eliminar elementos del catálogo.
    public async Task<RespuestaPelicula[]> Peliculas(CancellationToken tokenCancelacion)
    {
        return await db.Peliculas
            .AsNoTracking()
            .OrderBy(pelicula => pelicula.Titulo)
            .Select(pelicula => new RespuestaPelicula(
                pelicula.Id,
                pelicula.Titulo,
                pelicula.DuracionMinutos))
            .ToArrayAsync(tokenCancelacion);
    }

    public async Task<RespuestaPelicula> GuardarPelicula(
        int? id,
        EntradaPelicula entrada,
        CancellationToken tokenCancelacion)
    {
        Pelicula pelicula;

        if (id.HasValue)
        {
            pelicula = await db.Peliculas.FindAsync([id.Value], tokenCancelacion)
                ?? throw NoEncontrado("Película");

            pelicula.Cambiar(entrada.Titulo, entrada.DuracionMinutos);
        }
        else
        {
            pelicula = new Pelicula(entrada.Titulo, entrada.DuracionMinutos);
            db.Peliculas.Add(pelicula);
        }

        try
        {
            await db.SaveChangesAsync(tokenCancelacion);
        }
        catch (DbUpdateException excepcion) when (EsDuplicado(excepcion))
        {
            throw new ExcepcionConflictoDominio("Ya existe una película con ese título.", "TITULO_DUPLICADO");
        }

        return ConvertirPelicula(pelicula);
    }

    public async Task EliminarPelicula(int id, CancellationToken tokenCancelacion)
    {
        Pelicula? pelicula = await db.Peliculas.FindAsync([id], tokenCancelacion);

        if (pelicula is null)
        {
            throw NoEncontrado("Película");
        }

        db.Peliculas.Remove(pelicula);
        await GuardarOIndicarConflicto(
            tokenCancelacion,
            "La película tiene funciones y no se puede eliminar.");
    }

    // SALAS: el plano de una sala no se cambia una vez creado.
    public async Task<RespuestaSala[]> Salas(CancellationToken tokenCancelacion)
    {
        return await db.Salas
            .AsNoTracking()
            .OrderBy(sala => sala.Nombre)
            .Select(sala => new RespuestaSala(
                sala.Id,
                sala.Nombre,
                sala.Filas,
                sala.AsientosPorFila))
            .ToArrayAsync(tokenCancelacion);
    }

    public async Task<RespuestaSala> GuardarSala(
        int? id,
        EntradaSala entrada,
        CancellationToken tokenCancelacion)
    {
        Sala sala;

        if (id.HasValue)
        {
            sala = await db.Salas.FindAsync([id.Value], tokenCancelacion)
                ?? throw NoEncontrado("Sala");

            if (entrada.Filas != sala.Filas || entrada.AsientosPorFila != sala.AsientosPorFila)
            {
                throw new ExcepcionConflictoDominio(
                    "No se puede cambiar la distribución de una sala existente.",
                    "PLANO_INMUTABLE");
            }

            sala.CambiarNombre(entrada.Nombre);
        }
        else
        {
            sala = new Sala(entrada.Nombre, entrada.Filas, entrada.AsientosPorFila);
            db.Salas.Add(sala);
        }

        try
        {
            await db.SaveChangesAsync(tokenCancelacion);
        }
        catch (DbUpdateException excepcion) when (EsDuplicado(excepcion))
        {
            throw new ExcepcionConflictoDominio("Ya existe una sala con ese nombre.", "SALA_DUPLICADA");
        }

        return ConvertirSala(sala);
    }

    public async Task EliminarSala(int id, CancellationToken tokenCancelacion)
    {
        Sala? sala = await db.Salas.FindAsync([id], tokenCancelacion);

        if (sala is null)
        {
            throw NoEncontrado("Sala");
        }

        db.Salas.Remove(sala);
        await GuardarOIndicarConflicto(
            tokenCancelacion,
            "La sala tiene funciones y no se puede eliminar.");
    }

    // FUNCIONES: consulta y programación de proyecciones.
    public async Task<RespuestaFuncion[]> Funciones(CancellationToken tokenCancelacion)
    {
        return await db.Funciones
            .AsNoTracking()
            .Include(funcion => funcion.Pelicula)
            .Include(funcion => funcion.Sala)
            .OrderBy(funcion => funcion.InicioUtc)
            .ThenBy(funcion => funcion.Id)
            .Select(funcion => new RespuestaFuncion(
                funcion.Id,
                funcion.PeliculaId,
                funcion.Pelicula.Titulo,
                funcion.SalaId,
                funcion.Sala.Nombre,
                funcion.InicioUtc,
                funcion.FinUtc))
            .ToArrayAsync(tokenCancelacion);
    }

    public async Task<RespuestaFuncion> CrearFuncion(
        EntradaFuncion entrada,
        CancellationToken tokenCancelacion)
    {
        // La transacción evita que dos solicitudes programen funciones que se cruzan.
        await using var transaccion = await db.Database.BeginTransactionAsync(
            System.Data.IsolationLevel.Serializable,
            tokenCancelacion);

        Pelicula? pelicula = await db.Peliculas.FindAsync([entrada.PeliculaId], tokenCancelacion);
        Sala? sala = await db.Salas.FindAsync([entrada.SalaId], tokenCancelacion);

        if (pelicula is null)
        {
            throw NoEncontrado("Película");
        }

        if (sala is null)
        {
            throw NoEncontrado("Sala");
        }

        var funcion = new Funcion(
            pelicula.Id,
            sala.Id,
            entrada.InicioUtc,
            pelicula.DuracionMinutos);

        var horariosOcupados = await db.Funciones
            .Where(elemento => elemento.SalaId == sala.Id)
            .Select(elemento => new { elemento.InicioUtc, elemento.FinUtc })
            .ToListAsync(tokenCancelacion);

        var intervalos = horariosOcupados.Select(elemento => (elemento.InicioUtc, elemento.FinUtc));
        // Aplica la regla antes de guardar la nueva función.
        ReglasCine.ValidarSinCruces(funcion.InicioUtc, funcion.FinUtc, intervalos);

        db.Funciones.Add(funcion);
        await db.SaveChangesAsync(tokenCancelacion);
        await transaccion.CommitAsync(tokenCancelacion);

        return new RespuestaFuncion(
            funcion.Id,
            pelicula.Id,
            pelicula.Titulo,
            sala.Id,
            sala.Nombre,
            funcion.InicioUtc,
            funcion.FinUtc);
    }

    // Marca como ocupados los asientos con una reserva activa.
    public async Task<MapaAsientosRespuesta> Mapa(int funcionId, CancellationToken tokenCancelacion)
    {
        Funcion? funcion = await db.Funciones
            .AsNoTracking()
            .FirstOrDefaultAsync(elemento => elemento.Id == funcionId, tokenCancelacion);

        if (funcion is null)
        {
            throw NoEncontrado("Función");
        }

        List<int> asientosOcupados = await db.Reservas
            .AsNoTracking()
            .Where(reserva =>
                reserva.FuncionId == funcionId && reserva.Estado == EntidadReserva.Activa)
            .Select(reserva => reserva.AsientoId)
            .ToListAsync(tokenCancelacion);

        EstadoAsientoRespuesta[] asientos = await db.Asientos
            .AsNoTracking()
            .Where(asiento => asiento.SalaId == funcion.SalaId)
            .OrderBy(asiento => asiento.Fila)
            .ThenBy(asiento => asiento.Numero)
            .Select(asiento => new EstadoAsientoRespuesta(
                asiento.Id,
                asiento.Fila,
                asiento.Numero,
                asientosOcupados.Contains(asiento.Id)))
            .ToArrayAsync(tokenCancelacion);

        return new MapaAsientosRespuesta(
            funcion.Id,
            funcion.SalaId,
            funcion.InicioUtc > AhoraUtc,
            asientos);
    }

    // RESERVAS: listar, crear y cancelar reservas.
    public async Task<RespuestaReserva[]> Reservas(CancellationToken tokenCancelacion)
    {
        List<EntidadReserva> reservas = await db.Reservas
            .AsNoTracking()
            .Include(reserva => reserva.Asiento)
            .OrderByDescending(reserva => reserva.Id)
            .ToListAsync(tokenCancelacion);

        return reservas.Select(ConvertirReserva).ToArray();
    }

    public async Task<RespuestaReserva> CrearReserva(
        EntradaReserva entrada,
        CancellationToken tokenCancelacion)
    {
        // Busca la función y el asiento antes de validar que sean compatibles.
        Funcion? funcion = await db.Funciones.FindAsync([entrada.FuncionId], tokenCancelacion);
        Asiento? asiento = await db.Asientos
            .FirstOrDefaultAsync(elemento => elemento.Id == entrada.AsientoId, tokenCancelacion);

        if (funcion is null)
        {
            throw NoEncontrado("Función");
        }

        if (asiento is null)
        {
            throw NoEncontrado("Asiento");
        }

        ReglasCine.ValidarSalaDelAsiento(funcion.SalaId, asiento.SalaId);
        ReglasCine.ValidarReservaAbierta(funcion.InicioUtc, AhoraUtc);

        var reserva = new EntidadReserva(
            funcion.Id,
            asiento.Id,
            funcion.SalaId,
            entrada.Titular,
            AhoraUtc);

        // SQL tiene un índice único que impide ocupar dos veces el mismo asiento.
        db.Reservas.Add(reserva);

        try
        {
            await db.SaveChangesAsync(tokenCancelacion);
        }
        catch (DbUpdateException excepcion) when (EsDuplicado(excepcion))
        {
            throw new ExcepcionConflictoDominio(
                "El asiento ya está ocupado en esta función.",
                "ASIENTO_OCUPADO");
        }

        return await ObtenerRespuestaReserva(reserva.Id, tokenCancelacion);
    }

    public async Task<RespuestaReserva> Cancelar(int id, CancellationToken tokenCancelacion)
    {
        // Solo permite cancelar una reserva activa antes del inicio de la función.
        DateTimeOffset ahora = AhoraUtc;

        DateTimeOffset? inicioFuncion = await db.Reservas
            .Where(reserva => reserva.Id == id)
            .Select(reserva => (DateTimeOffset?)reserva.Funcion.InicioUtc)
            .FirstOrDefaultAsync(tokenCancelacion);

        if (!inicioFuncion.HasValue)
        {
            throw NoEncontrado("Reserva");
        }

        if (inicioFuncion.Value <= ahora)
        {
            throw new ExcepcionConflictoDominio("La función ya comenzó.", "FUNCION_INICIADA");
        }

        int filasActualizadas = await db.Reservas
            .Where(reserva =>
                reserva.Id == id &&
                reserva.Estado == EntidadReserva.Activa &&
                reserva.Funcion.InicioUtc > ahora)
            .ExecuteUpdateAsync(actualizacion => actualizacion
                .SetProperty(reserva => reserva.Estado, EntidadReserva.Cancelada)
                .SetProperty(reserva => reserva.CanceladaUtc, ahora), tokenCancelacion);

        if (filasActualizadas == 0)
        {
            throw new ExcepcionConflictoDominio(
                "La reserva ya fue cancelada o la función comenzó.",
                "RESERVA_CANCELADA");
        }

        return await ObtenerRespuestaReserva(id, tokenCancelacion);
    }

    private async Task<RespuestaReserva> ObtenerRespuestaReserva(
        int id,
        CancellationToken tokenCancelacion)
    {
        EntidadReserva? reserva = await db.Reservas
            .AsNoTracking()
            .Include(elemento => elemento.Asiento)
            .FirstOrDefaultAsync(elemento => elemento.Id == id, tokenCancelacion);

        if (reserva is null)
        {
            throw NoEncontrado("Reserva");
        }

        return ConvertirReserva(reserva);
    }

    private async Task GuardarOIndicarConflicto(
        CancellationToken tokenCancelacion,
        string mensaje)
    {
        try
        {
            await db.SaveChangesAsync(tokenCancelacion);
        }
        catch (DbUpdateException)
        {
            throw new ExcepcionConflictoDominio(mensaje, "HISTORIAL_EXISTENTE");
        }
    }

    private static bool EsDuplicado(DbUpdateException excepcion)
    {
        return excepcion.InnerException is SqlException excepcionSql &&
            (excepcionSql.Number == 2601 || excepcionSql.Number == 2627);
    }

    private static ExcepcionNoEncontrado NoEncontrado(string nombre)
    {
        return new ExcepcionNoEncontrado($"No se encontró {nombre}.");
    }

    private static RespuestaPelicula ConvertirPelicula(Pelicula pelicula)
    {
        return new RespuestaPelicula(pelicula.Id, pelicula.Titulo, pelicula.DuracionMinutos);
    }

    private static RespuestaSala ConvertirSala(Sala sala)
    {
        return new RespuestaSala(sala.Id, sala.Nombre, sala.Filas, sala.AsientosPorFila);
    }

    private static RespuestaReserva ConvertirReserva(EntidadReserva reserva)
    {
        string fila = ((char)('A' + reserva.Asiento.Fila - 1)).ToString();
        string codigoAsiento = fila + reserva.Asiento.Numero;

        return new RespuestaReserva(
            reserva.Id,
            reserva.FuncionId,
            reserva.AsientoId,
            codigoAsiento,
            reserva.Titular,
            reserva.Estado,
            reserva.CreadaUtc,
            reserva.CanceladaUtc);
    }
}
