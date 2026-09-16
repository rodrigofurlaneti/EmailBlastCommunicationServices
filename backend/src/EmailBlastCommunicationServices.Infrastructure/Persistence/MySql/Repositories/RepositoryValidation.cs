namespace EmailBlastCommunicationServices.Infrastructure.Persistence.MySql.Repositories;

internal static class RepositoryValidation
{
    public static void Page(int offset, int limit)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        if (limit is < 1 or > 1000) throw new ArgumentOutOfRangeException(nameof(limit), "Use entre 1 e 1000 registros.");
    }

    public static void RequiredText(string value, int maximum, string name)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > maximum)
            throw new ArgumentException($"{name} deve conter entre 1 e {maximum} caracteres.", name);
    }
}
