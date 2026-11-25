using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Cine.Aplicacion;
using Cine.Dominio;
using Cine.Infraestructura;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

// Inicio y configuración del servidor.
var constructor = WebApplication.CreateBuilder(args);

// Conexión a SQL Server.
string cadenaConexion = constructor.Configuration.GetConnectionString("Cine")
    ?? throw new InvalidOperationException("Falta configurar ConnectionStrings:Cine.");

// Registra el acceso a SQL y el servicio que ejecuta las operaciones del cine.
constructor.Services.AddDbContext<ContextoCine>(opciones =>
    opciones.UseSqlServer(cadenaConexion));
constructor.Services.AddScoped<IServicioCine, ServicioCine>();

// Configuración del inicio de sesión con token JWT.
string claveJwt = constructor.Configuration["Jwt:Key"]
    ?? throw new InvalidOperationException("Falta configurar Jwt:Key.");

var claveFirma = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(claveJwt));

constructor.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(opciones =>
    {
        opciones.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = constructor.Configuration["Jwt:Issuer"],
            ValidateAudience = true,
            ValidAudience = constructor.Configuration["Jwt:Audience"],
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = claveFirma,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30)
        };
    });

constructor.Services.AddAuthorization(opciones =>
{
    // El administrador mantiene películas y salas; ambos roles pueden gestionar funciones y reservas.
    opciones.AddPolicy("Administrador", politica => politica.RequireRole("Administrador"));
    opciones.AddPolicy("Gestion", politica =>
        politica.RequireRole("Administrador", "Gestor"));
});

string[] origenesFrontend = constructor.Configuration
    .GetSection("Cors:Origins")
    .Get<string[]>() ?? new[]
    {
        "https://localhost:59538",
        "http://localhost:59539"
    };

