using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace DotNetAcmeClient;

public class AsyncDisposeList : IAsyncDisposable
{
    private readonly List<Func<ValueTask>> tasks = new ();

    public AsyncDisposeList Add(Func<ValueTask> task)
    {
        this.tasks.Add(task);
        return this;
    }

    public async ValueTask DisposeAsync()
    {
        List<Exception> exceptions = new ();
        foreach(var task in tasks)
        {
            try {
                await task();
            } catch (Exception ex)
            {
                if (ex is not TaskCanceledException) {
                    exceptions.Add(ex);
                }
            }
        }
        if (exceptions.Any())
        {
            throw new Exception(string.Join("\n",exceptions.Select((x) => x.ToString())));
        }
    }
}