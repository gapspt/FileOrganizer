namespace FileOrganizer;

public static class RetryOnFailure
{
    public static int DefaultRetries { get; set; } = 3;
    public static int DefaultDelay { get; set; } = 100;

    public static void Retry(Action f, int? retries = null, int? delay = null)
    {
        retries ??= DefaultRetries;
        while (true) try { f(); return; } catch { if (retries-- == 0) throw; Delay(delay); }
    }
    public static T Retry<T>(Func<T> f, int? retries = null, int? delay = null)
    {
        retries ??= DefaultRetries;
        while (true) try { return f(); } catch { if (retries-- == 0) throw; Delay(delay); }
    }

    public static async ValueTask RetryAsync(Func<ValueTask> f, int? retries = null, int? delay = null)
    {
        retries ??= DefaultRetries;
        while (true) try { await f(); return; } catch { if (retries-- == 0) throw; await DelayAsync(delay); }
    }
    public static async ValueTask RetryAsync(Func<Task> f, int? retries = null, int? delay = null)
    {
        retries ??= DefaultRetries;
        while (true) try { await f(); return; } catch { if (retries-- == 0) throw; await DelayAsync(delay); }
    }
    public static async ValueTask<T> RetryAsync<T>(Func<ValueTask<T>> f, int? retries = null, int? delay = null)
    {
        retries ??= DefaultRetries;
        while (true) try { return await f(); } catch { if (retries-- == 0) throw; await DelayAsync(delay); }
    }
    public static async ValueTask<T> RetryAsync<T>(Func<Task<T>> f, int? retries = null, int? delay = null)
    {
        retries ??= DefaultRetries;
        while (true) try { return await f(); } catch { if (retries-- == 0) throw; await DelayAsync(delay); }
    }

    static void Delay(int? ms)
    {
        if (ms != null && ms > 0)
        {
            Thread.Sleep(ms.Value);
        }
    }
    static ValueTask DelayAsync(int? ms)
    {
        return (ms != null && ms > 0) ?
            new(Task.Delay(ms.Value)) :
            ValueTask.CompletedTask;
    }
}
