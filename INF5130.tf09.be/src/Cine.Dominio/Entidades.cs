namespace Cine.Dominio;

// Guarda los datos de una película y valida sus cambios.
public sealed class Pelicula
{
    private Pelicula() { }

    public Pelicula(string titulo, int duracionMinutos)
    {
        Cambiar(titulo, duracionMinutos);
    }

    public int Id { get; private set; }
    public string Titulo { get; private set; } = "";
    public int DuracionMinutos { get; private set; }

    // Constructor y cambios pasan por las mismas reglas de validación.
    public void Cambiar(string titulo, int duracionMinutos)
    {
        Titulo = ValidarTexto(titulo, 150, "El título");

        if (duracionMinutos < 20 || duracionMinutos > 300)
        {
            throw new ExcepcionValidacionDominio("La duración debe estar entre 20 y 300 minutos.");
        }

        DuracionMinutos = duracionMinutos;
    }

    internal static string ValidarTexto(string? texto, int maximo, string campo)
    {
        string valor = texto?.Trim() ?? "";

        if (valor.Length == 0 || valor.Length > maximo)
        {
            throw new ExcepcionValidacionDominio($"{campo} requiere entre 1 y {maximo} caracteres.");
        }

        return valor;
    }
}

// Una sala está compuesta por sus asientos; al crearla se genera su plano.
public sealed class Sala
{
    private Sala()
    {
        Asientos = new List<Asiento>();
    }

    public Sala(string nombre, int filas, int asientosPorFila)
    {
        Nombre = Pelicula.ValidarTexto(nombre, 80, "El nombre de sala");

        if (filas < 1 || filas > 12 || asientosPorFila < 1 || asientosPorFila > 20)
        {
            throw new ExcepcionValidacionDominio(
                "La sala debe tener entre 1 y 12 filas y entre 1 y 20 asientos por fila.");
        }

        Filas = filas;
        AsientosPorFila = asientosPorFila;
        Asientos = new List<Asiento>();

        // Por cada fila se crean todos los números de asiento.
        for (int fila = 1; fila <= filas; fila++)
        {
            for (int numero = 1; numero <= asientosPorFila; numero++)
            {
                Asientos.Add(new Asiento(fila, numero));
            }
        }
    }

    public int Id { get; private set; }
    public string Nombre { get; private set; } = "";
    public int Filas { get; private set; }
    public int AsientosPorFila { get; private set; }
    public List<Asiento> Asientos { get; private set; }

    public void CambiarNombre(string nombre)
    {
        Nombre = Pelicula.ValidarTexto(nombre, 80, "El nombre de sala");
    }
}

// Identifica una posición mediante la fila y el número dentro de una sala.
public sealed class Asiento
{
    private Asiento() { }

    internal Asiento(int fila, int numero)
    {
        Fila = fila;
        Numero = numero;
    }

    public int Id { get; private set; }
    public int SalaId { get; private set; }
    public int Fila { get; private set; }
    public int Numero { get; private set; }
}

// Une una película y una sala con sus horarios de inicio y fin.
public sealed class Funcion
{
    private Funcion() { }

    public Funcion(int peliculaId, int salaId, DateTimeOffset inicioUtc, int duracionMinutos)
    {
        DateTimeOffset ahora = DateTimeOffset.UtcNow;

        if (inicioUtc <= ahora || inicioUtc > ahora.AddDays(90))
        {
            throw new ExcepcionValidacionDominio("Programe la función dentro de los próximos 90 días.");
        }

        if (duracionMinutos < 20 || duracionMinutos > 300)
        {
            throw new ExcepcionValidacionDominio("La duración de la película no es válida.");
        }

        PeliculaId = peliculaId;
        SalaId = salaId;
        // Normaliza la hora para guardar y comparar todos los horarios en UTC.
        InicioUtc = inicioUtc.ToUniversalTime();
        FinUtc = InicioUtc.AddMinutes(duracionMinutos);
    }

    public int Id { get; private set; }
    public int PeliculaId { get; private set; }
    public Pelicula Pelicula { get; private set; } = null!;
    public int SalaId { get; private set; }
    public Sala Sala { get; private set; } = null!;
    public DateTimeOffset InicioUtc { get; private set; }
    public DateTimeOffset FinUtc { get; private set; }
}

// Guarda quién reservó el asiento y si la reserva sigue activa o fue cancelada.
public sealed class Reserva
{
    public const string Activa = "Activa";
    public const string Cancelada = "Cancelada";

    private Reserva() { }

    public Reserva(int funcionId, int asientoId, int salaId, string titular, DateTimeOffset ahoraUtc)
    {
        FuncionId = funcionId;
        AsientoId = asientoId;
        SalaId = salaId;
        Titular = Pelicula.ValidarTexto(titular, 120, "El titular");
        Estado = Activa;
        CreadaUtc = ahoraUtc.ToUniversalTime();
    }

    public int Id { get; private set; }
    public int FuncionId { get; private set; }
    public Funcion Funcion { get; private set; } = null!;
    public int AsientoId { get; private set; }
    public Asiento Asiento { get; private set; } = null!;
    public int SalaId { get; private set; }
    public string Titular { get; private set; } = "";
    public string Estado { get; private set; } = Activa;
    public DateTimeOffset CreadaUtc { get; private set; }
    public DateTimeOffset? CanceladaUtc { get; private set; }

}
