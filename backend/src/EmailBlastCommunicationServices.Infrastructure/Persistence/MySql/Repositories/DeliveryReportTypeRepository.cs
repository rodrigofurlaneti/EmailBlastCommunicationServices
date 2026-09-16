using Microsoft.EntityFrameworkCore;
using EmailBlastCommunicationServices.Domain.Entities;
using EmailBlastCommunicationServices.Domain.Interfaces;
using EmailBlastCommunicationServices.Domain.Queries;
using EmailBlastCommunicationServices.Infrastructure.Persistence.Context;

namespace EmailBlastCommunicationServices.Infrastructure.Persistence.MySql.Repositories;

public sealed class DeliveryReportTypeRepository(EmailBlastDbContext context) : IDeliveryReportTypeRepository
{
    public async Task<int> CreateAsync(DeliveryReportType entity, CancellationToken ct)
    {
        Validate(entity);
        if (entity.Id != 0) throw new ArgumentException("O ID de um novo registro deve ser zero.");
        context.DeliveryReportTypes.Add(entity);
        try { await context.SaveChangesAsync(ct); return entity.Id; }
        finally { context.Entry(entity).State = EntityState.Detached; }
    }

    public Task<DeliveryReportType?> GetByIdAsync(int key, CancellationToken ct)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(key);
        return context.DeliveryReportTypes.AsNoTracking().SingleOrDefaultAsync(entity => entity.Id == key, ct);
    }

    public async Task<IReadOnlyList<DeliveryReportType>> ListAsync(PageQuery query, CancellationToken ct)
    {
        RepositoryValidation.Page(query.Offset, query.Limit);
        return await context.DeliveryReportTypes.AsNoTracking().OrderBy(entity => entity.Id)
            .Skip(query.Offset).Take(query.Limit).ToListAsync(ct);
    }

    public async Task<bool> UpdateAsync(DeliveryReportType entity, CancellationToken ct)
    {
        Validate(entity);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(entity.Id);
        return await context.DeliveryReportTypes.Where(current => current.Id == entity.Id).ExecuteUpdateAsync(
            setters => setters.SetProperty(current => current.StatusName, entity.StatusName), ct) > 0;
    }

    public async Task<bool> DeleteAsync(int key, CancellationToken ct)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(key);
        return await context.DeliveryReportTypes.Where(entity => entity.Id == key).ExecuteDeleteAsync(ct) > 0;
    }

    private static void Validate(DeliveryReportType entity)
    {
        ArgumentNullException.ThrowIfNull(entity);
        RepositoryValidation.RequiredText(entity.StatusName, 50, nameof(entity.StatusName));
    }
}
