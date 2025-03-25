using Raven.Client.Documents;
using Raven.Client.Documents.Operations;
using Raven.Client.Exceptions;
using Raven.Client.Exceptions.Database;
using Raven.Client.ServerWide;
using Raven.Client.ServerWide.Operations;

namespace MJ.Akka.Projections.EventStoreToRavenDbExample;

public static class DocumentStoreExtensions
{
    public static void EnsureDatabaseExists(
        this IDocumentStore store,
        string? database = null,
        bool createDatabaseIfNotExists = true)
    {
        database ??= store.Database;
        
        if (string.IsNullOrWhiteSpace(database))
            throw new ArgumentException("Value cannot be null or whitespace.", nameof (database));
        
        try
        {
            store.Maintenance.ForDatabase(database).Send(new GetStatisticsOperation());
        }
        catch (DatabaseDoesNotExistException)
        {
            if (!createDatabaseIfNotExists)
            {
                throw;
            }

            try
            {
                store.Maintenance.Server.Send(new CreateDatabaseOperation(new DatabaseRecord(database), 1));
            }
            catch (ConcurrencyException)
            {
            }
        }
    }
}
