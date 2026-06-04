using Application.AI.Common.Services;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Application.AI.Common.Tests.Services;

public class AmbientRequestScopeTests
{
    private static IServiceProvider NewProvider() => new ServiceCollection().BuildServiceProvider();

    [Fact]
    public void Current_NoScope_IsNull()
    {
        var sut = new AmbientRequestScope();

        sut.Current.Should().BeNull();
    }

    [Fact]
    public void BeginScope_SetsCurrent_AndDisposeClearsIt()
    {
        var sut = new AmbientRequestScope();
        var provider = NewProvider();

        using (sut.BeginScope(provider))
        {
            sut.Current.Should().BeSameAs(provider);
        }

        sut.Current.Should().BeNull();
    }

    [Fact]
    public void BeginScope_Nested_RestoresPreviousOnDispose()
    {
        var sut = new AmbientRequestScope();
        var outer = NewProvider();
        var inner = NewProvider();

        using (sut.BeginScope(outer))
        {
            sut.Current.Should().BeSameAs(outer);
            using (sut.BeginScope(inner))
            {
                sut.Current.Should().BeSameAs(inner);
            }

            sut.Current.Should().BeSameAs(outer);
        }

        sut.Current.Should().BeNull();
    }

    [Fact]
    public async Task Current_FlowsAcrossAwait_IntoChildAsyncContext()
    {
        var sut = new AmbientRequestScope();
        var provider = NewProvider();

        using (sut.BeginScope(provider))
        {
            // The ambient value must survive an await boundary (this is the whole point:
            // it has to reach the provider invoked deep inside agent.RunAsync).
            await Task.Yield();
            var observed = await ObserveAfterDelayAsync(sut);
            observed.Should().BeSameAs(provider);
        }
    }

    [Fact]
    public void BeginScope_NullProvider_Throws()
    {
        var sut = new AmbientRequestScope();

        var act = () => sut.BeginScope(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    private static async Task<IServiceProvider?> ObserveAfterDelayAsync(AmbientRequestScope scope)
    {
        await Task.Delay(10);
        return scope.Current;
    }
}
