using System.Collections.Immutable;
using Akka;
using Akka.Actor;
using Akka.Streams;
using Akka.Streams.Dsl;

namespace MJ.Akka.Projections.Storage;

public class BatchedProjectionStorage : IProjectionStorage
{
    public static IStorageBatchingStrategy DefaultStrategy { get; } = new BatchSizeStorageBatchingStrategy(100);

    private readonly IProjectionStorage _innerStorage;
    private readonly ISourceQueueWithComplete<IPendingWrite> _writeQueue;

    public BatchedProjectionStorage(
        ActorSystem actorSystem,
        IProjectionStorage innerStorage,
        int parallelism,
        IStorageBatchingStrategy batchingStrategy)
    {
        _innerStorage = innerStorage;

        var queue = Source
            .Queue<IPendingWrite>(int.MaxValue, OverflowStrategy.Backpressure);

        _writeQueue = batchingStrategy
            .GetStrategy(queue)
            .SelectAsync(
                parallelism,
                async writes =>
                {
                    var cancelledWrite = writes
                        .Where(x => x.CancellationToken.IsCancellationRequested)
                        .Aggregate(
                            (IPendingWrite)PendingWrite.Empty,
                            (current, pending) => current.MergeWith(pending));

                    cancelledWrite.Fail(new OperationCanceledException("Write was cancelled"));

                    var write = writes
                        .Where(x => !x.CancellationToken.IsCancellationRequested)
                        .Aggregate(
                            (IPendingWrite)PendingWrite.Empty,
                            (current, pending) => current.MergeWith(pending));

                    if (write.IsEmpty)
                        return NotUsed.Instance;
                    
                    try
                    {
                        await _innerStorage.Store(write.ToUpsert, write.ToDelete);

                        write.Completed();
                    }
                    catch (Exception e)
                    {
                        write.Fail(e);
                    }

                    return NotUsed.Instance;
                })
            .ToMaterialized(Sink.Ignore<NotUsed>(), Keep.Left)
            .Run(actorSystem.Materializer());
    }

    public Task<TDocument?> LoadDocument<TDocument>(
        object id,
        CancellationToken cancellationToken = default)
    {
        return _innerStorage.LoadDocument<TDocument>(id, cancellationToken);
    }

    public Task Store(
        IImmutableList<DocumentToStore> toUpsert,
        IImmutableList<DocumentToDelete> toDelete,
        CancellationToken cancellationToken = default)
    {
        var promise = new TaskCompletionSource<NotUsed>(TaskCreationOptions.RunContinuationsAsynchronously);

        _writeQueue.OfferAsync(new PendingWrite(toUpsert, toDelete, promise, cancellationToken))
            .ContinueWith(
                result =>
                {
                    if (result.IsCompletedSuccessfully)
                    {
                        switch (result.Result)
                        {
                            case QueueOfferResult.Enqueued:
                                break;

                            case QueueOfferResult.Failure f:
                                promise.TrySetException(new Exception("Failed to write documents", f.Cause));

                                break;
                            case QueueOfferResult.Dropped:
                                promise.TrySetException(new Exception(
                                    "Failed to enqueue documents batch write, the queue buffer was full"));

                                break;

                            case QueueOfferResult.QueueClosed:
                                promise.TrySetException(new Exception(
                                    "Failed to enqueue documents batch write, the queue was closed."));

                                break;
                        }
                    }
                    else
                    {
                        promise.TrySetException(result.Exception ?? new Exception("Failed to write documents"));
                    }
                },
                cancellationToken: cancellationToken,
                continuationOptions: TaskContinuationOptions.ExecuteSynchronously,
                scheduler: TaskScheduler.Default);

        return promise.Task;
    }
}