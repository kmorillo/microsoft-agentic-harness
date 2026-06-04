using Application.AI.Common.Interfaces;

namespace Application.AI.Common.Services;

/// <summary>
/// <see cref="AsyncLocal{T}"/>-backed implementation of <see cref="IAmbientRequestScope"/>.
/// Registered as a singleton; the ambient value flows with the async execution context, so a
/// singleton-cached agent's context providers observe the request scope established by the handler.
/// </summary>
public sealed class AmbientRequestScope : IAmbientRequestScope
{
    private static readonly AsyncLocal<IServiceProvider?> s_current = new();

    /// <inheritdoc />
    public IServiceProvider? Current => s_current.Value;

    /// <inheritdoc />
    public IDisposable BeginScope(IServiceProvider requestServices)
    {
        ArgumentNullException.ThrowIfNull(requestServices);

        var previous = s_current.Value;
        s_current.Value = requestServices;
        return new ScopeToken(previous);
    }

    private sealed class ScopeToken : IDisposable
    {
        private readonly IServiceProvider? _previous;
        private bool _disposed;

        public ScopeToken(IServiceProvider? previous) => _previous = previous;

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            s_current.Value = _previous;
        }
    }
}
