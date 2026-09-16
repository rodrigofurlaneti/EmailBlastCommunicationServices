using EmailBlastCommunicationServices.Application.Interfaces.Persistence;
namespace EmailBlastCommunicationServices.Application.Queries.GetEmail;

public sealed class GetEmailHandler(IEmailStore store)
{
    public async Task<GetEmailResult> GetAsync(int id, int systemId, CancellationToken ct)
    {
        if (id <= 0 || systemId <= 0 || !await store.SystemExistsAsync(systemId, ct))
            return new(null, true);
        return new(await store.GetAsync(id, systemId, ct), false);
    }
}
