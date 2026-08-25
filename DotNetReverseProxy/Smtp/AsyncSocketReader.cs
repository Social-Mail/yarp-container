using System;
using System.Collections.Generic;
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

    public async Task<string> ReadTillLineFeedAsync()
    {
        Memory<byte> lineBuffer = new();
        while (true)
        {
            if (this.peek == null)
            {
                var next = await this.Next();
                this.SetPeek(next);
            }

            var peek = this.peek!.Value;

            var index = peek.Span.IndexOf((byte)10);
            if (index == -1)
            {
                lineBuffer = !lineBuffer.IsEmpty
                    ? lineBuffer.Add(peek.Slice(0, index))
                    : peek.Slice(0, index)
                    ;
                this.ClearPeek();
                continue;
            }

            // ended by \r\n
            if ((index + 1) == peek.Length)
            {
                this.ClearPeek();
                lineBuffer = !lineBuffer.IsEmpty
                    ? lineBuffer.Add(peek.Slice(0, index))
                    : peek.Slice(0, index);
            }
            else
            {
                lineBuffer = !lineBuffer.IsEmpty
                    ? lineBuffer.Add(peek.Slice(0, index))
                    : peek.Slice(0, index);
                this.SetPeek(peek.Slice(index + 1));
            }
            break;
        }
        return System.Text.Encoding.ASCII.GetString(lineBuffer.Span);
    }


    public async Task<string> ReadLineAsync()
    {
        Memory<byte> lineBuffer = new();
        while (true)
        {
            if(this.peek == null)
            {
                var next = await this.Next();
                this.SetPeek(next);
            }

            var peek = this.peek!.Value;

            // lets handle broken new line first...
            if(lineBuffer.Length > 0 && lineBuffer.Span[lineBuffer.Length-1] == 13 && peek.Span[0] == 10)
            {
                if(peek.Length ==1)
                {
                    this.ClearPeek();
                } else
                {
                    this.SetPeek(peek.Slice(1));
                }
                return System.Text.Encoding.ASCII.GetString(lineBuffer.Slice(0, lineBuffer.Length-1).Span);
            }

            var index = peek.Span.IndexOf(sep.Span);
            if(index == -1)
            {
                lineBuffer = !lineBuffer.IsEmpty
                    ? lineBuffer.Add(peek.Slice(0, index))
                    : peek.Slice(0,index)
                    ;
                this.ClearPeek();
                continue;
            }

            // ended by \r\n
            if((index + 2) == peek.Length)
            {
                this.ClearPeek();
                lineBuffer = !lineBuffer.IsEmpty
                    ? lineBuffer.Add(peek.Slice(0, index))
                    : peek.Slice(0, index);
            } else
            {
                lineBuffer = !lineBuffer.IsEmpty
                    ? lineBuffer.Add(peek.Slice(0, index))
                    : peek.Slice(0, index);
                this.SetPeek(peek.Slice(index+ 2));
            }
            break;
        }
        return System.Text.Encoding.ASCII.GetString(lineBuffer.Span);
    }

    private void ClearPeek()
    {
        this.peek = null;
    }

    private void SetPeek(Memory<byte> next)
    {
        if(next.Length == 0)
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
    public static Memory<T> Add<T>(this Memory<T> first, Memory<T> second)
    {
        T[] result = new T[first.Length + second.Length];

        // 2. Copy the elements out using Spans
        first.Span.CopyTo(result.AsSpan());
        second.Span.CopyTo(result.AsSpan(first.Length));

        return result.AsMemory();
    }
}
