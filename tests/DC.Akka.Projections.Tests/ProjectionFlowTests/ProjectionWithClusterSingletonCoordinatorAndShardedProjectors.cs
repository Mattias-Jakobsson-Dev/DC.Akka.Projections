using DC.Akka.Projections.Cluster.Sharding;
using DC.Akka.Projections.Configuration;
using Xunit;

namespace DC.Akka.Projections.Tests.ProjectionFlowTests;

public class ProjectionWithClusterSingletonCoordinatorAndShardedProjectors(
    ClusteredActorSystemSupplier actorSystemHandler)
    : TestProjectionBaseFlowTests<string>(actorSystemHandler), IClassFixture<ClusteredActorSystemSupplier>
{
    protected override IHaveConfiguration<ProjectionSystemConfiguration> Configure(
        IHaveConfiguration<ProjectionSystemConfiguration> config)
    {
        return config
            .AsClusterSingleton()
            .WithSharding();
    }
}