namespace Cine.Dominio;

public class ExcepcionDominio : Exception
{
    public ExcepcionDominio(string message, string code) : base(message)
    {
        Codigo = code;
    }

    public string Codigo { get; }
}

public sealed class ExcepcionValidacionDominio : ExcepcionDominio
{
    public ExcepcionValidacionDominio(string message)
        : base(message, "VALIDACION")
    {
    }
}

public sealed class ExcepcionConflictoDominio : ExcepcionDominio
{
    public ExcepcionConflictoDominio(string message, string code = "CONFLICTO")
        : base(message, code)
    {
    }
}

public sealed class ExcepcionNoEncontrado : ExcepcionDominio
{
    public ExcepcionNoEncontrado(string message)
        : base(message, "NO_ENCONTRADO")
    {
    }
}
