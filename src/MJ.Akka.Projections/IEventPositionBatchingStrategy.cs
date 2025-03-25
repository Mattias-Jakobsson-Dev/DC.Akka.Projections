using Akka;
using Akka.Streams.Dsl;

namespace MJ.Akka.Projections;

public interface IEventPositionBatchingStrategy
{
    Source<PositionData, NotUsed> Get(Source<PositionData, NotUsed> source);
}