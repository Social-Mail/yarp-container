using System;
using System.Collections.Generic;
using System.Text;

namespace NeuroSpeech.Smtp;

public class FileLineReader
{
    public static async IAsyncEnumerable<string> ReadLinesAsync(Stream fs)
    {
        using StreamReader reader = new StreamReader(fs, Encoding.UTF8);
        string line;
        StringBuilder sb = new StringBuilder();
        while((line = await reader.ReadLineAsync()) != null)
        {
            if(line.EndsWith('\r'))
            {
                sb.Append(line.Substring(0, line.Length - 1));
                var r = sb.ToString();
                sb.Length = 0;
                yield return r;
                continue;
            }
            sb.AppendLine(line);
        }

        if(sb.Length > 0)
        {
            yield return sb.ToString();
        }
    }

}

