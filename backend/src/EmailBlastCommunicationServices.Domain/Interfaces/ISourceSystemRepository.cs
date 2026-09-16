using EmailBlastCommunicationServices.Domain.Entities;
using EmailBlastCommunicationServices.Domain.Queries;
using EmailBlastCommunicationServices.Domain.Interfaces.Base;

namespace EmailBlastCommunicationServices.Domain.Interfaces;

public interface ISourceSystemRepository : ICrudRepository<SourceSystem, int, PageQuery>
{
}
