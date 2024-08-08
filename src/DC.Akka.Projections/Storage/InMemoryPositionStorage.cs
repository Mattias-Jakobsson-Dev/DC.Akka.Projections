using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Text.Json;
using JetBrains.Annotations;

namespace DC.Akka.Projections.Storage;

public class InMemoryPositionStorage : IProjectionStorage
{
    protected readonly ConcurrentDictionary<object, (Type Type, ReadOnlyMemory<byte> Data)> Documents = new();
    
    public async Task<(TDocument? document, bool requireReload)> LoadDocument<TDocument>(
        object id,
        CancellationToken cancellationToken = default)
    {
        return Documents.TryGetValue(id, out var data) 
            ? ((TDocument?)await DeserializeData(data.Data, data.Type), false) : 
            (default, true);
    }

    public async Task Store(
        IImmutableList<DocumentToStore> toUpsert,
        IImmutableList<DocumentToDelete> toDelete,
        CancellationToken cancellationToken = default)
    {
        foreach (var document in toUpsert)
        {
            var serialized = await SerializeData(document.Document);

            var result = (document.Document.GetType(), serialized);
            
            Documents.AddOrUpdate(document.Id, _ => result, (_, _) => result);
            
            document.Ack();
        }

        foreach (var document in toDelete)
        {
            Documents.TryRemove(document.Id, out _);

            document.Ack();
        }
    }

    [PublicAPI]
    protected static async Task<ReadOnlyMemory<byte>> SerializeData(object data)
    {
        var buffer = new ArrayBufferWriter<byte>();
        await using var writer = new Utf8JsonWriter(buffer);

        JsonSerializer.Serialize(writer, data);

        return buffer.WrittenMemory;
    }

    protected static Task<object?> DeserializeData(ReadOnlyMemory<byte> data, Type? type)
    {
        return Task.FromResult(type == null ? null : JsonSerializer.Deserialize(data.Span, type));
    }
}