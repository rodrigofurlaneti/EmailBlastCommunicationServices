namespace EmailBlastCommunicationServices.Domain.Interfaces.Base;

/// <summary>Contrato comum de persistência. A chave e a consulta preservam o escopo de cada entidade.</summary>
public interface ICrudRepository<TEntity, in TKey, in TQuery> where TEntity : class
{
    Task<int> CreateAsync(TEntity entity, CancellationToken ct);
    Task<TEntity?> GetByIdAsync(TKey key, CancellationToken ct);
    Task<IReadOnlyList<TEntity>> ListAsync(TQuery query, CancellationToken ct);
    Task<bool> UpdateAsync(TEntity entity, CancellationToken ct);
    Task<bool> DeleteAsync(TKey key, CancellationToken ct);
}
