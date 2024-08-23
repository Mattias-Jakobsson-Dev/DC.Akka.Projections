using System.Collections.Immutable;
using Akka.Actor;
using DC.Akka.Projections.Configuration;
using JetBrains.Annotations;

namespace DC.Akka.Projections;

[PublicAPI]
public class ProjectionSequencer<TId, TDocument> : ReceiveActor
    where TId : notnull where TDocument : notnull
{
    public static class Commands
    {
        public record StartProjecting(IImmutableList<EventWithPosition> Events);

        public record IdFinished(TId Id, Messages.IProjectEventsResponse Result);

        public record TaskFinished(Guid GroupId, Guid TaskId);

        public record WaitForGroupToFinish(Guid GroupId, PositionData PositionData);
    }

    public static class Responses
    {
        public record StartProjectingResponse(
            IImmutableList<(Guid groupId, Guid taskId, Task<Messages.IProjectEventsResponse> task)> Tasks);

        public record WaitForGroupToFinishResponse(PositionData PositionData);
    }

    private readonly List<TId> _inprogressIds = [];
    private readonly Dictionary<Guid, List<Guid>> _groupWaitingTasks = new();
    private readonly Dictionary<Guid, List<(PositionData positionData, IActorRef respondTo)>> _groupWaiters = new();

    private readonly ProjectionConfiguration _configuration;

    private readonly
        Dictionary<TId, Queue<(IImmutableList<EventWithPosition> events,
            TaskCompletionSource<Messages.IProjectEventsResponse>
            task)>> _queues = new();

    public ProjectionSequencer(ProjectionConfiguration configuration)
    {
        _configuration = configuration;

        Receive<Commands.StartProjecting>(cmd =>
        {
            var tasks = new List<(Guid groupId, Guid taskId, Task<Messages.IProjectEventsResponse> task)>();

            var groupedEvents = cmd
                .Events
                .SelectMany((x, index) => _configuration
                    .TransformEvent(x.Event)
                    .Select(y => new
                    {
                        EventWithPosition = x with { Event = y },
                        Index = index
                    }))
                .Select(x => new
                {
                    Event = x.EventWithPosition,
                    Id = _configuration.GetDocumentIdFrom(x.EventWithPosition.Event),
                    x.Index
                })
                .GroupBy(x => x.Id)
                .Select(x => (
                    Events: x.Select(y => y.Event).ToImmutableList(),
                    Id: x.Key,
                    Index: x.Min(y => y.Index)))
                .OrderBy(x => x.Index)
                .Select(x => new
                {
                    x.Events,
                    x.Id,
                    TaskId = Guid.NewGuid()
                });

            var self = Self;

            foreach (var chunk in groupedEvents
                         .Chunk(configuration.ProjectionEventBatchingStrategy.GetParallelism()))
            {
                var groupId = Guid.NewGuid();

                foreach (var groupedEvent in chunk)
                {
                    if (!groupedEvent.Id.IsUsable)
                    {
                        tasks.Add((groupId, groupedEvent.TaskId, Task.FromResult<Messages.IProjectEventsResponse>(
                            new Messages.Acknowledge(groupedEvent.Events.GetHighestEventNumber()))));

                        continue;
                    }

                    var id = (TId)groupedEvent.Id.Id;

                    if (!_inprogressIds.Contains(id))
                    {
                        var task = Run(id, groupedEvent.Events);

                        task
                            .ContinueWith(result => self.Tell(new Commands.IdFinished(
                                id,
                                result.IsCompletedSuccessfully
                                    ? result.Result
                                    : new Messages.Reject(result.Exception ?? new Exception("Failed projecting events")))));

                        _inprogressIds.Add(id);

                        tasks.Add((groupId, groupedEvent.TaskId, task));
                    }
                    else
                    {
                        var promise = new TaskCompletionSource<Messages.IProjectEventsResponse>(
                            TaskCreationOptions.RunContinuationsAsynchronously);

                        if (!_queues.TryGetValue(id, out var value))
                        {
                            value =
                                new Queue<(IImmutableList<EventWithPosition>,
                                    TaskCompletionSource<Messages.IProjectEventsResponse>)>();

                            _queues[id] = value;
                        }

                        value.Enqueue((groupedEvent.Events, promise));

                        tasks.Add((groupId, groupedEvent.TaskId, promise.Task));
                    }
                }

                _groupWaitingTasks[groupId] = tasks.Select(x => x.taskId).ToList();

                foreach (var task in tasks)
                {
                    task
                        .task
                        .ContinueWith(_ => self.Tell(new Commands.TaskFinished(groupId, task.taskId)));
                }
            }

            Sender.Tell(new Responses.StartProjectingResponse(tasks.ToImmutableList()));
        });

        Receive<Commands.IdFinished>(cmd =>
        {
            if (_queues.TryGetValue(cmd.Id, out var value))
            {
                if (cmd.Result is not Messages.Acknowledge)
                {
                    while (value.TryDequeue(out var item))
                    {
                        item.task.SetResult(cmd.Result);
                    }
                }
                else if (value.TryDequeue(out var item))
                {
                    var self = Self;

                    Run(cmd.Id, item.events)
                        .ContinueWith(result =>
                        {
                            if (result.IsCompletedSuccessfully)
                            {
                                item.task.SetResult(result.Result);
                                
                                self.Tell(cmd with { Result = result.Result });
                            }
                            else
                            {
                                var error = result.Exception ??
                                            new Exception("Failed projecting events");
                                
                                item.task.TrySetException(error);
                                
                                self.Tell(new Messages.Reject(result.Exception ?? error));
                            }
                        });

                    return;
                }

                _queues.Remove(cmd.Id);
            }

            if (_inprogressIds.Contains(cmd.Id))
                _inprogressIds.Remove(cmd.Id);
        });

        Receive<Commands.TaskFinished>(cmd =>
        {
            if (_groupWaitingTasks.TryGetValue(cmd.GroupId, out var tasks))
                tasks.Remove(cmd.TaskId);

            if (_groupWaitingTasks.TryGetValue(cmd.GroupId, out var value) && value.Count != 0)
                return;

            _groupWaitingTasks.Remove(cmd.GroupId);

            if (!_groupWaiters.TryGetValue(cmd.GroupId, out var groupWaiter))
                return;

            foreach (var waiter in groupWaiter)
            {
                waiter.respondTo.Tell(new Responses.WaitForGroupToFinishResponse(waiter.positionData));
            }

            _groupWaiters.Remove(cmd.GroupId);
        });

        Receive<Commands.WaitForGroupToFinish>(cmd =>
        {
            if (!_groupWaitingTasks.TryGetValue(cmd.GroupId, out var value) || value.Count == 0)
            {
                Sender.Tell(new Responses.WaitForGroupToFinishResponse(cmd.PositionData));

                return;
            }

            if (!_groupWaiters.ContainsKey(cmd.GroupId))
                _groupWaiters[cmd.GroupId] = [];

            _groupWaiters[cmd.GroupId].Add((cmd.PositionData, Sender));
        });
    }

    private async Task<Messages.IProjectEventsResponse> Run(
        TId id,
        IImmutableList<EventWithPosition> events)
    {
        var projector = await _configuration.ProjectorFactory.GetProjector<TId, TDocument>(id, _configuration);

        return await projector.ProjectEvents(events, _configuration.GetProjection().ProjectionTimeout);
    }

    public static IActorRef Create(IActorRefFactory refFactory, ProjectionConfiguration configuration)
    {
        return refFactory.ActorOf(
            Props.Create(() => new ProjectionSequencer<TId, TDocument>(configuration)));
    }
}