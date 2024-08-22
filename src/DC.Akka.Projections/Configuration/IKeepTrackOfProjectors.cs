namespace DC.Akka.Projections.Configuration;

public interface IKeepTrackOfProjectors
{
    void Reset();
    
    Task<IProjectorProxy> GetProjector<TId, TDocument>(
        TId id,
        ProjectionConfiguration configuration)
        where TId : notnull where TDocument : notnull;
}