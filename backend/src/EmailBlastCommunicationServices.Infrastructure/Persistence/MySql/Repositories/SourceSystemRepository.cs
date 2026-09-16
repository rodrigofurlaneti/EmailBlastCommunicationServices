using Microsoft.EntityFrameworkCore;
using EmailBlastCommunicationServices.Domain.Entities;
using EmailBlastCommunicationServices.Domain.Interfaces;
using EmailBlastCommunicationServices.Domain.Queries;
using EmailBlastCommunicationServices.Infrastructure.Persistence.Context;

namespace EmailBlastCommunicationServices.Infrastructure.Persistence.MySql.Repositories;

public sealed class SourceSystemRepository(EmailBlastDbContext context) : ISourceSystemRepository
{
    public async Task<int> CreateAsync(SourceSystem entity, CancellationToken ct)
    {
        Validate(entity);
        if (entity.Id != 0) throw new ArgumentException("O ID de um novo registro deve ser zero.");
        context.Systems.Add(entity);
        try { await context.SaveChangesAsync(ct); return entity.Id; }
        finally { context.Entry(entity).State = EntityState.Detached; }
    }

    public Task<SourceSystem?> GetByIdAsync(int key, CancellationToken ct)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(key);
        return context.Systems.AsNoTracking().SingleOrDefaultAsync(entity => entity.Id == key, ct);
    }

    public async Task<IReadOnlyList<SourceSystem>> ListAsync(PageQuery query, CancellationToken ct)
    {
        RepositoryValidation.Page(query.Offset, query.Limit);
        return await context.Systems.AsNoTracking().OrderBy(entity => entity.Id)
            .Skip(query.Offset).Take(query.Limit).ToListAsync(ct);
    }

    public async Task<bool> UpdateAsync(SourceSystem entity, CancellationToken ct)
    {
        Validate(entity);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(entity.Id);
        return await context.Systems.Where(current => current.Id == entity.Id).ExecuteUpdateAsync(
            setters => setters.SetProperty(current => current.Name, entity.Name)
                .SetProperty(current => current.Description, entity.Description), ct) > 0;
    }

    public async Task<bool> DeleteAsync(int key, CancellationToken ct)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(key);
        return await context.Systems.Where(entity => entity.Id == key).ExecuteDeleteAsync(ct) > 0;
    }

    private static void Validate(SourceSystem entity)
    {
        ArgumentNullException.ThrowIfNull(entity);
        RepositoryValidation.RequiredText(entity.Name, 100, nameof(entity.Name));
        if (entity.Description?.Length > 255) throw new ArgumentException("Description excede 255 caracteres.");
    }
}
