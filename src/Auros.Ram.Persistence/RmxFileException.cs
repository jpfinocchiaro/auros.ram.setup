using Auros.Ram.Persistence.Resources;

namespace Auros.Ram.Persistence;

public enum RmxErrorKind
{
    NotRmx,
    Corrupted,
    NewerVersion,
    ObsoleteVersion,
    WrongPassword,
    WrongKind,
    IoError,
}

/// <summary>Error al abrir o guardar un archivo, con un mensaje apto para el usuario (RNF-47).</summary>
public sealed class RmxFileException : Exception
{
    public RmxFileException(RmxErrorKind kind, Exception? inner = null)
        : base(MessageFor(kind), inner)
    {
        Kind = kind;
    }

    public RmxErrorKind Kind { get; }

    private static string MessageFor(RmxErrorKind kind) => kind switch
    {
        RmxErrorKind.NotRmx => FileMessages.Error_NotRmx,
        RmxErrorKind.Corrupted => FileMessages.Error_Corrupted,
        RmxErrorKind.NewerVersion => FileMessages.Error_NewerVersion,
        RmxErrorKind.ObsoleteVersion => FileMessages.Error_ObsoleteVersion,
        RmxErrorKind.WrongPassword => FileMessages.Error_WrongPassword,
        RmxErrorKind.WrongKind => FileMessages.Error_WrongKind,
        _ => FileMessages.Error_Io,
    };
}
