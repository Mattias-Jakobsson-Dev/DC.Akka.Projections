using System.Collections.Concurrent;
using Akka.Actor;
using Akka.Cluster.Sharding;
using MJ.Akka.Projections;
using MJ.Akka.Projections.Configuration;

namespace MJ.Akka.Projections.Cluster.Sharding;

public class ShardedProjectors(ActorSystem actorSystem, ClusterShardingSettings settings, int maxNumberOfShards) 
    : IKeepTrackOfProjectors
{
    private readonly ConcurrentDictionary<string, Task<IActorRef>> _projectors = new();
    
    public async Task<IProjectorProxy> GetProjector<TId, TDocument>(TId id, ProjectionConfiguration configuration)
        where TId : notnull where TDocument : notnull
    {
        var projector = await _projectors
            .GetOrAdd(
                configuration.Name,
                name => ClusterSharding.Get(actorSystem).StartAsync(
                    $"projection-{name}",
                    documentId => configuration
                        .GetProjection()
                        .CreateProjectionProps(configuration.IdFromString(documentId)),
                    settings,
                    new MessageExtractor<TId, TDocument>(maxNumberOfShards, configuration)));

        return new ActorRefProjectorProxy<TId, TDocument>(id, projector);
    }

    private class MessageExtractor<TId, TDocument>(
        int maxNumberOfShards,
        ProjectionConfiguration configuration)
        : HashCodeMessageExtractor(maxNumberOfShards) where TId : notnull where TDocument : notnull
    {
        public override string EntityId(object message)
        {
            return message is DocumentProjection<TId, TDocument>.Commands.IMessageWithId messageWithId 
                ? configuration.IdToString(messageWithId.Id) 
                : "";
        }
    }
}