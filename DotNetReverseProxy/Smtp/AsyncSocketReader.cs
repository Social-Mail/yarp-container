using System;
using System.IO;
using System.Threading.Tasks;

namespace DotNetReverseProxy.Smtp;

public class AsyncSocketReader
{
    private readonly Stream stream;
    private Memory<byte>? peek;

    private readonly Memory<byte> sep = new Memory<byte>(new byte[] { 13, 10 });

    public AsyncSocketReader(Stream stream)
    {
        this.stream = stream;
    }

    public async Task<string> ReadLineAsync()
    {
        Memory<byte> lineBuffer = new Memory<byte>();
        while (true)
        {
            if(this.peek == null)
            {
                var next = await this.Next();
                this.SetPeek(next);
            }

            var peek = this.peek!.Value;

            // lets handle broken new line first...
            if(lineBuffer.Length > 0 && lineBuffer.Span[lineBuffer.Length-1] == '\r' && peek.Span[0] == '\n')
            {
                this.SetPeek(peek.Length == 1 ? null : peek[1..]);
                return System.Text.Encoding.ASCII.GetString(lineBuffer.Span);
            }

            var index = peek.Span.IndexOf(sep.Span);
            if(index == -1)
            {
                lineBuffer = !lineBuffer.IsEmpty
                    ? lineBuffer.Add(peek.Slice(0, index))
                    : peek.Slice(0,index)
                    ;
                this.SetPeek(new Memory<byte> { });
                continue;
            }

            // ended by \r\n
            if((index + sep.Length) == peek.Length)
            {
                this.SetPeek(null);
                lineBuffer = !lineBuffer.IsEmpty
                    ? lineBuffer.Add(peek.Slice(0, index))
                    : peek.Slice(0, index);
            } else
            {
                lineBuffer = !lineBuffer.IsEmpty
                    ? lineBuffer.Add(peek.Slice(0, index))
                    : peek.Slice(0, index);
                this.SetPeek(peek.Slice(index+ sep.Length));
            }
            break;
        }
        return System.Text.Encoding.ASCII.GetString(lineBuffer.Span);
    }

    private void SetPeek(Memory<byte>? next)
    {
        if(next?.Length == 0)
        {
            this.peek = null;
            return;
        }
        this.peek = next;
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

static class MemoryExtensions
{
    public static Memory<T> Add<T>(this Memory<T> memory, Memory<T> a1)
    {
        var b = new T[memory.Length +  a1.Length];
        memory.CopyTo(b);
        a1.CopyTo(b.AsMemory(memory.Length));
        return b.AsMemory();
    }
}
