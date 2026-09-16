using EmailBlastCommunicationServices.Domain.Entities;
using EmailBlastCommunicationServices.Domain.Queries;
using EmailBlastCommunicationServices.Domain.ValueObjects;
using EmailBlastCommunicationServices.Domain.Interfaces.Base;

namespace EmailBlastCommunicationServices.Domain.Interfaces;

/// <summary>
/// CRUD de logs limitado ao sistema da chave/consulta. CreateAsync deve inserir Queued
/// e retornar o ID gerado pelo banco; UpdateAsync deve respeitar as transições de entrega.
/// </summary>
public interface IEmailLogRepository : ICrudRepository<EmailLog, EmailLogKey, EmailLogQuery>
{
}
