using System;
using System.IO;
using System.Threading.Tasks;

namespace DotNetReverseProxy.Smtp;

public class AsyncSocketReader
{
    private readonly Stream stream;
    private readonly Memory<byte> peek;

    public AsyncSocketReader(Stream stream)
    {
        this.stream = stream;
    }

    async string ReadLineAsync()
    {
        var buffer = new MemoryStream();
        while (true)
        {
            if(this.peek.IsEmpty)
            {
                var next = await this.Next();
            }
        }
    }

    async Task<Memory<byte>> Next()
    {
        byte[] buffer = new byte[4096];
        var i = await stream.ReadAsync(buffer, 0, buffer.Length);
        if(i != buffer.Length)
        {
            return buffer.AsMemory(0, i);
        }
        return buffer.AsMemory();
    }

}
