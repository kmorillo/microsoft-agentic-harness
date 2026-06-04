using Application.AI.Common.MediatRBehaviors;
using Application.AI.Common.Services;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Application.AI.Common.Tests.MediatRBehaviors;

public class AmbientRequestScopeBehaviorTests
{
    [Fact]
    public async Task Handle_EstablishesAmbientScopeDuringNext_AndClearsAfter()
    {
        var ambient = new AmbientRequestScope();
        var requestServices = new ServiceCollection().BuildServiceProvider();
        var sut = new AmbientRequestScopeBehavior<object, string>(ambient, requestServices);

        IServiceProvider? observedDuringNext = null;
        var result = await sut.Handle(
            new object(),
            () =>
            {
                observedDuringNext = ambient.Current;
                return Task.FromResult("handled");
            },
            CancellationToken.None);

        result.Should().Be("handled");
        observedDuringNext.Should().BeSameAs(requestServices); // scope active while the handler runs
        ambient.Current.Should().BeNull();                     // and cleared once the request completes
    }

    [Fact]
    public async Task Handle_ClearsAmbientScope_EvenWhenNextThrows()
    {
        var ambient = new AmbientRequestScope();
        var requestServices = new ServiceCollection().BuildServiceProvider();
        var sut = new AmbientRequestScopeBehavior<object, string>(ambient, requestServices);

        var act = async () => await sut.Handle(
            new object(),
            () => throw new InvalidOperationException("boom"),
            CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
        ambient.Current.Should().BeNull();
    }
}
