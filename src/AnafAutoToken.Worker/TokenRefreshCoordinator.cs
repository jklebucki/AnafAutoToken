namespace AnafAutoToken.Worker;

/// <summary>
/// Serializuje odświeżanie tokenu. Bez tego ręczne wywołanie z menedżera mogłoby trafić
/// dokładnie w moment zaplanowanego przebiegu i obie ścieżki wysłałyby do ANAF ten sam
/// refresh token - druga dostałaby odmowę, a rotacja zapisana w bazie mogłaby się rozjechać
/// z tym, co faktycznie honoruje ANAF.
/// </summary>
public sealed class TokenRefreshCoordinator : IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    public bool IsBusy => _gate.CurrentCount == 0;

    /// <summary>Czeka na swoją kolej - używane przez zaplanowany przebieg.</summary>
    public async Task RunAsync(Func<CancellationToken, Task> operation, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);

        try
        {
            await operation(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Nie czeka - używane przez ręczne wywołanie z API, żeby operator dostał od razu
    /// czytelną odpowiedź zamiast wiszącego żądania.
    /// </summary>
    public async Task<T?> TryRunAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken)
        where T : class
    {
        if (!await _gate.WaitAsync(TimeSpan.Zero, cancellationToken))
        {
            return null;
        }

        try
        {
            return await operation(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose() => _gate.Dispose();
}
