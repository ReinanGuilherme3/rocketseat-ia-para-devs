namespace Desafios.RAG.Shared;

/// <summary>
/// Resolve o caminho absoluto de um arquivo dentro de RAG/Recursos a partir da pasta
/// de saída do build (bin/Debug/net10.0/...). Os quatro ".." sobem de volta até a
/// pasta RAG, de onde entra em Recursos/{fileName}.
/// </summary>
public static class BookPathResolver
{
    public static string ResolveRecursoPath(string fileName)
        => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "Recursos", fileName));
}
