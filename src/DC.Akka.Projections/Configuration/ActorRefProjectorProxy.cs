using System.Collections.Immutable;
using Akka.Actor;

namespace DC.Akka.Projections.Configuration;

public class ActorRefProjectorProxy<TId, TDocument>(TId id, IActorRef projector) : IProjectorProxy
    where TId : notnull where TDocument : notnull
{
    public Task<Messages.IProjectEventsResponse> ProjectEvents(
        IImmutableList<EventWithPosition> events,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        return projector.Ask<Messages.IProjectEventsResponse>(
            new DocumentProjection<TId, TDocument>.Commands.ProjectEvents(id, events),
            timeout,
            cancellationToken);
    }

    public void StopAllInProgress()
    {
        projector.Tell(new DocumentProjection<TId, TDocument>.Commands.StopInProcessEvents(id));
    }
}