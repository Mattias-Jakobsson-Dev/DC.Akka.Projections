using System.Collections.Immutable;
using FluentAssertions;
using JetBrains.Annotations;
using Xunit;

namespace MJ.Akka.Projections.Tests.ProjectionSequencerTests;

public class When_projecting_slower_events_before_faster_events_for_same_id(
    When_projecting_slower_events_before_faster_events_for_same_id.Fixture fixture)
    : IClassFixture<When_projecting_slower_events_before_faster_events_for_same_id.Fixture>
{
    [Fact]
    public void Then_first_task_should_finish()
    {
        fixture.FirstTaskResponse.Should().NotBeNull();
    }

    [Fact]
    public void Then_second_task_should_finish()
    {
        fixture.SecondTaskResponse.Should().NotBeNull();
    }

    [Fact]
    public void Then_first_task_should_finish_before_second_task_starts()
    {
        fixture
            .FirstTaskResponse!
            .TimeSinceCompleted
            .Should()
            .BeLessThan(fixture
                .SecondTaskResponse!
                .TimeSinceStarted);
    }

    [PublicAPI]
    public class Fixture : ProjectionSequencerBaseFixture
    {
        public AckWithTime? FirstTaskResponse { get; private set; }
        public AckWithTime? SecondTaskResponse { get; private set; }

        protected override IImmutableDictionary<
            string,
            (string documentId, int sortOrder, ImmutableList<TimeSpan> delays)> SetupBatches()
        {
            var id = Guid.NewGuid().ToString();

            return new Dictionary<string, (string, int, ImmutableList<TimeSpan>)>
            {
                ["first"] = (id, 1, ImmutableList.Create(TimeSpan.FromMilliseconds(200))),
                ["second"] = (id, 2, ImmutableList.Create(TimeSpan.Zero))
            }.ToImmutableDictionary();
        }

        protected override Task FinishSetup(Func<string, AckWithTime?> getCompletionTime)
        {
            FirstTaskResponse = getCompletionTime("first");
            SecondTaskResponse = getCompletionTime("second");

            return Task.CompletedTask;
        }
    }
}