constructor.Services.AddCors(opciones =>
{
    opciones.AddPolicy("InterfazWeb", politica =>
    {
        politica
            .WithOrigins(origenesFrontend)
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});

var aplicacion = constructor.Build();

// Convierte errores del sistema en respuestas claras para el frontend.
aplicacion.Use(async (contextoHttp, siguiente) =>
{
    try
    {
        await siguiente();
    }
    catch (ExcepcionDominio excepcion)
    {
        // Convierte los errores de reglas del cine en códigos HTTP conocidos por el frontend.
        int codigoEstado = 400;

        if (excepcion is ExcepcionNoEncontrado)
        {
            codigoEstado = 404;
        }
        else if (excepcion is ExcepcionConflictoDominio)
        {
            codigoEstado = 409;
        }

        contextoHttp.Response.StatusCode = codigoEstado;
        contextoHttp.Response.ContentType = "application/problem+json";
        await contextoHttp.Response.WriteAsJsonAsync(new
        {
            type = "about:blank",
            title = excepcion.Message,
            status = codigoEstado,
            detail = excepcion.Message,
            code = excepcion.Codigo
        });
    }
    catch (DbUpdateException excepcion)
        when (excepcion.InnerException is Microsoft.Data.SqlClient.SqlException excepcionSql &&
              (excepcionSql.Number == 2601 || excepcionSql.Number == 2627))
    {
        contextoHttp.Response.StatusCode = 409;
        contextoHttp.Response.ContentType = "application/problem+json";
        await contextoHttp.Response.WriteAsJsonAsync(new
        {
            title = "Conflicto de datos",
            status = 409,
            detail = "El registro ya existe o fue modificado simultáneamente.",
            code = "CONFLICTO_DATOS"
        });
    }
});

aplicacion.UseCors("InterfazWeb");
aplicacion.UseAuthentication();
aplicacion.UseAuthorization();

aplicacion.MapPost("/api/auth/login", (
    EntradaInicioSesion entrada,
    IConfiguration configuracion) =>
{
    // Busca una cuenta de demostración usando las claves configuradas en appsettings.
    var cuentas = configuracion.GetSection("DemoUsers").GetChildren();
    var cuenta = cuentas.FirstOrDefault(elemento =>
        string.Equals(
            elemento["Email"],
            entrada.Correo.Trim(),
            StringComparison.OrdinalIgnoreCase) &&
        string.Equals(
            elemento["Password"],
            entrada.Contrasena,
            StringComparison.Ordinal));

    if (cuenta is null)
    {
        return Results.Unauthorized();
    }

    string correo = cuenta["Email"]!;
    string rol = cuenta["Role"]!;

    var datosDelToken = new List<Claim>
    {
        new Claim(JwtRegisteredClaimNames.Sub, correo),
        new Claim(JwtRegisteredClaimNames.Email, correo),
        new Claim(ClaimTypes.Name, correo),
        new Claim(ClaimTypes.Role, rol)
    };

    // El token identifica al usuario y su rol; el frontend lo usará al llamar las otras rutas.
    DateTime fechaExpiracion = DateTime.UtcNow.AddHours(4);
    var token = new JwtSecurityToken(
        constructor.Configuration["Jwt:Issuer"],
        constructor.Configuration["Jwt:Audience"],
        datosDelToken,
        expires: fechaExpiracion,
        signingCredentials: new SigningCredentials(claveFirma, SecurityAlgorithms.HmacSha256));

    string textoToken = new JwtSecurityTokenHandler().WriteToken(token);
    return Results.Ok(new RespuestaSesion(textoToken, correo, new[] { rol }));
}).AllowAnonymous();

// Rutas protegidas de películas, salas, funciones y reservas.
var api = aplicacion.MapGroup("/api").RequireAuthorization("Gestion");
RegistrarRutasPeliculas(api);
RegistrarRutasSalas(api);
RegistrarRutasFunciones(api);
RegistrarRutasReservas(api);

api.MapGet("/dashboard", (IServicioCine servicio, CancellationToken tokenCancelacion) =>
    servicio.ObtenerResumen(tokenCancelacion));

// Si está activada la opción, prueba la conexión antes de iniciar la API.
using (var scope = aplicacion.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ContextoCine>();

    if (aplicacion.Configuration.GetValue<bool>("Database:CheckOnStartup"))
    {
        try
        {
            await db.Database.OpenConnectionAsync();
            await db.Database.CloseConnectionAsync();
        }
        catch (Exception excepcion)
        {
            var conexion = db.Database.GetDbConnection();
            throw new InvalidOperationException(
                $"No se pudo conectar con la base '{conexion.Database}' en " +
                $"'{conexion.DataSource}': {excepcion.GetBaseException().Message}",
                excepcion);
        }
    }
}

aplicacion.Run();

static void RegistrarRutasPeliculas(RouteGroupBuilder api)
{
    // Consulta, crea, edita y elimina películas. Los cambios requieren rol administrador.
    var peliculas = api.MapGroup("/peliculas");

    peliculas.MapGet("", (IServicioCine servicio, CancellationToken tokenCancelacion) =>
        servicio.Peliculas(tokenCancelacion));
    peliculas.MapPost("", async (
        EntradaPelicula entrada,
        IServicioCine servicio,
        CancellationToken tokenCancelacion) =>
    {
        RespuestaPelicula pelicula = await servicio.GuardarPelicula(null, entrada, tokenCancelacion);
        return Results.Created($"/api/peliculas/{pelicula.Id}", pelicula);
    }).RequireAuthorization("Administrador");
    peliculas.MapPut("/{id:int}", (
        int id,
        EntradaPelicula entrada,
        IServicioCine servicio,
        CancellationToken tokenCancelacion) =>
            servicio.GuardarPelicula(id, entrada, tokenCancelacion))
        .RequireAuthorization("Administrador");
    peliculas.MapDelete("/{id:int}", async (
        int id,
        IServicioCine servicio,
        CancellationToken tokenCancelacion) =>
    {
        await servicio.EliminarPelicula(id, tokenCancelacion);
        return Results.NoContent();
    }).RequireAuthorization("Administrador");
}

static void RegistrarRutasSalas(RouteGroupBuilder api)
{
    // Crear una sala también genera su conjunto de asientos.
    var salas = api.MapGroup("/salas");

    salas.MapGet("", (IServicioCine servicio, CancellationToken tokenCancelacion) =>
        servicio.Salas(tokenCancelacion));
    salas.MapPost("", async (
        EntradaSala entrada,
        IServicioCine servicio,
        CancellationToken tokenCancelacion) =>
    {
        RespuestaSala sala = await servicio.GuardarSala(null, entrada, tokenCancelacion);
        return Results.Created($"/api/salas/{sala.Id}", sala);
    }).RequireAuthorization("Administrador");
    salas.MapPut("/{id:int}", (
        int id,
        EntradaSala entrada,
        IServicioCine servicio,
        CancellationToken tokenCancelacion) =>
            servicio.GuardarSala(id, entrada, tokenCancelacion))
        .RequireAuthorization("Administrador");
    salas.MapDelete("/{id:int}", async (
        int id,
        IServicioCine servicio,
        CancellationToken tokenCancelacion) =>
    {
        await servicio.EliminarSala(id, tokenCancelacion);
        return Results.NoContent();
    }).RequireAuthorization("Administrador");
}

static void RegistrarRutasFunciones(RouteGroupBuilder api)
{
    // Permite crear funciones y consultar el mapa de asientos de una función.
    var funciones = api.MapGroup("/funciones");

    funciones.MapGet("", (IServicioCine servicio, CancellationToken tokenCancelacion) =>
        servicio.Funciones(tokenCancelacion));
    funciones.MapPost("", async (
        EntradaFuncion entrada,
        IServicioCine servicio,
        CancellationToken tokenCancelacion) =>
    {
        RespuestaFuncion funcion = await servicio.CrearFuncion(entrada, tokenCancelacion);
        return Results.Created($"/api/funciones/{funcion.Id}", funcion);
    });
    funciones.MapGet("/{id:int}/asientos", (
        int id,
        IServicioCine servicio,
        CancellationToken tokenCancelacion) =>
            servicio.Mapa(id, tokenCancelacion));
}

static void RegistrarRutasReservas(RouteGroupBuilder api)
{
    // Permite reservar un asiento y cancelar una reserva.
    var reservas = api.MapGroup("/reservas");

    reservas.MapGet("", (IServicioCine servicio, CancellationToken tokenCancelacion) =>
        servicio.Reservas(tokenCancelacion));
    reservas.MapPost("", async (
        EntradaReserva entrada,
        IServicioCine servicio,
        CancellationToken tokenCancelacion) =>
    {
        RespuestaReserva reserva = await servicio.CrearReserva(entrada, tokenCancelacion);
        return Results.Created($"/api/reservas/{reserva.Id}", reserva);
    });
    reservas.MapPost("/{id:int}/cancelacion", (
        int id,
        IServicioCine servicio,
        CancellationToken tokenCancelacion) =>
            servicio.Cancelar(id, tokenCancelacion));
}

public partial class Program { }
