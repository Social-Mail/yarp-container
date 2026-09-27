using MimeKit;
using System;
using System.Text.RegularExpressions;

namespace NeuroSpeech.Smtp;

public class SmtpParser
{
    public static (MailboxAddress address, Dictionary<string,string> options) ParseAddress(string arg)
    {
        var tokenizer = new Tokenizer(arg);

        var address = tokenizer.ExtractTill('>').Trim('<');
        tokenizer.SkipWhitespace();
        Dictionary<string, string> options = new Dictionary<string, string>();
        while(!tokenizer.IsEmpty) {
            var key = tokenizer.ExtractTillOrEnd(' ');
            var value = key;
            var tokens = key.Split('=',2, StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length > 1)
            {
                key = tokens[0];
                value = tokens[1];
            }
            options.Add(key, value);
        }
        var smtpUTF8 = true;

        return (MimeKit.MailboxAddress.Parse(address), options);
    }
}

public class Tokenizer
{
    private string text;
    private int start;

    public string ExtractAll() {
        var n = text.Substring(start);
        this.start = text.Length;
        return n;
    }

    public Tokenizer(string text, int start = 0)
    {
        this.text = text;
        this.start = start;
    }

    public bool IsEmpty => text.Length == start;

    public void SkipWhitespace()
    {
        while (char.IsWhiteSpace(text[start]))
        {
            start++;
        }
    }

    public string ExtractTill(char ch)
    {
        if (!TryExtractTill(ch, out var text))
            throw new IndexOutOfRangeException();
        return text;
    }

    public string ExtractTillOrEnd(char ch)
    {
        if (!TryExtractTill(ch, out var text))
        {
            text = this.text.Substring(start);
            start = this.text.Length;
            return text;
        }
        return text;
    }

    public bool TryExtractTill(char ch, out string extracted)
    {
        int index = this.text.IndexOf(ch, start);
        if (index == -1)
        {
            extracted = default;
            return false;
        }
        extracted = this.text.Substring(start, index - start);
        this.start = index + 1;
        return true;
    }
}
