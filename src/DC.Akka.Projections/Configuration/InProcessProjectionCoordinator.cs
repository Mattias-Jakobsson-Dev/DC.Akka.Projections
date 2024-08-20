using System.Collections.Immutable;
using Akka.Actor;
using Akka.Util;

namespace DC.Akka.Projections.Configuration;

public class InProcessSingletonProjectionCoordinator(IImmutableDictionary<string, IProjectionProxy> coordinators) 
    : IProjectionsCoordinator
{
    public IImmutableList<IProjectionProxy> GetAll()
    {
        return coordinators.Values.ToImmutableList();
    }

    public IProjectionProxy? Get(string projectionName)
    {
        return coordinators.GetValueOrDefault(projectionName);
    }
    
    public class Setup(ActorSystem actorSystem) : IConfigureProjectionCoordinator
    {
        private readonly Dictionary<string, IProjection> _projections = new();
        
        public void WithProjection(IProjection projection)
        {
            _projections[projection.Name] = projection;
        }

        public Task<IProjectionsCoordinator> Start()
        {
            var result = new Dictionary<string, IProjectionProxy>();
            
            foreach (var projection in _projections)
            {
                var actorName = MurmurHash.StringHash(projection.Value.Name).ToString();
            
                var coordinator = actorSystem.ActorOf(
                    projection.Value.CreateCoordinatorProps(),
                    actorName);
                
                coordinator.Tell(new ProjectionsCoordinator.Commands.Start());

                result[projection.Value.Name] = new ActorRefProjectionProxy(coordinator, projection.Value);
            }

            return Task.FromResult<IProjectionsCoordinator>(
                new InProcessSingletonProjectionCoordinator(result.ToImmutableDictionary()));
        }
    }
}