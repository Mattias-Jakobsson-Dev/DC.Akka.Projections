using System.Collections.Immutable;
using Akka;
using Akka.Streams;
using Akka.Streams.Dsl;
using MJ.Akka.Projections.Storage;

namespace MJ.Akka.Projections.Configuration;

public class ProjectionConfiguration<TId, TDocument>(
    IProjection<TId, TDocument> projection,
    IProjectionStorage documentStorage,
    IProjectionPositionStorage positionStorage,
    IKeepTrackOfProjectors projectorFactory,
    RestartSettings? restartSettings,
    IEventBatchingStrategy projectionEventBatchingStrategy,
    IEventPositionBatchingStrategy positionBatchingStrategy,
    IHandleEventInProjection<TDocument> eventsHandler) 
    : ProjectionConfiguration(
        projection,
        documentStorage,
        positionStorage,
        projectorFactory,
        restartSettings,
        projectionEventBatchingStrategy,
        positionBatchingStrategy) where TId : notnull where TDocument : notnull
{
    public override string IdToString(object id)
    {
        return projection.IdToString((TId)id);
    }

    public override object IdFromString(string id)
    {
        return projection.IdFromString(id);
    }

    public override IImmutableList<object> TransformEvent(object evnt)
    {
        return eventsHandler.Transform(evnt);
    }

    public override DocumentId GetDocumentIdFrom(object evnt)
    {
        return eventsHandler.GetDocumentIdFrom(evnt);
    }

    public override async Task<(object? document, bool hasHandler)> HandleEvent(
        object? document,
        object evnt,
        long position,
        CancellationToken cancellationToken)
    {
        var response = await eventsHandler.Handle(
            (TDocument?)document,
            evnt, 
            position,
            cancellationToken);

        return response;
    }
}

public abstract class ProjectionConfiguration(
    IProjection projection,
    IProjectionStorage documentStorage,
    IProjectionPositionStorage positionStorage,
    IKeepTrackOfProjectors projectorFactory,
    RestartSettings? restartSettings,
    IEventBatchingStrategy projectionEventBatchingStrategy,
    IEventPositionBatchingStrategy positionBatchingStrategy)
{
    public string Name { get; } = projection.Name;
    public IProjectionStorage DocumentStorage { get; } = documentStorage;
    public IProjectionPositionStorage PositionStorage { get; } = positionStorage;
    public IKeepTrackOfProjectors ProjectorFactory { get; } = projectorFactory;
    public RestartSettings? RestartSettings { get; } = restartSettings;
    public IEventBatchingStrategy ProjectionEventBatchingStrategy { get; } = projectionEventBatchingStrategy;
    public IEventPositionBatchingStrategy PositionBatchingStrategy { get; } = positionBatchingStrategy;

    public IProjection GetProjection()
    {
        return projection;
    }
    
    public abstract string IdToString(object id);

    public abstract object IdFromString(string id);
    
    public Source<EventWithPosition, NotUsed> StartSource(long? fromPosition)
    {
        return projection.StartSource(fromPosition);
    }
    
    public abstract IImmutableList<object> TransformEvent(object evnt);
    
    public abstract DocumentId GetDocumentIdFrom(object evnt);
    
    public abstract Task<(object? document, bool hasHandler)> HandleEvent(
        object? document,
        object evnt,
        long position,
        CancellationToken cancellationToken);
}
